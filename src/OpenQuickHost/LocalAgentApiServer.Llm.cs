using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost;

/// <summary>
/// OpenAI-compatible local LLM gateway. Uses the existing Agent API listener, auth and
/// saved model service providers. Provider credentials never enter the public payload.
/// </summary>
public sealed partial class LocalAgentApiServer
{
    private static readonly HttpClient LlmUpstreamClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };
    private const int MaxLlmRequestChars = 1_048_576;

    private async Task<bool> TryHandleLlmApiAsync(
        HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (path != "/v1/models" && path != "/v1/chat/completions")
            return false;

        if ((path == "/v1/models" && request.HttpMethod != "GET") ||
            (path == "/v1/chat/completions" && request.HttpMethod != "POST"))
        {
            await WriteLlmErrorAsync(response, 405, "method_not_allowed", "Unsupported method.");
            return true;
        }

        // Agent API authentication has already run before reaching this route.
        // The historical default token is public and must not authorize paid-model usage.
        if (string.IsNullOrWhiteSpace(_token) || _token == "yanzi-local-dev-token")
        {
            await WriteLlmErrorAsync(response, 503, "token_not_configured",
                "Set a private Agent API token in Yanzi settings before using the LLM API.");
            return true;
        }

        // Browser origins are not trusted merely because they can reach localhost.
        var origin = request.Headers["Origin"];
        if (!string.IsNullOrWhiteSpace(origin) &&
            (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
             !IsLoopbackHost(originUri.Host)))
        {
            await WriteLlmErrorAsync(response, 403, "origin_not_allowed",
                "Only local application origins can use the LLM API.");
            return true;
        }

        var settings = AppSettingsStore.Load();
        var providers = (settings.AiServiceProviders ?? [])
            .Where(static provider => provider.IsEnabled)
            .ToArray();

        if (path == "/v1/models")
        {
            var models = providers
                .SelectMany(provider => (provider.Models ?? [])
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new
                    {
                        id = provider.Id + "/" + name,
                        @object = "model",
                        created = 0,
                        owned_by = provider.Name,
                        provider_id = provider.Id
                    }))
                .OrderBy(static model => model.id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await WriteJsonAsync(response, 200, new { @object = "list", data = models });
            return true;
        }

        try
        {
            if (request.ContentLength64 > MaxLlmRequestChars)
            {
                await WriteLlmErrorAsync(response, 413, "request_too_large",
                    "The request exceeds the 1 MiB size limit.");
                return true;
            }

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding,
                detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            var sb = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), _cts.Token)) > 0)
            {
                if (sb.Length + count > MaxLlmRequestChars)
                {
                    await WriteLlmErrorAsync(response, 413, "request_too_large",
                        "The request exceeds the 1 MiB size limit.");
                    return true;
                }
                sb.Append(buffer, 0, count);
            }

            JsonNode? body;
            try { body = JsonNode.Parse(sb.ToString()); }
            catch (JsonException)
            {
                await WriteLlmErrorAsync(response, 400, "invalid_json", "Invalid JSON body.");
                return true;
            }
            if (body is not JsonObject payload || payload["messages"] is not JsonArray messages ||
                messages.Count == 0)
            {
                await WriteLlmErrorAsync(response, 400, "invalid_request",
                    "An OpenAI-compatible messages array is required.");
                return true;
            }

            string? requestedModel;
            try { requestedModel = payload["model"]?.GetValue<string>()?.Trim(); }
            catch (InvalidOperationException)
            {
                await WriteLlmErrorAsync(response, 400, "invalid_model", "model must be a string.");
                return true;
            }
            requestedModel = string.IsNullOrWhiteSpace(requestedModel) ? "default" : requestedModel;
            var (provider, upstreamModel, resolutionError) =
                ResolveLlmModel(settings, providers, requestedModel);
            if (provider == null || string.IsNullOrWhiteSpace(upstreamModel))
            {
                await WriteLlmErrorAsync(response,
                    resolutionError == "ambiguous_model" ? 400 : 404,
                    resolutionError ?? "model_not_found", "Model unavailable or ambiguous. Use an ID from GET /v1/models.");
                return true;
            }

            var type = (provider.ProviderType ?? "").Trim();
            if (type.Equals("OpenAI-Response", StringComparison.OrdinalIgnoreCase))
            {
                await WriteLlmErrorAsync(response, 422, "unsupported_provider_protocol",
                    "The Responses protocol requires a separate adapter; select a chat-completions provider.");
                return true;
            }
            if (string.IsNullOrWhiteSpace(provider.BaseUrl) ||
                (string.IsNullOrWhiteSpace(provider.ApiKey) &&
                 !type.Equals("Ollama", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteLlmErrorAsync(response, 503, "provider_not_configured",
                    "The selected model provider is missing an API URL or credential.");
                return true;
            }

            var stream = payload["stream"] is JsonValue streamValue &&
                         streamValue.TryGetValue<bool>(out var streamFlag) && streamFlag;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(4));
            await ForwardLlmCompletionAsync(request, response, provider,
                upstreamModel, requestedModel, payload, stream, timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!response.OutputStream.CanWrite) return true;
            await WriteLlmErrorAsync(response, 504, "upstream_timeout", "The model request timed out.");
            return true;
        }
        catch (NotSupportedException)
        {
            await WriteLlmErrorAsync(response, 422, "unsupported_payload",
                "This provider adapter supports text-only chat without tool calls.");
            return true;
        }
        catch (HttpRequestException)
        {
            await WriteLlmErrorAsync(response, 502, "upstream_unavailable",
                "Unable to reach the configured model provider.");
            return true;
        }
        catch (JsonException)
        {
            await WriteLlmErrorAsync(response, 502, "invalid_upstream_response",
                "The model provider returned invalid JSON.");
            return true;
        }
        catch (IOException)
        {
            // Most often the caller disconnected during SSE. Do not retry the upstream request.
            try { response.Abort(); } catch { }
            return true;
        }
    }

    private static (AiServiceProviderSettings? provider, string? model, string? error)
        ResolveLlmModel(AppSettings settings, AiServiceProviderSettings[] providers, string requested)
    {
        if (requested == "default")
        {
            var selected = providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, settings.ActiveServiceProviderId, StringComparison.OrdinalIgnoreCase));
            var model = selected?.SelectedModel;
            if (string.IsNullOrWhiteSpace(model)) model = settings.AiModel;
            if (selected != null && !string.IsNullOrWhiteSpace(model) &&
                (selected.Models ?? []).Contains(model, StringComparer.OrdinalIgnoreCase))
                return (selected, model, null);
            return (null, null, "default_model_not_configured");
        }
        foreach (var provider in providers)
        {
            var prefix = provider.Id + "/";
            if (!requested.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var model = requested[prefix.Length..];
            if ((provider.Models ?? []).Contains(model, StringComparer.OrdinalIgnoreCase))
                return (provider, model, null);
            return (null, null, "model_not_found");
        }

        var matches = providers.SelectMany(provider => (provider.Models ?? [])
            .Where(model => string.Equals(model, requested, StringComparison.OrdinalIgnoreCase))
            .Select(model => (provider, model))).ToArray();
        return matches.Length switch
        {
            1 => (matches[0].provider, matches[0].model, null),
            > 1 => (null, null, "ambiguous_model"),
            _ => (null, null, "model_not_found")
        };
    }

    private async Task ForwardLlmCompletionAsync(
        HttpListenerRequest incoming, HttpListenerResponse outgoing,
        AiServiceProviderSettings provider, string model, string publicModel,
        JsonObject payload, bool stream, CancellationToken cancellation)
    {
        var providerType = (provider.ProviderType ?? "").Trim();
        var isAnthropic = providerType.Equals("Anthropic", StringComparison.OrdinalIgnoreCase);
        var isGemini = providerType.Equals("Gemini", StringComparison.OrdinalIgnoreCase);
        var native = isAnthropic || isGemini;

        // Native adapters currently use one completion and synthesize a valid SSE
        // response when requested. OpenAI-compatible sources stream incrementally.
        JsonObject upstreamPayload = native
            ? BuildNativeLlmPayload(payload, model, isAnthropic)
            : (JsonObject)payload.DeepClone();
        if (!native)
        {
            upstreamPayload["model"] = model;
            upstreamPayload["stream"] = stream;
        }

        var endpoint = isGemini
            ? BuildGeminiEndpoint(provider.BaseUrl, model, provider.ApiKey)
            : isAnthropic
                ? BuildNativeEndpoint(provider.BaseUrl, "messages")
                : BuildOpenAiCompletionEndpoint(provider.BaseUrl);

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri.Host))))
        {
            await WriteLlmErrorAsync(outgoing, 422, "invalid_provider_url",
                "Provider API URLs must use HTTPS or local HTTP.");
            return;
        }
        if (IsLoopbackHost(uri.Host) && incoming.Url?.Port == uri.Port)
        {
            await WriteLlmErrorAsync(outgoing, 422, "recursive_route",
                "The model provider cannot point back to this Agent API port.");
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (isAnthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", provider.ApiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else if (!isGemini && !string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }
        if (stream && !native)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(upstreamPayload.ToJsonString(), Encoding.UTF8, "application/json");

        using var upstream = await LlmUpstreamClient.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!upstream.IsSuccessStatusCode)
        {
            await WriteLlmErrorAsync(outgoing, (int)upstream.StatusCode,
                "upstream_error", $"Model provider returned HTTP {(int)upstream.StatusCode}.");
            return;
        }

        if (!native && stream &&
            upstream.Content.Headers.ContentType?.MediaType?.Contains("event-stream",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            outgoing.StatusCode = 200;
            outgoing.ContentType = "text/event-stream; charset=utf-8";
            outgoing.Headers["Cache-Control"] = "no-cache, no-transform";
            outgoing.SendChunked = true;
            await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(cancellation);
            await upstreamStream.CopyToAsync(outgoing.OutputStream, cancellation);
            outgoing.Close();
            return;
        }

        // Non-streaming JSON and the occasional provider that ignores stream=true.
        var upstreamJson = await upstream.Content.ReadAsStringAsync(cancellation);
        using var doc = JsonDocument.Parse(upstreamJson);
        if (!native && !stream)
        {
            await WriteRawLlmJsonAsync(outgoing, upstreamJson, cancellation);
            return;
        }

        var content = native
            ? ExtractNativeLlmText(doc.RootElement, isAnthropic)
            : ExtractOpenAiLlmText(doc.RootElement);
        if (content == null)
        {
            await WriteLlmErrorAsync(outgoing, 502, "empty_upstream_response",
                "The model provider did not return a text completion.");
            return;
        }
        if (stream)
        {
            await WriteOneShotLlmSseAsync(outgoing, publicModel, content, cancellation);
            return;
        }

        var usage = native ? BuildNativeUsage(doc.RootElement, isAnthropic) : null;
        await WriteJsonAsync(outgoing, 200, new
        {
            id = "chatcmpl-yanzi-" + Guid.NewGuid().ToString("N"),
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = publicModel,
            choices = new[] { new { index = 0, message = new { role = "assistant", content },
                finish_reason = "stop" } },
            usage
        });
    }

    private static JsonObject BuildNativeLlmPayload(JsonObject payload, string model, bool anthropic)
    {
        var messages = (JsonArray)payload["messages"]!;
        var system = new StringBuilder();
        var turns = new JsonArray();
        foreach (var messageNode in messages)
        {
            if (messageNode is not JsonObject message)
                throw new JsonException("Invalid message item.");
            var role = message["role"]?.GetValue<string>() ?? "";
            if (message["content"] is not JsonValue contentNode ||
                !contentNode.TryGetValue<string>(out var content))
                throw new NotSupportedException("Native provider adapter currently supports text messages only.");
            if (role is "system" or "developer")
            {
                system.AppendLine(content);
                continue;
            }
            if (role is not ("user" or "assistant"))
                throw new NotSupportedException("Native provider adapter does not support tool messages.");
            turns.Add(anthropic
                ? new JsonObject { ["role"] = role, ["content"] = content }
                : new JsonObject
                {
                    ["role"] = role == "assistant" ? "model" : "user",
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = content })
                });
        }
        if (turns.Count == 0)
            throw new JsonException("No user or assistant messages.");
        if (payload.ContainsKey("tools") || payload.ContainsKey("tool_choice"))
            throw new NotSupportedException("Native provider adapter does not support tool calling yet.");

        if (anthropic)
        {
            var native = new JsonObject
            {
                ["model"] = model,
                ["messages"] = turns,
                ["max_tokens"] = payload["max_tokens"]?.DeepClone() ??
                                 payload["max_completion_tokens"]?.DeepClone() ?? JsonValue.Create(1024)
            };
            if (system.Length > 0) native["system"] = system.ToString();
            if (payload["temperature"] != null) native["temperature"] = payload["temperature"]!.DeepClone();
            return native;
        }
        var gemini = new JsonObject { ["contents"] = turns };
        if (system.Length > 0)
            gemini["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = system.ToString() })
            };
        var generation = new JsonObject();
        if (payload["temperature"] != null) generation["temperature"] = payload["temperature"]!.DeepClone();
        var maxTokens = payload["max_tokens"] ?? payload["max_completion_tokens"];
        if (maxTokens != null) generation["maxOutputTokens"] = maxTokens.DeepClone();
        if (generation.Count > 0) gemini["generationConfig"] = generation;
        return gemini;
    }

    private static string BuildNativeEndpoint(string baseUrl, string segment)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed + "/" + segment : trimmed + "/v1/" + segment;
    }

    private static string BuildGeminiEndpoint(string baseUrl, string model, string key)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (!trimmed.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed += "/v1beta";
        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            model = model["models/".Length..];
        return trimmed + "/models/" + Uri.EscapeDataString(model) +
               ":generateContent?key=" + Uri.EscapeDataString(key);
    }

    private static string BuildOpenAiCompletionEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return trimmed + "/chat/completions";
        return trimmed + "/v1/chat/completions";
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static string? ExtractNativeLlmText(JsonElement json, bool anthropic)
    {
        if (anthropic)
        {
            if (!json.TryGetProperty("content", out var blocks) ||
                blocks.ValueKind != JsonValueKind.Array) return null;
            return string.Concat(blocks.EnumerateArray()
                .Where(b => b.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(b => b.TryGetProperty("text", out var t) ? t.GetString() : ""));
        }
        if (!json.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            return null;
        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array) return null;
        return string.Concat(parts.EnumerateArray()
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : ""));
    }

    private static string? ExtractOpenAiLlmText(JsonElement json)
    {
        if (!json.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;
        var first = choices[0];
        return first.TryGetProperty("message", out var message) &&
               message.TryGetProperty("content", out var text) &&
               text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    }

    private static object? BuildNativeUsage(JsonElement json, bool anthropic)
    {
        var usageName = anthropic ? "usage" : "usageMetadata";
        if (!json.TryGetProperty(usageName, out var usage)) return null;
        var inName = anthropic ? "input_tokens" : "promptTokenCount";
        var outName = anthropic ? "output_tokens" : "candidatesTokenCount";
        var input = usage.TryGetProperty(inName, out var i) && i.TryGetInt32(out var a) ? a : 0;
        var output = usage.TryGetProperty(outName, out var o) && o.TryGetInt32(out var b) ? b : 0;
        return new { prompt_tokens = input, completion_tokens = output, total_tokens = input + output };
    }

    private static async Task WriteRawLlmJsonAsync(HttpListenerResponse response,
        string json, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = 200;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellation);
        response.Close();
    }

    private static async Task WriteOneShotLlmSseAsync(HttpListenerResponse response,
        string model, string content, CancellationToken cancellation)
    {
        var id = "chatcmpl-yanzi-" + Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        response.StatusCode = 200;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache, no-transform";
        response.SendChunked = true;
        var chunk = JsonSerializer.Serialize(new
        {
            id, @object = "chat.completion.chunk", created = timestamp, model,
            choices = new[] { new { index = 0, delta = new { role = "assistant", content },
                finish_reason = (string?)null } }
        });
        var finished = JsonSerializer.Serialize(new
        {
            id, @object = "chat.completion.chunk", created = timestamp, model,
            choices = new[] { new { index = 0, delta = new { },
                finish_reason = "stop" } }
        });
        var bytes = Encoding.UTF8.GetBytes("data: " + chunk + "\n\n" +
            "data: " + finished + "\n\n" + "data: [DONE]\n\n");
        await response.OutputStream.WriteAsync(bytes, cancellation);
        response.Close();
    }

    private static Task WriteLlmErrorAsync(HttpListenerResponse response,
        int status, string code, string message) =>
        WriteJsonAsync(response, status, new
        {
            error = new { message, type = "invalid_request_error", code }
        });
}
