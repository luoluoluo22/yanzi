using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>Provider-neutral parser for OpenAI-compatible SSE chat-completion streams.</summary>
public static class AiChatSseParser
{
    public static async Task<string> ReadAsync(Stream stream, Action<string> onDelta,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        var output = new StringBuilder();
        var completed = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") { completed = true; break; }
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new IOException(error.TryGetProperty("message", out var msg)
                    ? msg.GetString() ?? "AI SSE 请求失败" : error.ToString());
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finishReason) &&
                finishReason.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(finishReason.GetString()))
                completed = true;
            if (!choice.TryGetProperty("delta", out var delta) ||
                !delta.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String) continue;
            var token = content.GetString();
            if (string.IsNullOrEmpty(token)) continue;
            output.Append(token);
            onDelta(token);
        }
        if (!completed) throw new IOException("供应商流式回复未完成，连接提前断开");
        var result = output.ToString().Trim();
        if (result.Length == 0) throw new IOException("供应商流式响应为空");
        return result;
    }
}
