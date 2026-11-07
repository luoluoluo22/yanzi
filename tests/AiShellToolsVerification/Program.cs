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
            await ExperienceTests();
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
        for (int i = 0; i < 80; i++)
        {
            output.Append(response.GetProperty("stdout").GetString());
            error.Append(response.GetProperty("stderr").GetString());
            if (response.GetProperty("session_id").ValueKind == JsonValueKind.Null) return (response, output.ToString(), error.ToString());
            response = Parse(await manager.InvokeAsync("write_stdin", Json(new { session_id = response.GetProperty("session_id").GetString(), yield_time_ms = 500 }), default));
        }
        throw new Exception("Command failed to complete within test deadline: " + response.GetRawText() + " stdout=" + output + " stderr=" + error);
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

        // Stress the earliest possible stdin write: it may arrive while the
        // PowerShell child is still waiting for the job-assignment handshake.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var fastStart = Parse(await manager.InvokeAsync("exec_command",
                Json(new { cmd = "$line=[Console]::ReadLine(); [Console]::WriteLine('burst:' + $line)", yield_time_ms = 0 }), default));
            var fastInput = Parse(await manager.InvokeAsync("write_stdin",
                Json(new { session_id = fastStart.GetProperty("session_id").GetString(), chars = "rapid\n", yield_time_ms = 0 }), default));
            var fastResult = await Finish(manager, fastInput);
            Check(fastResult.Out.Contains("burst:rapid"), "early stdin write survives handshake buffering #" + (attempt + 1));
        }

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

    private static object Call(string id = "native1", string arguments = "{\"cmd\":\"[Console]::WriteLine('MODEL_ROUNDTRIP')\",\"yield_time_ms\":3000}") =>
        new { type = "function_call", call_id = id, name = "exec_command", arguments, status = "completed" };
    private static object Completed(params object[] output) => new { status = "completed", output };
    private static object Answer(string text) => new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text } } };
    private static async Task ProtocolTests()
    {
        var legacy = "你是燕子电脑端 AI 助手。【可用工具列表】 execute_command ```json";
        var prompt = AiAgentPrompt.Build(legacy, Root);
        Check(!prompt.Contains("```json") && !prompt.Contains("query_extensions") && !prompt.Contains("execute_command"), "legacy prompt and miniapp catalog not injected");
        Check(AiAgentPrompt.NormalizeCustomPrompt(legacy) == "" && AiAgentPrompt.NormalizeCustomPrompt("简洁回答") == "简洁回答", "legacy migration preserves unrelated preferences");
        var backupDir = Path.Combine(Root, "prompt-backup");
        AiAgentPrompt.BackupLegacy(legacy, backupDir); AiAgentPrompt.BackupLegacy(legacy, backupDir);
        Check(Directory.GetFiles(backupDir).Length == 1 && File.ReadAllText(Directory.GetFiles(backupDir)[0]) == legacy, "legacy prompt backup is exact and idempotent");
        Check(AiToolRegistry.NativeTools.Length == 3 && !AiToolRegistry.IsKnown("query_extensions") && !AiToolRegistry.IsKnown("execute_command"), "only implemented native shell tools advertised");
        Check(AiToolCompletionClient.BuildEndpoint("https://unit.test/v1/chat/completions") == "https://unit.test/v1/responses" && AiToolCompletionClient.BuildEndpoint("https://unit.test/v1") == "https://unit.test/v1/responses", "Responses endpoint normalization");
        var parsed = AiToolCompletionClient.ParseResponse(JsonSerializer.Serialize(Completed(Answer("{\"tool\":\"exec_command\",\"cmd\":\"evil\"}"))));
        Check(parsed.Calls.Count == 0, "JSON in assistant text is never executable");
        foreach (var invalid in new[] {
            JsonSerializer.Serialize(new { status = "incomplete", output = new[] { Call() } }),
            JsonSerializer.Serialize(Completed(Call(), Call())),
            JsonSerializer.Serialize(Completed(Call(arguments: "[]"))),
            JsonSerializer.Serialize(Completed(Call(arguments: "{broken"))) })
        {
            try { AiToolCompletionClient.ParseResponse(invalid); throw new Exception("Accepted invalid response"); }
            catch (Exception ex) when (ex is IOException or JsonException) { Check(true, "invalid/incomplete call batch rejected before execution"); }
        }
        var sse = "data: " + JsonSerializer.Serialize(new { type = "response.function_call_arguments.delta", delta = "{broken" }) + "\n\n" +
            "data: " + JsonSerializer.Serialize(new { type = "response.completed", response = Completed(Call()) }) + "\n\n";
        parsed = await AiToolCompletionClient.ReadStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(sse)), default);
        Check(parsed.Calls.Single().Id == "native1", "only complete Responses event authorizes execution");
        try { await AiToolCompletionClient.ReadStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes("data: {\"type\":\"response.function_call_arguments.delta\",\"delta\":\"{}\"}\n\ndata: [DONE]\n")), default); throw new Exception("Accepted incomplete stream"); }
        catch (IOException) { Check(true, "partial SSE call never executes"); }
        int step = 0;
        using var http = new HttpClient(new FakeHandler(async request =>
        {
            var body = Parse(await request.Content!.ReadAsStringAsync());
            Check(body.GetProperty("tools")[0].GetProperty("name").GetString() == "exec_command" && !body.TryGetProperty("messages", out _), "Responses tools and input envelope used");
            if (step++ == 0) return Response(Completed(new { type = "reasoning", id = "r1", encrypted_content = "opaque", summary = Array.Empty<object>() }, Call()));
            var items = body.GetProperty("input").EnumerateArray().ToArray();
            var last = items.Last();
            Check(last.GetProperty("type").GetString() == "function_call_output" && last.GetProperty("call_id").GetString() == "native1", "result linked using call_id");
            Check(last.GetProperty("output").GetString()!.Contains("MODEL_ROUNDTRIP"), "real shell output returned to model");
            Check(items.Any(i => i.TryGetProperty("encrypted_content", out var v) && v.GetString() == "opaque"), "reasoning items preserved across tool rounds");
            return Response(Completed(Answer("Verified command output.")));
        }));
        var client = new AiToolCompletionClient();
        await using var shell = new AiShellSessionManager(Root);
        var progress = new List<AiToolProgress>();
        var finished = await AiAgentTurnRunner.RunAsync([new { role = "user", content = "Run test" }],
            (messages, ct) => client.SendAsync(http, "https://unit.test/v1/responses", "fake", "test", messages, false, ct),
            (call, ct) => shell.InvokeAsync(call.Name, Parse(call.Arguments), ct), progress.Add, default);
        Check(finished && progress.Last().Phase == "answer" && step == 2, "model -> native function call -> real shell -> native result -> model");
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.NotImplemented, HttpStatusCode.BadGateway })
        {
            int requests = 0;
            using var failedHttp = new HttpClient(new FakeHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("unsupported tools") }); }));
            try { await client.SendAsync(failedHttp, "https://unit.test/v1/responses", "fake", "test", [], false, default); throw new Exception("Expected error"); }
            catch (IOException) { Check(requests == 1, "no silent downgrade/retry: " + status); }
        }
        int executes = 0;
        finished = await AiAgentTurnRunner.RunAsync([], (_, _) => Task.FromResult(new AiToolCompletion("", [new("same", "exec_command", "{}")])),
            (_, _) => { executes++; return Task.FromResult("ok"); }, _ => { }, default, maxRounds: 2);
        Check(!finished && executes == 1, "duplicate call IDs cannot repeat side effects");
        var budget = await Finish(shell, Parse(await shell.InvokeAsync("exec_command", Json(new { cmd = "[Console]::Write('x'*5000)", max_output_tokens = 128 }), default)));
        Check(budget.Out.Length <= 256, "native output budget enforced by shell");
    }
    private static async Task ExperienceTests()
    {
        var transcript = new AiToolTranscript();
        var run = new AiToolCall("run", "exec_command", "{\"cmd\":\"Get-Location\"}");
        var execution = transcript.Begin(run);
        transcript.Apply(run, "{\"session_id\":\"session-one\",\"status\":\"running\",\"stdout\":\"first\",\"stderr\":\"\",\"wall_time_seconds\":1}");
        var poll = new AiToolCall("poll", "write_stdin", "{\"session_id\":\"session-one\"}");
        Check(ReferenceEquals(execution, transcript.Begin(poll)), "poll reuses original execution card");
        execution.SetPreview("{\"stdout\":\" second\",\"stderr\":\"\"}");
        Check(execution.DisplayOutput == "first second", "live preview appended without altering committed output");
        transcript.Apply(poll, "{\"status\":\"completed\",\"exit_code\":0,\"stdout\":\" second\",\"stderr\":\"\",\"wall_time_seconds\":2}");
        Check(execution.Output == "first second" && execution.State == AiToolExecutionState.Succeeded && execution.Calls == 2, "final output not duplicated and success requires zero exit");
        var saved = JsonSerializer.Serialize(execution.Snapshot());
        var restored = AiToolExecution.Restore(JsonSerializer.Deserialize<AiToolExecutionSnapshot>(saved)!);
        Check(restored.Output == execution.Output && restored.Command == "Get-Location" && !restored.IsRunning, "execution snapshot round trip retains command and result");
        var running = new AiToolExecution("exec_command", "{}");
        Check(AiToolExecution.Restore(running.Snapshot()).State == AiToolExecutionState.Interrupted, "history never restores a fictitious running process");
        foreach (var pair in new[] { ("completed", 3, AiToolExecutionState.Failed), ("timed_out", -1, AiToolExecutionState.TimedOut), ("cancelled", -1, AiToolExecutionState.Cancelled), ("terminated", -1, AiToolExecutionState.Stopped) })
        {
            var item = new AiToolExecution("exec_command", "{}");
            item.ApplyResult(JsonSerializer.Serialize(new { status = pair.Item1, exit_code = pair.Item2 }));
            Check(item.State == pair.Item3 && !item.IsRunning, "terminal execution state: " + pair.Item1);
        }
        running.SetPreview("{\"stdout\":\"partial\"}");
        running.ApplyResult("{\"status\":\"cancelled\"}");
        Check(running.Output == "partial", "cancelled execution retains last visible output");
        var separate = new AiToolTranscript();
        Check(!ReferenceEquals(execution, separate.Begin(poll)), "execution cards remain scoped to their own turn");
        var activeCall = new AiToolCall("active", "exec_command", "{}");
        var active = transcript.Begin(activeCall);
        transcript.Apply(activeCall, "{\"status\":\"running\",\"session_id\":\"active\"}");
        var badPoll = new AiToolCall("bad-poll", "write_stdin", "{\"session_id\":\"active\",\"chars\":1}");
        transcript.Begin(badPoll);
        var error = transcript.Apply(badPoll, "{\"status\":\"error\",\"error\":\"invalid stdin\"}");
        Check(active.IsRunning && !ReferenceEquals(active, error) && error.State == AiToolExecutionState.Failed, "invalid poll does not misreport original process as exited");
        transcript.Finish(false);
        Check(active.State == AiToolExecutionState.Stopped, "turn end marks outstanding command stopped after disposal");
        var bounded = new AiToolExecution("exec_command", "{}");
        bounded.ApplyResult(JsonSerializer.Serialize(new { status = "completed", exit_code = 0, stdout = new string('x', 100000) }));
        Check(bounded.Output.Length == 32000 && bounded.OmittedChars == 68000, "aggregated UI history bounded independently of per-call budgets");
        await using var shell = new AiShellSessionManager(Root);
        var previews = new List<string>();
        var first = Parse(await shell.InvokeAsync("exec_command", Json(new { cmd = "[Console]::WriteLine('LIVE'); Start-Sleep -Seconds 2", yield_time_ms = 1800 }), default, previews.Add));
        var result = await Finish(shell, first);
        Check(previews.Any(p => p.Contains("LIVE")), "shell emits output preview before returning tool result");
        Check(result.Out.Split("LIVE").Length == 2, "preview does not consume or duplicate model output");
    }

    private static HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
}
