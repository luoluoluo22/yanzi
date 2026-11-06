using System.ComponentModel;
using System.Text.Json;

namespace OpenQuickHost;

public enum AiToolExecutionState { Running, Succeeded, Returned, Failed, TimedOut, Cancelled, Stopped, Interrupted }

public sealed record AiToolExecutionSnapshot(
    string Name, string Arguments, string Workdir, string Output, string Error,
    AiToolExecutionState State, int? ExitCode, double Seconds, long OmittedChars, int Calls);

/// <summary>Bounded, durable presentation of one operation; never used to execute commands.</summary>
public sealed class AiToolExecution : INotifyPropertyChanged
{
    private const int OutputLimit = 32000;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; private set; }
    public string Arguments { get; private set; }
    public string Workdir { get; private set; } = "";
    public string Output { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string SessionId { get; private set; } = "";
    public AiToolExecutionState State { get; private set; } = AiToolExecutionState.Running;
    public int? ExitCode { get; private set; }
    public double Seconds { get; private set; }
    public long OmittedChars { get; private set; }
    public int Calls { get; private set; }
    public Action? CancelTurn { get; set; }
    private string previewOutput = "", previewError = "";
    public bool IsRunning => State == AiToolExecutionState.Running;
    public bool IsFailure => State is AiToolExecutionState.Failed or AiToolExecutionState.TimedOut or AiToolExecutionState.Interrupted;
    public string DisplayOutput => JoinForDisplay(Output, previewOutput);
    public string DisplayError => JoinForDisplay(Error, previewError);
    public string Title => Name switch
    {
        "exec_command" or "execute_command" => "执行命令",
        "write_stdin" => "读取命令输出", "terminate_command" => "停止命令",
        "query_extensions" => "查询小程序", "run_extension" or "execute_extension" => "运行小程序",
        "create_extension" => "创建小程序", "delete_extension" => "删除小程序", "stop_extension" => "停止小程序",
        _ => Name
    };
    public string Command => ReadString(Arguments, "cmd") ?? ReadString(Arguments, "command") ?? Arguments;
    public string StatusText => State switch
    {
        AiToolExecutionState.Running => "执行中", AiToolExecutionState.Succeeded => "已完成",
        AiToolExecutionState.Returned => "已返回结果", AiToolExecutionState.Failed => "执行失败",
        AiToolExecutionState.TimedOut => "执行超时", AiToolExecutionState.Cancelled => "已取消",
        AiToolExecutionState.Stopped => "已停止", _ => "执行中断"
    };
    public string DetailText => string.Join(" · ", new[]
    {
        Seconds > 0 ? $"{Seconds:0.0} 秒" : "",
        ExitCode.HasValue ? $"退出码 {ExitCode}" : "",
        Calls > 1 ? $"{Calls} 次交互" : "",
        OmittedChars > 0 ? $"已省略 {OmittedChars} 字符" : ""
    }.Where(s => s.Length != 0));

    public AiToolExecution(string name, string arguments)
    {
        Name = name; Arguments = arguments;
        Workdir = ReadString(arguments, "workdir") ?? "";
    }
    internal static string? ReadString(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
    public void SetPreview(string json)
    {
        if (!IsRunning) return;
        previewOutput = ReadString(json, "stdout") ?? "";
        previewError = ReadString(json, "stderr") ?? "";
        Changed();
    }
    public void ApplyResult(string json)
    {
        Calls++;
        // Cancellation has no final stdout payload; keep the last preview.
        if (ReadString(json, "status") == "cancelled")
        {
            AppendOutput(previewOutput, false); AppendOutput(previewError, true);
        }
        previewOutput = previewError = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = ReadString(json, "status");
            if (root.ValueKind != JsonValueKind.Object || status == null)
            {
                AppendOutput(json, false);
                State = json.Contains("失败", StringComparison.Ordinal) ? AiToolExecutionState.Failed : AiToolExecutionState.Returned;
            }
            else
            {
                SessionId = ReadString(json, "session_id") ?? SessionId;
                Workdir = ReadString(json, "workdir") ?? Workdir;
                AppendOutput(ReadString(json, "stdout") ?? "", false);
                AppendOutput(ReadString(json, "stderr") ?? "", true);
                var error = ReadString(json, "error");
                if (error != null) AppendOutput(error, true);
                if (root.TryGetProperty("exit_code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var exit)) ExitCode = exit;
                if (root.TryGetProperty("wall_time_seconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetDouble(out var elapsed) && double.IsFinite(elapsed)) Seconds = Math.Max(Seconds, elapsed);
                if (root.TryGetProperty("omitted_chars", out var omitted) && omitted.ValueKind == JsonValueKind.Number && omitted.TryGetInt64(out var count)) OmittedChars += Math.Max(0, count);
                State = status switch
                {
                    "running" => AiToolExecutionState.Running,
                    "completed" => ExitCode == 0 ? AiToolExecutionState.Succeeded : ExitCode.HasValue ? AiToolExecutionState.Failed : AiToolExecutionState.Returned,
                    "error" => AiToolExecutionState.Failed,
                    "timed_out" => AiToolExecutionState.TimedOut,
                    "cancelled" => AiToolExecutionState.Cancelled,
                    "terminated" or "turn_finished" or "disposed" => AiToolExecutionState.Stopped,
                    _ => AiToolExecutionState.Returned
                };
            }
        }
        catch (JsonException)
        {
            AppendOutput(json, false);
            State = json.Contains("失败", StringComparison.Ordinal) ? AiToolExecutionState.Failed : AiToolExecutionState.Returned;
        }
        Changed();
    }
    public void FinishTurn(bool cancelled)
    {
        if (!IsRunning) return;
        AppendOutput(previewOutput, false); AppendOutput(previewError, true);
        previewOutput = previewError = "";
        State = cancelled ? AiToolExecutionState.Cancelled : AiToolExecutionState.Stopped;
        Changed();
    }
    private void AppendOutput(string text, bool error)
    {
        var value = (error ? Error : Output) + text;
        if (value.Length > OutputLimit)
        {
            OmittedChars += value.Length - OutputLimit;
            value = value[^OutputLimit..];
        }
        if (error) Error = value; else Output = value;
    }
    private static string JoinForDisplay(string previous, string preview)
    {
        var value = previous + preview;
        return value.Length > OutputLimit ? value[^OutputLimit..] : value;
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public AiToolExecutionSnapshot Snapshot() => new(Name, Arguments, Workdir, DisplayOutput, DisplayError, State, ExitCode, Seconds, OmittedChars, Calls);
    public static AiToolExecution Restore(AiToolExecutionSnapshot saved)
    {
        var result = new AiToolExecution(saved.Name, saved.Arguments)
        {
            Workdir = saved.Workdir, State = saved.State == AiToolExecutionState.Running ? AiToolExecutionState.Interrupted : saved.State,
            ExitCode = saved.ExitCode, Seconds = saved.Seconds, OmittedChars = saved.OmittedChars, Calls = saved.Calls
        };
        result.AppendOutput(saved.Output, false); result.AppendOutput(saved.Error, true);
        return result;
    }
    public string BuildContext() => JsonSerializer.Serialize(Snapshot());
}

internal sealed class AiToolTranscript
{
    private readonly Dictionary<string, AiToolExecution> byCall = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AiToolExecution> bySession = new(StringComparer.Ordinal);
    public IEnumerable<AiToolExecution> Executions => byCall.Values.Distinct();
    public AiToolExecution Begin(AiToolCall call)
    {
        var session = AiToolExecution.ReadString(call.Arguments, "session_id");
        var entry = (call.Name is "write_stdin" or "terminate_command") && session != null && bySession.TryGetValue(session, out var known) && known.IsRunning
            ? known : new AiToolExecution(call.Name, call.Arguments);
        byCall[call.Id] = entry;
        return entry;
    }
    public AiToolExecution? Find(string callId) => byCall.GetValueOrDefault(callId);
    public AiToolExecution Apply(AiToolCall call, string result)
    {
        var entry = byCall[call.Id];
        // A failed poll is not evidence that the original process exited.
        if (call.Name is "write_stdin" or "terminate_command" &&
            entry.Name is "exec_command" or "execute_command" &&
            AiToolExecution.ReadString(result, "status") == "error")
            byCall[call.Id] = entry = new AiToolExecution(call.Name, call.Arguments);
        entry.ApplyResult(result);
        if (entry.SessionId.Length > 0) bySession[entry.SessionId] = entry;
        return entry;
    }
    public void Finish(bool cancelled)
    {
        foreach (var entry in Executions) { entry.FinishTurn(cancelled); entry.CancelTurn = null; }
    }
}
