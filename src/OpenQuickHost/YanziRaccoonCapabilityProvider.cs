using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>Loopback Raccoon MCP bridge. Browser access is always routed through Yanzi's authenticated device relay.</summary>
public static class YanziRaccoonCapabilityProvider
{
    private static string Extension => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "Extensions", "raccoon-manager");
    private static string Runtime => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "McpRuntime", "raccoon");
    private static string Bridge => Path.Combine(Runtime, "app", "scripts", "yanzi-bridge.js");
    private static string EnableFile => Path.Combine(Runtime, "remote-control.enabled");

    private static int GetConfiguredPort()
    {
        var path = Path.Combine(Runtime, ".env");
        if (!File.Exists(path)) return 3766;
        try
        {
            var line = File.ReadLines(path).FirstOrDefault(item =>
                item.StartsWith("RACCOON_PORT=", StringComparison.Ordinal));
            if (line != null && int.TryParse(line["RACCOON_PORT=".Length..].Trim(), out var port) &&
                port is >= 1 and <= 65535) return port;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return 3766;
    }

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "raccoon.status", Description = "Check whether the local Raccoon MCP extension and HTTP service are ready.",
            Permissions = ["raccoon.read"], Category = "raccoon",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };
        yield return new()
        {
            Name = "raccoon.tools.list", Description = "List MCP tools on this computer. No remote execution permission required.",
            Permissions = ["raccoon.read"], Category = "raccoon",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = _ => InvokeBridgeAsync("list", null, null)
        };
        yield return new()
        {
            Name = "raccoon.tools.call", Description = "Call an MCP tool on this computer. Requires locally enabled remote control.",
            Permissions = ["raccoon.execute"], Category = "raccoon", RiskLevel = "high", RequiresConfirmation = true,
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"name":{"type":"string","minLength":1},"arguments":{"type":"object"}},"required":["name"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = CallAsync
        };
    }

    private static async Task<object?> StatusAsync(object? _)
    {
        var installed = File.Exists(Path.Combine(Extension, "manifest.json")) && File.Exists(Bridge);
        var ready = false;
        if (installed)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var response = await client.GetAsync($"http://127.0.0.1:{GetConfiguredPort()}/health");
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                ready = response.IsSuccessStatusCode &&
                    data.RootElement.TryGetProperty("name", out var name) && name.GetString() == "raccoon-mcp";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        }
        return new { installed, ready, remoteControlEnabled = File.Exists(EnableFile) };
    }

    private static async Task<object?> CallAsync(object? input)
    {
        var json = (JsonElement)input!;
        var name = json.GetProperty("name").GetString() ?? "";
        if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_.:\-]{0,127}$"))
            throw new ArgumentException("Invalid Raccoon MCP tool name.");
        var args = json.TryGetProperty("arguments", out var parsed) ? parsed.Clone() : JsonSerializer.SerializeToElement(new { });
        if (args.ValueKind != JsonValueKind.Object || args.GetRawText().Length > 65536)
            throw new ArgumentException("Raccoon tool arguments must be an object of at most 64 KiB.");
        if (!File.Exists(EnableFile))
            throw new UnauthorizedAccessException("Remote Raccoon execution is disabled on this computer. Enable it locally in the Raccoon manager.");
        // Do not permit a remote caller to create a second implicit device hop.
        var sanitized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args.GetRawText())
            ?? new Dictionary<string, JsonElement>();
        sanitized.Remove("deviceId");
        sanitized.Remove("deviceName");
        return await InvokeBridgeAsync("call", name, JsonSerializer.SerializeToElement(sanitized));
    }

    private static string ResolveNode()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "Runtimes", "node");
        if (Directory.Exists(root))
        {
            var portable = Directory.GetDirectories(root)
                .Select(folder => new { folder, version = Version.TryParse(Path.GetFileName(folder).TrimStart('v'), out var v) ? v : null })
                .Where(item => item.version != null && File.Exists(Path.Combine(item.folder, "node.exe")))
                .OrderByDescending(item => item.version)
                .FirstOrDefault();
            if (portable != null) return Path.Combine(portable.folder, "node.exe");
        }
        return "node.exe";
    }

    private static async Task<object?> InvokeBridgeAsync(string action, string? name, JsonElement? args)
    {
        if (!File.Exists(Path.Combine(Extension, "manifest.json")) || !File.Exists(Bridge))
            throw new FileNotFoundException("Raccoon MCP is not installed or staged.", Bridge);
        var command = new ProcessStartInfo(ResolveNode())
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Bridge)!,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        command.ArgumentList.Add(Bridge);
        command.Environment["RACCOON_RUNTIME_DIR"] = Runtime;
        using var process = Process.Start(command) ?? throw new InvalidOperationException("Cannot start the Raccoon adapter.");
        var request = JsonSerializer.Serialize(new { action, name, arguments = args ?? JsonSerializer.SerializeToElement(new { }) });
        await process.StandardInput.WriteAsync(request);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Raccoon adapter timed out; do not retry a mutating tool without checking its result.");
        }
        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Raccoon adapter failed: " + stderr[..Math.Min(stderr.Length, 300)]);
        if (stdout.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Raccoon MCP response exceeds the 4 MiB limit.");
        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.Clone();
    }
}
