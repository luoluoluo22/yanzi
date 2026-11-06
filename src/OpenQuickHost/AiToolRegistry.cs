using System.Text.Json;

namespace OpenQuickHost;

internal sealed record AiToolCall(string Id, string Name, string Arguments);
internal sealed record AiToolCompletion(string Text, IReadOnlyList<AiToolCall> Calls,
    IReadOnlyList<JsonElement>? OutputItems = null)
{
    public IEnumerable<object> ToItems()
    {
        if (OutputItems != null) { foreach (var item in OutputItems) yield return item; yield break; }
        if (Text.Length > 0) yield return new { role = "assistant", content = Text };
        foreach (var call in Calls)
            yield return new { type = "function_call", call_id = call.Id, name = call.Name, arguments = call.Arguments };
    }
}

internal static class AiToolRegistry
{
    private static object String(string description) => new { type = "string", description };
    private static object Integer(string description, int minimum, int maximum) => new { type = "integer", description, minimum, maximum };
    private static object Tool(string name, string description, Dictionary<string, object> properties, params string[] required) => new
    {
        type = "function", name, description, strict = false,
        parameters = new { type = "object", properties, required, additionalProperties = false }
    };
    // Responses API function tools, matching the Codex wire envelope. Advertise only implemented capabilities.
    public static object[] NativeTools =>
    [
        Tool("exec_command", "Run PowerShell 5.1 with plain pipes (no PTY or sandbox). Returns incremental stdout/stderr and exit_code or session_id. Each call starts a fresh shell; sessions stop when this turn ends. Commands time out after 120 seconds.", new()
        {
            ["cmd"] = String("PowerShell command to execute, without an outer powershell.exe wrapper. Maximum 10000 characters."),
            ["workdir"] = String("Working directory. Defaults to the turn cwd."),
            ["yield_time_ms"] = Integer("Wait before yielding a session ID. Defaults to 10000 ms.", 0, 30000),
            ["max_output_tokens"] = Integer("Approximate output token budget; host conservatively caps output characters at twice this value.", 128, 32000)
        }, "cmd"),
        Tool("write_stdin", "Write to a pipe-based session and read new output. Empty chars only polls. Use the session_id returned in this turn. Interactive terminal UI and Read-Host are unsupported.", new()
        {
            ["session_id"] = String("Session ID returned by exec_command."),
            ["chars"] = String("Optional stdin text. Include newline if the program expects a line."),
            ["yield_time_ms"] = Integer("Wait before yielding output. Defaults to 1000 ms.", 0, 30000),
            ["max_output_tokens"] = Integer("Approximate output token budget; host caps output characters at twice this value.", 128, 32000)
        }, "session_id"),
        Tool("terminate_command", "Stop a command and its descendants in this turn; return its remaining output. This is a host extension for pipe-based cancellation.", new()
        {
            ["session_id"] = String("Session ID to terminate."),
            ["yield_time_ms"] = Integer("Wait for termination output; default 1000 ms.", 0, 30000),
            ["max_output_tokens"] = Integer("Approximate output token budget.", 128, 32000)
        }, "session_id")
    ];
    public static bool IsKnown(string name) => name is "exec_command" or "write_stdin" or "terminate_command";
}
