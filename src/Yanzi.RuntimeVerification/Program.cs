using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OpenQuickHost;

if (args.Length >= 1 && args[0] == "--query")
{
    var data = await RuntimeRpc.CallAsync(args.Length > 1 ? args[1] : "status",
        args.Length > 2 ? JsonSerializer.Deserialize<JsonElement>(args[2]) : null);
    Console.WriteLine(JsonSerializer.Serialize(data, RuntimeRpc.Json));
    return;
}
if (args.Length >= 1 && args[0] == "--fake-shell")
{
    var development = args[1] == "dev";
    var callback = RuntimeRpc.PipeName + ".shell." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
    using var server = new RuntimeRpcServer(callback, (operation, _) =>
    {
        File.AppendAllText(args[2], operation + Environment.NewLine);
        return Task.FromResult<object?>(new { handled = true });
    });
    server.Start();
    await RuntimeRpc.CallAsync("shell.attach", new { pid = Environment.ProcessId, pipe = callback, development, active = development });
    while (true)
    {
        await Task.Delay(1000);
        await RuntimeRpc.CallAsync("shell.touch", new { pid = Environment.ProcessId, pipe = callback, development, active = development });
    }
}

var runtimePath = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Supply Yanzi.Runtime.exe path.");
var root = Path.Combine(Path.GetTempPath(), "yanzi-runtime-check-" + Guid.NewGuid().ToString("N"));
var pipeName = "Yanzi.Runtime.Check." + Guid.NewGuid().ToString("N");
Environment.SetEnvironmentVariable("YANZI_RUNTIME_PIPE", pipeName);
Directory.CreateDirectory(root);
var extension = Path.Combine(root, "Extensions", "runtime-resident-test");
Directory.CreateDirectory(extension);
var portReservation = new TcpListener(IPAddress.Loopback, 0);
portReservation.Start();
var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
portReservation.Stop();
File.WriteAllText(Path.Combine(root, "appsettings.local.json"), JsonSerializer.Serialize(new
{
    launchAtStartup = false, refreshCloudOnStartup = false, enableAgentApi = true,
    agentApiPort = port, enableLanSync = false, enableEverything = false, enableAutoUpdate = false,
    launcherHotkey = "", enableWindowSnapAssist = false
}, RuntimeRpc.Json));
File.WriteAllText(Path.Combine(extension, "manifest.json"), """
{"id":"runtime-resident-test","name":"Runtime lifecycle fixture","version":"1.0","runtime":"csharp","uiMode":"native-window","entryMode":"entry","entry":"main.cs","startup":{"mode":"on_app_launch"},"permissions":[],"provides":[{"name":"runtime.verification.ping","inputSchema":{"type":"object"},"outputSchema":{"type":"object"}}]}
""");
File.WriteAllText(Path.Combine(extension, "main.cs"), """
using System;
using System.IO;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;
public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var resident = new Resident();
        context.RegisterObject?.Invoke(context.ExtensionId + "-window", resident);
        context.Capabilities.Register("runtime.verification.ping", _ => Task.FromResult<object?>(new { processId = Environment.ProcessId, instance = resident.Instance }));
        await resident.Done.Task;
        return "stopped";
    }
}
public sealed class Resident
{
    public Guid Instance { get; } = Guid.NewGuid();
    public TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Toggle() { }
    public void Quit() => Done.TrySetResult(true);
}
""");

