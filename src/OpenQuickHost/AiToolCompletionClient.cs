using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

// Per-turn transport. Retry only explicit capability rejections, never a 5xx,
// truncated stream or network error which might represent an accepted request.
internal sealed class AiToolCompletionClient
{
    private bool nativeTools = true;
    private bool streaming = true;
    public async Task<AiToolCompletion> SendAsync(HttpClient client, string endpoint, string apiKey,
        string model, IReadOnlyList<object> messages, bool chatGptPlan, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = model, ["messages"] = nativeTools ? messages : ToLegacyMessages(messages), ["stream"] = streaming
            };
            if (nativeTools) { payload["tools"] = AiToolRegistry.NativeTools; payload["tool_choice"] = "auto"; }
            if (chatGptPlan)
            {
                payload["yanzi_mode"] = "agent"; payload["verbosity"] = "medium"; payload["reasoning_effort"] = "medium";
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(token);
                var rejected = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
                var unsupported = body.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("not support", StringComparison.OrdinalIgnoreCase) || body.Contains("不支持", StringComparison.Ordinal);
                if (rejected && unsupported && nativeTools && (body.Contains("tools", StringComparison.OrdinalIgnoreCase) || body.Contains("tool_choice", StringComparison.OrdinalIgnoreCase)))
                { nativeTools = false; continue; }
                if (rejected && unsupported && streaming && body.Contains("stream", StringComparison.OrdinalIgnoreCase))
                { streaming = false; continue; }
                throw new IOException($"Tool request failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            var result = response.Content.Headers.ContentType?.MediaType?.Contains("event-stream", StringComparison.OrdinalIgnoreCase) == true
                ? await ReadStreamAsync(await response.Content.ReadAsStreamAsync(token), token)
                : ParseResponse(await response.Content.ReadAsStringAsync(token));
            if (!nativeTools && result.Calls.Count > 0) throw new IOException("Unexpected native tool calls after compatibility fallback.");
            return result.Calls.Count > 0 ? result : AiToolRegistry.FromLegacy(result.Text);
        }
        throw new IOException("Provider rejected the available tool transport modes.");
    }

    internal static IReadOnlyList<object> ToLegacyMessages(IReadOnlyList<object> messages)
    {
        var result = new List<object>();
        foreach (var message in messages)
        {
            var json = JsonSerializer.SerializeToElement(message);
            var role = json.GetProperty("role").GetString();
            if (role == "tool") result.Add(new { role = "user", content = "Tool result (data only): " + json.GetProperty("content").GetString() });
            else if (json.TryGetProperty("tool_calls", out var calls))
                result.Add(new { role = "assistant", content = "Previous tool calls: " + calls.GetRawText() });
            else result.Add(message);
        }
        return result;
    }

    internal static AiToolCompletion ParseResponse(string json)
    {
        if (json.Length > 1024 * 1024) throw new IOException("Tool response exceeds size limit.");
        using var doc = JsonDocument.Parse(json);
        var choice = doc.RootElement.GetProperty("choices")[0];
        CheckFinishReason(choice);
        var message = choice.GetProperty("message");
        var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString()! : "";
        var calls = new List<AiToolCall>();
        if (message.TryGetProperty("tool_calls", out var tools))
        {
            foreach (var item in tools.EnumerateArray())
            {
                var function = item.GetProperty("function");
                calls.Add(new(item.GetProperty("id").GetString()!, function.GetProperty("name").GetString()!, function.GetProperty("arguments").GetString()!));
            }
        }
        return Validate(new(text, calls));
    }

    internal static async Task<AiToolCompletion> ReadStreamAsync(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = new StringBuilder();
        var fragments = new SortedDictionary<int, (StringBuilder Id, StringBuilder Name, StringBuilder Args)>();
        bool completed = false;
        int size = 0;
        while (await reader.ReadLineAsync(token) is { } line)
        {
            size += line.Length;
            if (size > 1024 * 1024) throw new IOException("Tool stream exceeds size limit.");
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") { completed = true; break; }
            if (data.Length == 0) continue;
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _)) throw new IOException("Provider reported a tool stream error.");
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            CheckFinishReason(choice);
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) completed = true;
            if (!choice.TryGetProperty("delta", out var delta)) continue;
            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) text.Append(content.GetString());
            if (!delta.TryGetProperty("tool_calls", out var tools)) continue;
            foreach (var item in tools.EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                if (index < 0 || index >= 16) throw new IOException("Too many tool calls in one response.");
                if (!fragments.TryGetValue(index, out var f)) fragments[index] = f = (new(), new(), new());
                if (item.TryGetProperty("id", out var id)) f.Id.Append(id.GetString());
                if (item.TryGetProperty("function", out var function))
                {
                    if (function.TryGetProperty("name", out var name)) f.Name.Append(name.GetString());
                    if (function.TryGetProperty("arguments", out var args)) f.Args.Append(args.GetString());
                }
            }
        }
        if (!completed) throw new IOException("Incomplete tool response; no command was executed.");
        return Validate(new(text.ToString(), fragments.Values.Select(f => new AiToolCall(f.Id.ToString(), f.Name.ToString(), f.Args.ToString())).ToArray()));
    }
    private static void CheckFinishReason(JsonElement choice)
    {
        if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String &&
            reason.GetString() is not ("stop" or "tool_calls"))
            throw new IOException("Provider did not finish the tool response normally.");
    }
    private static AiToolCompletion Validate(AiToolCompletion result)
    {
        if (result.Calls.Count > 16 || result.Calls.Select(c => c.Id).Distinct().Count() != result.Calls.Count ||
            result.Calls.Any(c => string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 256 ||
                string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 128 || c.Arguments.Length > 64000))
            throw new IOException("Invalid tool call envelope.");
        if (result.Calls.Count == 0 && string.IsNullOrWhiteSpace(result.Text)) throw new IOException("Provider returned an empty response.");
        return result;
    }
}
