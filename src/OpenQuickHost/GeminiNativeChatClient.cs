using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost;

// Use Google's native request/response format when talking to generativelanguage.googleapis.com.
// Do not send an OpenAI chat/completions request to the /v1beta API.
internal static class GeminiNativeChatClient
{
    internal static bool IsGeminiApi(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) &&
        (uri.AbsolutePath.TrimEnd('/').Equals("/v1beta", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.TrimEnd('/').Equals("/v1beta/openai", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.TrimEnd('/').Equals("/v1", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.TrimEnd('/').Equals("/v1/openai", StringComparison.OrdinalIgnoreCase));

    internal static Uri BuildEndpoint(string baseUrl, string model)
    {
        if (!IsGeminiApi(baseUrl)) throw new ArgumentException("Not a supported Gemini API URL.");
        var uri = new Uri(baseUrl);
        var version = uri.AbsolutePath.Contains("/v1beta", StringComparison.OrdinalIgnoreCase)
            ? "v1beta" : "v1";
        var name = model.Trim();
        if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) name = name[7..];
        if (name.Length == 0) throw new ArgumentException("Gemini model is missing.");
        return new Uri($"https://generativelanguage.googleapis.com/{version}/models/{Uri.EscapeDataString(name)}:generateContent");
    }

    internal static string BuildPayload(IEnumerable<object> messages)
    {
        var contents = new JsonArray();
        var systems = new List<string>();
        foreach (var message in messages)
        {
            var json = JsonSerializer.SerializeToElement(message);
            if (!json.TryGetProperty("role", out var roleElement)) continue;
            var role = roleElement.GetString() ?? "user";
            if (!json.TryGetProperty("content", out var content)) continue;
            var parts = BuildParts(content);
            if (role == "system")
            {
                foreach (var part in parts)
                {
                    if (part is JsonObject obj && obj["text"] is JsonValue text)
                        systems.Add(text.GetValue<string>());
                }
                continue;
            }
            if (parts.Count == 0) continue;
            contents.Add(new JsonObject
            {
                ["role"] = role == "assistant" ? "model" : "user",
                ["parts"] = parts
            });
        }
        var payload = new JsonObject { ["contents"] = contents };
        if (systems.Count > 0)
        {
            payload["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = string.Join("\n", systems) })
            };
        }
        return payload.ToJsonString();
    }

    private static JsonArray BuildParts(JsonElement content)
    {
        var parts = new JsonArray();
        if (content.ValueKind == JsonValueKind.String)
        {
            parts.Add(new JsonObject { ["text"] = content.GetString() });
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(new JsonObject { ["text"] = text.GetString() });
                else if (item.TryGetProperty("image_url", out var image))
                {
                    var url = image.ValueKind == JsonValueKind.String
                        ? image.GetString()
                        : image.TryGetProperty("url", out var imageUrl) ? imageUrl.GetString() : null;
                    if (url is null || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    var marker = url.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
                    if (marker <= 5) continue;
                    parts.Add(new JsonObject { ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = url[5..marker],
                        ["data"] = url[(marker + 8)..]
                    }});
                }
            }
        }
        return parts;
    }

    internal static string ParseAnswer(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new IOException("Gemini returned no candidates.");
        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            throw new IOException("Gemini returned no text parts (possibly blocked or refused).");
        var answer = string.Concat(parts.EnumerateArray()
            .Where(p => p.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            .Select(p => p.GetProperty("text").GetString())).Trim();
        if (answer.Length == 0) throw new IOException("Gemini returned an empty response.");
        return answer;
    }

    internal static async Task<string> SendAsync(HttpClient client, string baseUrl, string key, string model,
        IReadOnlyList<object> messages, CancellationToken cancellationToken, Action<string> log)
    {
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limited.CancelAfter(TimeSpan.FromSeconds(75));
        var endpoint = BuildEndpoint(baseUrl, model);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-goog-api-key", key);
        request.Content = new StringContent(BuildPayload(messages), Encoding.UTF8, "application/json");
        log("native-request-sent");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, limited.Token);
            log($"native-response-status={(int)response.StatusCode} elapsedMs={watch.ElapsedMilliseconds}");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Gemini native API HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    null, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(limited.Token);
            var text = ParseAnswer(body);
            log($"native-completed elapsedMs={watch.ElapsedMilliseconds} chars={text.Length}");
            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && limited.IsCancellationRequested)
        {
            log($"native-timeout elapsedMs={watch.ElapsedMilliseconds}");
            throw new TimeoutException("Gemini 接口等待超时（75秒）。检查网络或代理后可重新发送。");
        }
    }
}