Process? runtime = null, stable = null, dev = null;
var checks = 0;
void Check(bool condition, string scenario) { if (!condition) throw new InvalidOperationException(scenario); checks++; }
async Task<JsonElement> WaitForAsync(Func<JsonElement, bool> ready)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
    while (DateTimeOffset.UtcNow < deadline)
    {
        try { var value = await RuntimeRpc.CallAsync("status"); if (ready(value)) return value; }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { }
        if (runtime?.HasExited == true) throw new InvalidOperationException("Runtime exited: " + runtime.ExitCode);
        await Task.Delay(300);
    }
    throw new TimeoutException("Runtime readiness timed out. Data root: " + root);
}
Process FakeShell(string mode, string file)
{
    var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    info.ArgumentList.Add("--fake-shell"); info.ArgumentList.Add(mode); info.ArgumentList.Add(file);
    return Process.Start(info)!;
}
try
{
    var start = new ProcessStartInfo(runtimePath) { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add("--runtime"); start.ArgumentList.Add("--tray");
    start.ArgumentList.Add("--runtime-data-root"); start.ArgumentList.Add(root);
    runtime = Process.Start(start)!;
    var initial = await WaitForAsync(s => s.GetProperty("running").EnumerateArray().Any(e => e.GetProperty("extensionId").GetString() == "runtime-resident-test"));
    var instance = initial.GetProperty("instanceId").GetGuid();
    var residentId = initial.GetProperty("running").EnumerateArray().Single(e => e.GetProperty("extensionId").GetString() == "runtime-resident-test").GetProperty("instanceId").GetGuid();
    Check(initial.GetProperty("pid").GetInt32() == runtime.Id, "Background owner is not Runtime");
    Check(initial.GetProperty("backgroundServices").GetProperty("initialized").GetBoolean(), "Background initialization missing");
    using var http = new HttpClient();
    Check((await http.GetAsync($"http://127.0.0.1:{port}/health")).IsSuccessStatusCode, "Runtime API not alive");
    var first = await RuntimeRpc.CallAsync("capability.invoke", new { name = "runtime.verification.ping", payload = new { } });
    var providerInstance = first.GetProperty("instance").GetGuid();
    Check(first.GetProperty("processId").GetInt32() == runtime.Id, "Provider executed outside Runtime");

    var stableCallback = Path.Combine(root, "stable-callback.txt");
    var devCallback = Path.Combine(root, "dev-callback.txt");
    stable = FakeShell("stable", stableCallback);
    dev = FakeShell("dev", devCallback);
    await WaitForAsync(s => s.GetProperty("clients").GetArrayLength() == 2);
    var afterAttach = await RuntimeRpc.CallAsync("status");
    Check(afterAttach.GetProperty("instanceId").GetGuid() == instance, "Attaching shells restarted Runtime");
    dev.Kill(); await dev.WaitForExitAsync();
    await Task.Delay(1500);
    var ping = await RuntimeRpc.CallAsync("capability.invoke", new { name = "runtime.verification.ping", payload = new { } });
    Check(ping.GetProperty("instance").GetGuid() == providerInstance, "Dev exit restarted resident extension");
    Check(!runtime.HasExited, "Dev exit killed Runtime");
    dev.Dispose(); dev = FakeShell("dev", devCallback);
    await WaitForAsync(s => s.GetProperty("clients").EnumerateArray().Any(c => c.GetProperty("pid").GetInt32() == dev.Id));
    stable.Kill(); await stable.WaitForExitAsync();
    dev.Kill(); await dev.WaitForExitAsync();
    var noUi = await RuntimeRpc.CallAsync("capability.invoke", new { name = "runtime.verification.ping", payload = new { } });
    Check(noUi.GetProperty("instance").GetGuid() == providerInstance, "Closing every shell restarted resident provider");
    Check((await http.GetAsync($"http://127.0.0.1:{port}/health")).IsSuccessStatusCode, "API stopped when shells closed");
    var afterExit = await RuntimeRpc.CallAsync("status");
    Check(afterExit.GetProperty("running").EnumerateArray().Single(e => e.GetProperty("extensionId").GetString() == "runtime-resident-test").GetProperty("instanceId").GetGuid() == residentId,
        "Extension registration changed across UI restarts");

    // A malformed/aborted peer must not poison the listener.
    using (var broken = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        await broken.ConnectAsync(3000);
        var invalidLength = BitConverter.GetBytes(int.MaxValue);
        await broken.WriteAsync(invalidLength);
    }
    var survived = await RuntimeRpc.CallAsync("health");
    Check(survived.GetProperty("instanceId").GetGuid() == instance, "Malformed request crashed Runtime");
    try { await RuntimeRpc.CallAsync("unsupported.operation"); throw new Exception("Unknown operation accepted"); }
    catch (InvalidOperationException) { checks++; }
    var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RuntimeRpc.CallAsync("health")));
    Check(responses.All(s => s.GetProperty("instanceId").GetGuid() == instance), "Concurrent clients were not served by one Runtime");
    var descriptors = (await RuntimeRpc.CallAsync("catalog")).Deserialize<RuntimeCommandDescriptor[]>(RuntimeRpc.Json)!;
    var previewDirectory = Path.Combine(Path.GetTempPath(), "yanzi-extension-test", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(previewDirectory);
    File.WriteAllText(Path.Combine(previewDirectory, "main.ps1"), "Write-Output 'editor-preview-ok'");
    try
    {
        var preview = descriptors.Single(c => c.Id == "runtime-resident-test") with
        { Id = "runtime-editor-preview", Directory = previewDirectory, Runtime = "powershell", UiMode = "none", Entry = "main.ps1", WaitForExit = true };
        var result = (await RuntimeRpc.CallAsync("script.execute", new { id = preview.Id, source = "extension-editor-test", preview }))
            .Deserialize<ScriptExecutionResult>(RuntimeRpc.Json)!;
        Check(result.Success && result.Output.Contains("editor-preview-ok"), "Editor test did not run through Runtime");
        var executionId = Guid.NewGuid();
        var marker = Path.Combine(previewDirectory, "started.txt");
        File.WriteAllText(Path.Combine(previewDirectory, "main.ps1"),
            "Set-Content -LiteralPath '" + marker.Replace("'", "''") + "' -Value started; Start-Sleep -Seconds 30; Write-Output finished");
        var executing = RuntimeRpc.CallAsync("script.execute", new { id = preview.Id, source = "extension-editor-test", preview, executionId });
        var startedBy = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!File.Exists(marker) && DateTimeOffset.UtcNow < startedBy) await Task.Delay(100);
        Check(File.Exists(marker), "Cancellation fixture did not start");
        await RuntimeRpc.CallAsync("script.cancel", new { executionId });
        var cancelled = (await executing.WaitAsync(TimeSpan.FromSeconds(5))).Deserialize<ScriptExecutionResult>(RuntimeRpc.Json)!;
        Check(cancelled.IsCancelled, "Cancellation did not reach Runtime script");
        try
        {
            await RuntimeRpc.CallAsync("script.execute", new { id = preview.Id, preview = preview with { Directory = root } });
            throw new Exception("Invalid preview directory accepted");
        }
        catch (InvalidOperationException) { checks++; }
    }
    finally { Directory.Delete(previewDirectory, true); }
    Console.WriteLine($"Shared Runtime verification PASSED: {checks} checks; UI exits/restarts preserved Runtime, extension instance, capability and API.");
}
finally
{
    foreach (var process in new[] { dev, stable, runtime })
    {
        if (process == null) continue;
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } } catch { }
        process.Dispose();
    }
    // Keep failed-run diagnostics. This temporary directory never contains user data.
    Console.WriteLine("Verification data root: " + root);
}
