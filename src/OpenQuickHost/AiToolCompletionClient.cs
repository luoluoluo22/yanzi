using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

// Codex-style Responses API: execute only complete, validated function_call items.
// No extraction of commands from assistant prose and no silent protocol downgrade.
internal sealed class AiToolCompletionClient
{
    private const int ResponseLimit = 4 * 1024 * 1024;
    public static string BuildEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http")) throw new IOException("模型服务地址无效。");
        if (uri.Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
            throw new IOException("智能体需要支持 Responses API 原生工具的模型服务；当前 Gemini 原生地址仅可用于普通聊天。");
        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) path = path[..^17];
        if (!path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            path = path.Length == 0 ? "/v1/responses" : path + "/responses";
        builder.Path = path;
        return builder.Uri.AbsoluteUri;
    }

    public async Task<AiToolCompletion> SendAsync(HttpClient client, string endpoint, string apiKey,
        string model, IReadOnlyList<object> messages, bool chatGptPlan, CancellationToken token)
    {
        var input = messages.Select(ConvertInput).ToArray();
        var payload = new { model, input, tools = AiToolRegistry.NativeTools, tool_choice = "auto",
            stream = true, store = false, parallel_tool_calls = false, include = new[] { "reasoning.encrypted_content" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or
                HttpStatusCode.MethodNotAllowed or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotImplemented)
                throw new IOException($"当前模型服务未接受 Responses API 原生工具请求（HTTP {(int)response.StatusCode}）。请使用支持原生工具的服务，或切换普通聊天。不会降级为执行文本 JSON。");
            throw new IOException($"原生工具请求失败：HTTP {(int)response.StatusCode}。未自动重试。");
        }
        using var stream = await response.Content.ReadAsStreamAsync(token);
        if (response.Content.Headers.ContentType?.MediaType?.Contains("event-stream", StringComparison.OrdinalIgnoreCase) == true)
            return await ReadStreamAsync(stream, token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = new StringBuilder();
        var buffer = new char[8192];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (text.Length + count > ResponseLimit) throw new IOException("Tool response exceeds size limit.");
            text.Append(buffer, 0, count);
        }
        return ParseResponse(text.ToString());
    }

    internal static object ConvertInput(object value)
    {
        var item = JsonSerializer.SerializeToElement(value);
        if (item.TryGetProperty("type", out _)) return item;
        var role = item.GetProperty("role").GetString();
        var content = item.GetProperty("content");
        if (content.ValueKind == JsonValueKind.String) return new { role, content = content.GetString() };
        // Convert historical Chat Completions image/text parts without losing attachments.
        var parts = new List<object>();
        foreach (var part in content.EnumerateArray())
        {
            var type = part.GetProperty("type").GetString();
            if (type == "text") parts.Add(new { type = role == "assistant" ? "output_text" : "input_text", text = part.GetProperty("text").GetString() });
            else if (type == "image_url") parts.Add(new { type = "input_image", image_url = part.GetProperty("image_url").GetProperty("url").GetString() });
            else throw new IOException("Unsupported attachment in Responses API input.");
        }
        return new { role, content = parts };
    }

    internal static AiToolCompletion ParseResponse(string json)
    {
        if (json.Length > ResponseLimit) throw new IOException("Tool response exceeds size limit.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed" ||
            !root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new IOException("模型服务未返回完整的 Responses API 结果；没有执行命令。");
        var text = new StringBuilder();
        var calls = new List<AiToolCall>();
        var items = new List<JsonElement>();
        foreach (var item in output.EnumerateArray())
        {
            items.Add(item.Clone()); // Retain reasoning/encrypted items for the next model turn.
            var type = item.GetProperty("type").GetString();
            if (type == "function_call")
            {
                if (item.TryGetProperty("status", out var state) && state.GetString() != "completed")
                    throw new IOException("Incomplete function call; no command was executed.");
                var call = new AiToolCall(item.GetProperty("call_id").GetString()!, item.GetProperty("name").GetString()!, item.GetProperty("arguments").GetString()!);
                if (string.IsNullOrWhiteSpace(call.Id) || call.Id.Length > 256 ||
                    string.IsNullOrWhiteSpace(call.Name) || call.Name.Length > 128 || call.Arguments.Length > 64000)
                    throw new IOException("Invalid function call envelope.");
                using var args = JsonDocument.Parse(call.Arguments);
                if (args.RootElement.ValueKind != JsonValueKind.Object) throw new IOException("Function arguments must be a JSON object.");
                calls.Add(call);
            }
            else if (type == "message" && item.TryGetProperty("content", out var content))
            {
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var value)) text.Append(value.GetString());
                    else if (part.TryGetProperty("refusal", out var refusal)) text.Append(refusal.GetString());
            }
        }
        if (calls.Count > 16 || calls.Select(c => c.Id).Distinct().Count() != calls.Count)
            throw new IOException("Invalid or duplicate function call IDs.");
        if (calls.Count == 0 && string.IsNullOrWhiteSpace(text.ToString())) throw new IOException("Provider returned no text or function calls.");
        return new(text.ToString(), calls, items);
    }

    internal static async Task<AiToolCompletion> ReadStreamAsync(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        int size = 0;
        // Only response.completed is authoritative. Partial argument deltas never execute.
        while (await reader.ReadLineAsync(token) is { } line)
        {
            size += line.Length;
            if (size > ResponseLimit) throw new IOException("Tool stream exceeds size limit.");
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            if (data.Length == 0) continue;
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) continue;
            if (type.GetString() == "response.completed") return ParseResponse(root.GetProperty("response").GetRawText());
            if (type.GetString() is "error" or "response.failed" or "response.incomplete")
                throw new IOException("模型服务中断了原生工具响应；没有执行命令。");
        }
        throw new IOException("Incomplete Responses API stream; no command was executed.");
    }
}
