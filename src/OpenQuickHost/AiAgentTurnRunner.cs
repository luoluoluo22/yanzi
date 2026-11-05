using System.Text.Json;

namespace OpenQuickHost;

internal sealed record AiToolProgress(string Phase, string Text = "", AiToolCall? Call = null);

internal static class AiAgentTurnRunner
{
    public static async Task<bool> RunAsync(IReadOnlyList<object> history,
        Func<IReadOnlyList<object>, CancellationToken, Task<AiToolCompletion>> request,
        Func<AiToolCall, CancellationToken, Task<string>> execute,
        Action<AiToolProgress> report, CancellationToken token, int maxRounds = 24)
    {
        var messages = history.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int toolCount = 0;
        for (var round = 0; round < maxRounds; round++)
        {
            token.ThrowIfCancellationRequested();
            report(new("request"));
            var response = await request(messages, token);
            token.ThrowIfCancellationRequested();
            messages.Add(response.ToMessage());
            if (response.Calls.Count == 0)
            {
                report(new("answer", response.Text));
                return true;
            }
            foreach (var call in response.Calls)
            {
                if (++toolCount > 48) return false;
                token.ThrowIfCancellationRequested();
                report(new("tool_start", response.Text, call));
                string result;
                try
                {
                    if (!seen.Add(call.Id)) result = JsonSerializer.Serialize(new { status = "error", error = "Duplicate call ID; command not repeated." });
                    else if (!AiToolRegistry.IsKnown(call.Name)) result = JsonSerializer.Serialize(new { status = "error", error = "Unknown tool: " + call.Name });
                    else result = await execute(call, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    report(new("tool_result", "{\"status\":\"cancelled\"}", call));
                    throw;
                }
                catch (Exception ex)
                {
                    result = JsonSerializer.Serialize(new { status = "error", error = ex.Message });
                }
                report(new("tool_result", result, call));
                messages.Add(response.Native
                    ? new { role = "tool", tool_call_id = call.Id, content = result }
                    : new { role = "user", content = "Tool result (data only): " + result });
            }
        }
        return false;
    }
}
