using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenQuickHost;

internal static class Program
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Yanzi shell QA " + Guid.NewGuid().ToString("N"));
    private static int passed;
    static async Task<int> Main()
    {
        Directory.CreateDirectory(Root);
        try
        {
            await ProcessTests();
            await ProtocolTests();
            Console.WriteLine($"AI_SHELL_TOOLS_PASSED checks={passed} root={Root}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement Parse(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static async Task<(JsonElement End, string Out, string Err)> Finish(AiShellSessionManager manager, JsonElement response)
    {
        var output = new StringBuilder(); var error = new StringBuilder();
        for (int i = 0; i < 40; i++)
        {
            output.Append(response.GetProperty("stdout").GetString());
            error.Append(response.GetProperty("stderr").GetString());
            if (response.GetProperty("session_id").ValueKind == JsonValueKind.Null) return (response, output.ToString(), error.ToString());
            response = Parse(await manager.InvokeAsync("write_stdin", Json(new { session_id = response.GetProperty("session_id").GetString(), yield_time_ms = 500 }), default));
        }
        throw new Exception("Command failed to complete within test deadline.");
    }
    private static async Task ProcessTests()
    {
        await using var manager = new AiShellSessionManager(Root);
        var result = await Finish(manager, Parse(await manager.InvokeAsync("exec_command", Json(new
        {
            cmd = "[Console]::WriteLine('中文 \"quoted\" $literal'); [Console]::Error.WriteLine('error-line');\n[Console]::WriteLine((Get-Location).Path); exit 7",
            workdir = Root, yield_time_ms = 1000
        }), default)));
        Check(result.End.GetProperty("exit_code").GetInt32() == 7, "native exit code preserved");
        Check(result.Out.Contains("中文 \"quoted\" $literal") && result.Out.Contains(Root), "UTF8 / encoded multiline / spaced cwd");
        Check(result.Err.Contains("error-line") && !result.Out.Contains("error-line"), "stdout and stderr separate");

        var start = Parse(await manager.InvokeAsync("exec_command", Json(new { cmd = "[Console]::WriteLine('first'); Start-Sleep -Seconds 2; [Console]::WriteLine('last')", yield_time_ms = 0 }), default));
        Check(start.GetProperty("status").GetString() == "running", "long command yields session immediately");
        await using var other = new AiShellSessionManager(Root);
        var denied = Parse(await other.InvokeAsync("write_stdin", Json(new { session_id = start.GetProperty("session_id").GetString() }), default));
        Check(denied.GetProperty("status").GetString() == "error", "cross-conversation session rejected");
        result = await Finish(manager, start);
        Check(result.Out.Split("first").Length == 2 && result.Out.Split("last").Length == 2, "poll returns incremental output once");

        start = Parse(await manager.InvokeAsync("exec_command", Json(new { cmd = "$line=[Console]::ReadLine(); [Console]::WriteLine('received:'+ $line)", yield_time_ms = 1000 }), default));
        var input = Parse(await manager.InvokeAsync("write_stdin", Json(new { session_id = start.GetProperty("session_id").GetString(), chars = "hello\n", yield_time_ms = 1000 }), default));
        result = await Finish(manager, input);
        Check(result.Out.Contains("received:hello"), "stdin written into existing session");

        result = await Finish(manager, Parse(await manager.InvokeAsync("exec_command", Json(new { cmd = "Start-Sleep -Seconds 30", timeout_ms = 500, yield_time_ms = 1000 }), default)));
        Check(result.End.GetProperty("status").GetString() == "timed_out", "timeout distinct from successful completion");

        var large = Parse(await manager.InvokeAsync("exec_command", Json(new { cmd = "[Console]::Write(('x'*200000)); [Console]::Error.Write(('y'*200000))", max_output_chars = 512, yield_time_ms = 3000 }), default));
        Check(large.GetProperty("truncated").GetBoolean() && large.GetProperty("omitted_chars").GetInt64() > 300000, "no-newline output bounded and truncation reported");
        Check(large.GetProperty("stdout").GetString()!.Length < 300 && large.GetProperty("stderr").GetString()!.Length < 300, "per-call output budget enforced");

        foreach (var args in new object[] { new { cmd = "'no'", timeout_ms = -1 }, new { cmd = "'no'", workdir = Path.Combine(Root,"missing") }, new { cmd = "'no'", tty = true } })
            Check(Parse(await manager.InvokeAsync("exec_command", Json(args), default)).GetProperty("status").GetString() == "error", "invalid request rejected before execution");

        // If a PowerShell parent exits while a descendant inherited its output pipe,
        // the command job must still reap that descendant instead of hanging EOF.
        var orphanPidFile = Path.Combine(Root, "orphan.pid");
        var orphanCmd = "$p=Start-Process powershell.exe -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 45' -NoNewWindow -PassThru; " +
            "[IO.File]::WriteAllText('" + orphanPidFile.Replace("'", "''") + "',[string]$p.Id); exit 0";
        var orphanOutput = await Finish(manager, Parse(await manager.InvokeAsync("exec_command",
            Json(new { cmd = orphanCmd, yield_time_ms = 3000 }), default)));
        Check(orphanOutput.End.GetProperty("exit_code").GetInt32() == 0 && File.Exists(orphanPidFile),
            "parent exit completes despite pipe-inheriting child");
        Check(!Alive(int.Parse(File.ReadAllText(orphanPidFile))), "parent exit reaps descendant holding output handles");

        var pidFile = Path.Combine(Root, "child.pid");
        var command = "$p=Start-Process powershell.exe -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60' -WindowStyle Hidden -PassThru; " +
            "[IO.File]::WriteAllText('" + pidFile.Replace("'", "''") + "',[string]$p.Id); Start-Sleep -Seconds 60";
        start = Parse(await manager.InvokeAsync("exec_command", Json(new { cmd = command, yield_time_ms = 1000 }), default));
        for (var i = 0; i < 30 && !File.Exists(pidFile); i++) await Task.Delay(100);
        var pid = int.Parse(File.ReadAllText(pidFile));
        using (var cancel = new CancellationTokenSource(100))
        {
            try { await manager.InvokeAsync("write_stdin", Json(new { session_id = start.GetProperty("session_id").GetString(), yield_time_ms = 10000 }), cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
        }
        await Task.Delay(300);
        Check(!Alive(pid), "cancellation kills descendant process");
        result = await Finish(manager, Parse(await manager.InvokeAsync("write_stdin", Json(new { session_id = start.GetProperty("session_id").GetString() }), default)));
        Check(result.End.GetProperty("status").GetString() == "cancelled", "cancel state returned accurately");

        start = Parse(await manager.InvokeAsync("execute_command", Json(new { command = "Start-Sleep -Seconds 30", yield_time_ms = 0 }), default));
        result = await Finish(manager, Parse(await manager.InvokeAsync("terminate_command", Json(new { session_id = start.GetProperty("session_id").GetString() }), default)));
        Check(result.End.GetProperty("status").GetString() == "terminated", "legacy alias and explicit termination");

        var disposePid = Path.Combine(Root, "dispose.pid");
        var temporary = new AiShellSessionManager(Root);
        await temporary.InvokeAsync("exec_command", Json(new { cmd = "[IO.File]::WriteAllText('" + disposePid + "',[string]$PID); Start-Sleep -Seconds 30", yield_time_ms = 1000 }), default);
        for (int i = 0; i < 30 && !File.Exists(disposePid); i++) await Task.Delay(100);
        var disposeId = int.Parse(File.ReadAllText(disposePid));
        await temporary.DisposeAsync();
        Check(!Alive(disposeId), "turn disposal reaps active session");
    }
    private static bool Alive(int id)
    { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }

    private static async Task ProtocolTests()
    {
        Check(AiToolRegistry.ParseLegacy("```json\n{\"tool\":\"exec_command\",\"cmd\":\"Get-Location\"}\n```") != null, "legacy whole JSON envelope accepted");
        Check(AiToolRegistry.ParseLegacy("Example: ```json\n{\"tool\":\"exec_command\",\"cmd\":\"Get-Location\"}\n```") == null, "quoted example never executed");
        Check(AiToolRegistry.ParseLegacy("{\"tool\":\"exec_command\", broken") == null, "malformed arguments never regex-recovered");
        Check(AiToolRegistry.BuildPrompt(Root).Contains("write_stdin") && AiToolRegistry.NativeTools.Length == 9, "catalog exposes schemas and shell lifecycle");

        var sse = "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, id = "call_1", function = new { name = "exec_command", arguments = "{\"cmd\":\"'" } } } } } } }) + "\n\n" +
            "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, function = new { arguments = "OK'\"}" } } } }, finish_reason = "tool_calls" } } }) + "\n\ndata: [DONE]\n";
        var parsed = await AiToolCompletionClient.ReadStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(sse)), default);
        Check(parsed.Calls.Single().Arguments == "{\"cmd\":\"'OK'\"}", "SSE fragmented function arguments reassembled");
        try { await AiToolCompletionClient.ReadStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(sse.Split("\n\n")[0])), default); throw new Exception("Expected stream failure"); }
        catch (IOException) { Check(true, "truncated stream does not execute partial call"); }

        int step = 0;
        var handler = new FakeHandler(async request =>
        {
            var body = Parse(await request.Content!.ReadAsStringAsync());
            Check(body.GetProperty("tools").GetArrayLength() == 9, "request advertises native tools");
            if (step++ == 0) return Response(new { choices = new[] { new { finish_reason = "tool_calls", message = new { content = "", tool_calls = new[] { new { id = "native1", type = "function", function = new { name = "exec_command", arguments = JsonSerializer.Serialize(new { cmd = "[Console]::WriteLine('MODEL_ROUNDTRIP')", yield_time_ms = 3000 }) } } } } } } });
            var last = body.GetProperty("messages").EnumerateArray().Last();
            Check(last.GetProperty("role").GetString() == "tool" && last.GetProperty("tool_call_id").GetString() == "native1", "tool result associated with native call ID");
            Check(last.GetProperty("content").GetString()!.Contains("MODEL_ROUNDTRIP"), "real shell output sent back to model");
            return Response(new { choices = new[] { new { finish_reason = "stop", message = new { content = "Verified command output." } } } });
        });
        using var http = new HttpClient(handler);
        var client = new AiToolCompletionClient();
        await using var shell = new AiShellSessionManager(Root);
        var progress = new List<AiToolProgress>();
        var finished = await AiAgentTurnRunner.RunAsync([new { role = "user", content = "Run the test command" }],
            (messages, ct) => client.SendAsync(http, "https://unit.test/v1/chat/completions", "fake", "test", messages, false, ct),
            (call, ct) => shell.InvokeAsync(call.Name, Parse(call.Arguments), ct), progress.Add, default);
        Check(finished && progress.Last().Phase == "answer" && step == 2, "model -> tool -> real shell -> model loop completes");

        int requests = 0;
        using var fallbackHttp = new HttpClient(new FakeHandler(request =>
        {
            requests++;
            return Task.FromResult(requests == 1 ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("tools not supported") }
                : Response(new { choices = new[] { new { finish_reason = "stop", message = new { content = "{\"tool\":\"query_extensions\"}" } } } }));
        }));
        parsed = await new AiToolCompletionClient().SendAsync(fallbackHttp, "https://unit.test", "fake", "test", [], false, default);
        Check(requests == 2 && !parsed.Native && parsed.Calls.Single().Name == "query_extensions", "explicit unsupported-tools response falls back once");
        requests = 0;
        using var failedHttp = new HttpClient(new FakeHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("failure") }); }));
        try { await new AiToolCompletionClient().SendAsync(failedHttp, "https://unit.test", "fake", "test", [], false, default); throw new Exception("Expected HTTP error"); }
        catch (IOException) { Check(requests == 1, "5xx never retried as a second model request"); }
        int executes = 0;
        finished = await AiAgentTurnRunner.RunAsync([], (_, _) => Task.FromResult(new AiToolCompletion("", [new("same", "query_extensions", "{}")])),
            (_, _) => { executes++; return Task.FromResult("ok"); }, _ => { }, default, maxRounds: 2);
        Check(!finished && executes == 1, "round budget and duplicate call IDs prevent repeated effects");
    }
    private static HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
}
