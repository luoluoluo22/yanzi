using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// Discovers trusted, release-bundled service manifests rather than hardcoding
/// individual background-service providers into the host registration list.
/// A missing capability triggers a pinned Git checkout and its install lifecycle.
/// A running daemon is never stopped when the Yanzi GUI exits.
/// </summary>
internal static class YanziServiceModuleCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Local = new(new HttpClientHandler
    {
        UseProxy = false, AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private sealed class Module
    {
        public string Id { get; set; } = "";
        public string Repository { get; set; } = "";
        public string Revision { get; set; } = "";
        public string InstallScript { get; set; } = "";
        public string ManagedTaskName { get; set; } = "";
        public string ManagedEntryPoint { get; set; } = "";
        public string ManagedStopRoute { get; set; } = "";
        public string LocalDirectory { get; set; } = "";
        public string TokenHeader { get; set; } = "";
        public string TokenFile { get; set; } = "";
        public string ConfigFile { get; set; } = "";
        public string PortField { get; set; } = "";
        public List<ModuleCapability> Capabilities { get; set; } = [];
    }

    private sealed class ModuleCapability
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Method { get; set; } = "GET";
        public string Route { get; set; } = "";
        public string Category { get; set; } = "service";
        public List<string> Permissions { get; set; } = [];
        public Dictionary<string, string>? Project { get; set; }
    }

    private static IEnumerable<string> CatalogDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "ServiceModules");
        // Additional trusted manifests may be installed separately from Yanzi upgrades.
        yield return Path.Combine(LocalRoot, "OpenQuickHost", "ServiceModules");
    }
    private static string LocalRoot => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static IEnumerable<Module> Manifests()
    {
        foreach (var file in CatalogDirectories().Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.json", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Module? module = null;
            try { module = JsonSerializer.Deserialize<Module>(File.ReadAllText(file), Json); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                HostAssets.AppendLog("[ServiceModules] Invalid manifest " + Path.GetFileName(file) + ": " + ex.Message);
            }
            if (module != null && Valid(module)) yield return module;
        }
    }

    private static bool Valid(Module m) =>
        Regex.IsMatch(m.Id, "^[a-z0-9][a-z0-9-]{2,63}$") &&
        Regex.IsMatch(m.LocalDirectory, "^[a-zA-Z][a-zA-Z0-9-]{2,80}$") &&
        // Immutable source revision; never automatically execute mutable branch HEAD.
        Regex.IsMatch(m.Revision, "^[a-f0-9]{40}$") &&
        m.Repository.StartsWith("https://github.com/luoluoluo22/", StringComparison.OrdinalIgnoreCase) &&
        m.Repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(m.InstallScript, @"^scripts/[a-zA-Z0-9-]+\.ps1$") &&
        Regex.IsMatch(m.ManagedTaskName, @"^[A-Za-z][A-Za-z0-9-]{2,100}$") &&
        Regex.IsMatch(m.ManagedEntryPoint, @"^app/[A-Za-z0-9/.-]+\.(?:mjs|js|exe)$") &&
        !m.ManagedEntryPoint.Split('/').Any(s => s is "." or "..") &&
        (string.IsNullOrEmpty(m.ManagedStopRoute) || Regex.IsMatch(m.ManagedStopRoute, @"^/v1/[A-Za-z0-9/-]{1,100}$")) &&
        Regex.IsMatch(m.TokenHeader, "^[a-zA-Z0-9-]{5,80}$") &&
        Regex.IsMatch(m.TokenFile, "^[a-zA-Z0-9.-]+$") &&
        Regex.IsMatch(m.ConfigFile, "^[a-zA-Z0-9.-]+$") &&
        Regex.IsMatch(m.PortField, "^[a-zA-Z0-9]+$") &&
        m.Capabilities.Count is > 0 and < 50 &&
        m.Capabilities.All(c =>
            Regex.IsMatch(c.Name, @"^[a-z][a-z0-9._-]{2,100}$") &&
            Regex.IsMatch(c.Route, @"^/v1/[a-zA-Z0-9/._-]{1,130}$") &&
            c.Method is "GET" or "POST" &&
            c.Permissions.All(p => Regex.IsMatch(p, @"^[a-z.]+$")));

    private static string DirectoryFor(Module m) => Path.Combine(LocalRoot, m.LocalDirectory);
    private static bool Installed(Module m)
    {
        var root = DirectoryFor(m);
        return File.Exists(Path.Combine(root, m.ConfigFile)) &&
               File.Exists(Path.Combine(root, m.TokenFile));
    }

    public static IReadOnlyList<object> AvailableModules() => Manifests()
        .Select(m => (object)new
        {
            id = m.Id, installed = Installed(m),
            source = m.Repository, revision = m.Revision,
            capabilities = m.Capabilities.Select(c => new { c.Name, c.Description, c.Permissions })
        }).ToArray();

    public static void RegisterInstalled()
    {
        foreach (var module in Manifests())
            if (Installed(module)) Register(module);
    }

    public static bool IsKnownCapability(string capability) =>
        Manifests().Any(m => m.Capabilities.Any(c => c.Name.Equals(capability, StringComparison.OrdinalIgnoreCase)));

    public static async Task<bool> EnsureCapabilityAsync(string capability,
        YanziCapabilityCaller? caller = null, CancellationToken token = default)
    {
        var module = Manifests().FirstOrDefault(m =>
            m.Capabilities.Any(c => c.Name.Equals(capability, StringComparison.OrdinalIgnoreCase)));
        if (module == null) return false;
        var definition = module.Capabilities.First(c => c.Name.Equals(capability, StringComparison.OrdinalIgnoreCase));
        if (caller is { IsTrusted: false } &&
            definition.Permissions.Any(p => !caller.Permissions.Contains(p, StringComparer.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("service_module_permission_denied");

        var gate = Gates.GetOrAdd(module.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            if (!Installed(module))
                await InstallAsync(module, token);
            Register(module);
            return true;
        }
        finally { gate.Release(); }
    }

    public static async Task EnsureExtensionDependenciesAsync(string? extensionDirectory,
        IReadOnlyList<string>? grantedPermissions = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(extensionDirectory)) return;
        var file = Path.Combine(extensionDirectory, "manifest.json");
        if (!File.Exists(file)) return;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file, token));
        var root = document.RootElement;
        if (!root.TryGetProperty("capabilities", out var caps) ||
            caps.ValueKind != JsonValueKind.Object ||
            !caps.TryGetProperty("requires", out var requires) ||
            requires.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in requires.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String) continue;
            var name = entry.GetString() ?? "";
            if (IsKnownCapability(name))
                await EnsureCapabilityAsync(name,
                    new YanziCapabilityCaller("extension", grantedPermissions ?? []), token);
        }
    }

    private static void Register(Module module)
    {
        var definitions = module.Capabilities.Select(capability => new YanziCapabilityProviderDefinition
        {
            Name = capability.Name, Description = capability.Description,
            Category = capability.Category, Permissions = capability.Permissions,
            RiskLevel = capability.Method == "POST" ? "medium" : "low",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = payload => InvokeAsync(module, capability, payload)
        });
        YanziCapabilityProviderSdk.RegisterProvider(module.Id, definitions);
    }

    private static async Task<object?> InvokeAsync(Module module, ModuleCapability capability, object? body)
    {
        var dir = DirectoryFor(module);
        var token = (await File.ReadAllTextAsync(Path.Combine(dir, module.TokenFile))).Trim();
        if (!Regex.IsMatch(token, "^[a-f0-9]{64}$", RegexOptions.IgnoreCase))
            throw new IOException("service_module_invalid_control_token");
        using var cfg = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, module.ConfigFile)));
        if (!cfg.RootElement.TryGetProperty(module.PortField, out var portElement) ||
            !portElement.TryGetInt32(out var port) || port is < 1024 or > 65535)
            throw new IOException("service_module_invalid_local_port");

        using var request = new HttpRequestMessage(new HttpMethod(capability.Method),
            $"http://127.0.0.1:{port}{capability.Route}");
        request.Headers.TryAddWithoutValidation(module.TokenHeader, token);
        if (capability.Method == "POST")
        {
            // No unvalidated remote URLs or caller-provided control routes.
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }
        using var response = await Local.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        if (!result.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new IOException("service_module_request_failed");

        if (capability.Project is null) return result.RootElement.Clone();
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, expr) in capability.Project)
        {
            if (expr == "$true") { output[name] = true; continue; }
            if (expr == "$false") { output[name] = false; continue; }
            if (expr == "$module") { output[name] = module.Id; continue; }
            JsonElement node = result.RootElement;
            var found = true;
            foreach (var segment in expr.Split('.'))
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(segment, out node))
                { found = false; break; }
            }
            output[name] = found ? node.Clone() : null;
        }
        return output;
    }

    private static async Task InstallAsync(Module module, CancellationToken token)
    {
        // Record ownership before starting an external installer; interrupted installs stay accountable.
        YanziManagedServiceInstallations.Begin(module.Id, module.Repository, module.Revision,
            module.LocalDirectory, module.ManagedTaskName, module.ManagedEntryPoint);
        // Only a release-bundled, pinned GitHub descriptor may reach this method.
        var baseDir = DirectoryFor(module);
        var repo = Path.Combine(baseDir, "repo");
        if (!Directory.Exists(repo))
        {
            Directory.CreateDirectory(baseDir);
            var temp = Path.Combine(baseDir, ".yanzi-install-" + Guid.NewGuid().ToString("N"));
            try
            {
                await RunAsync("git.exe", ["-c", "core.hooksPath=NUL", "clone", "--no-checkout",
                    "--filter=blob:none", module.Repository, temp], token);
                await RunAsync("git.exe", ["-C", temp, "checkout", "--detach", module.Revision], token);
                var head = (await RunAsync("git.exe", ["-C", temp, "rev-parse", "HEAD"], token)).Trim();
                if (!head.Equals(module.Revision, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("service_module_revision_mismatch");
                Directory.Move(temp, repo);
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
        var repoHead = (await RunAsync("git.exe", ["-C", repo, "rev-parse", "HEAD"], token)).Trim();
        if (!repoHead.Equals(module.Revision, StringComparison.OrdinalIgnoreCase))
            throw new IOException("service_module_revision_mismatch");
        var changes = (await RunAsync("git.exe", ["-C", repo, "status", "--porcelain", "--untracked-files=no"], token)).Trim();
        if (changes.Length != 0) throw new IOException("service_module_unverified_modifications");
        var script = Path.Combine(repo, module.InstallScript.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(script)) throw new IOException("service_module_installer_missing");

        // Configuration/peer pairing is owned by the service installer, not the host.
        // Existing configurations and active jobs are never overwritten.
        await RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy",
            "Bypass", "-File", script, "-Start"], token);
        if (!Installed(module)) throw new IOException("service_module_configuration_required");
        YanziManagedServiceInstallations.Complete(module.Id);
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> args,
        CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardError = true,
                RedirectStandardOutput = true, CreateNoWindow = true
            }
        };
        foreach (var argument in args) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!process.Start()) throw new IOException("service_module_process_start_failed");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
                throw new IOException("service_module_install_failed: " +
                    error.Trim()[..Math.Min(error.Trim().Length, 300)]);
            return output;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
