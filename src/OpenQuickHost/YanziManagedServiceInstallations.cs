using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// Ownership ledger for service modules provisioned by Yanzi itself.
/// Discovering an already-installed service NEVER assigns its ownership to Yanzi.
/// The ledger lives in retained user data, outside the Velopack application directory.
/// </summary>
internal static class YanziManagedServiceInstallations
{
    private const string Owner = "yanzi-on-demand-service-v1";
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static string LedgerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "ServiceModules", "owned-installations.json");

    private sealed class Record
    {
        public string Owner { get; set; } = "";
        public string Id { get; set; } = "";
        public string Repository { get; set; } = "";
        public string Revision { get; set; } = "";
        public string LocalDirectory { get; set; } = "";
        public string ManagedTaskName { get; set; } = "";
        public string ManagedEntryPoint { get; set; } = "";
        public string State { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
    }

    private static List<Record> Read()
    {
        if (!File.Exists(LedgerPath)) return [];
        return JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(LedgerPath), Json)
            ?? throw new InvalidDataException("Owned service installation ledger is invalid.");
    }

    private static void Save(List<Record> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
        var temp = LedgerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(records, Json));
            File.Move(temp, LedgerPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void Begin(string id, string repository, string revision, string localDirectory,
        string managedTaskName, string managedEntryPoint)
    {
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{2,63}$") ||
            !Regex.IsMatch(localDirectory, "^[A-Za-z][A-Za-z0-9-]{2,80}$") ||
            !Regex.IsMatch(managedTaskName, "^[A-Za-z][A-Za-z0-9-]{2,100}$") ||
            !Regex.IsMatch(managedEntryPoint, @"^app/[A-Za-z0-9/.-]+\.(?:mjs|js|exe)$") ||
            managedEntryPoint.Split('/').Any(s => s == "." || s == "..") ||
            !Regex.IsMatch(revision, "^[a-f0-9]{40}$") ||
            !repository.StartsWith("https://github.com/luoluoluo22/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Untrusted service installation metadata.");

        lock (Gate)
        {
            var entries = Read();
            var existing = entries.FirstOrDefault(x => x.Id == id && x.Owner == Owner);
            if (existing != null)
            {
                if (existing.Repository != repository || existing.Revision != revision ||
                    existing.LocalDirectory != localDirectory || existing.ManagedTaskName != managedTaskName ||
                    existing.ManagedEntryPoint != managedEntryPoint)
                    throw new InvalidDataException("Existing managed-service installation ownership differs.");
                return; // Retry a previously interrupted installation, not a new claim.
            }
            // Never claim a user-preinstalled service, or a partial installation without our ledger.
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), localDirectory);
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new IOException("service_module_preexisting_unowned_installation");

            entries.Add(new Record {
                Owner = Owner, Id = id, Repository = repository, Revision = revision,
                LocalDirectory = localDirectory, ManagedTaskName = managedTaskName,
                ManagedEntryPoint = managedEntryPoint, State = "installing",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            Save(entries);
        }
    }

    internal static void Complete(string id)
    {
        lock (Gate)
        {
            var entries = Read();
            var record = entries.Single(x => x.Owner == Owner && x.Id == id);
            record.State = "installed";
            Save(entries);
        }
    }

    /// <summary>
    /// Starts a detached helper from retained user storage so cleanup can finish
    /// after Velopack removes its own application directory. No upgrade calls this.
    /// Only the exact, release-bundled manifests + our ledger authorize removal.
    /// </summary>
    internal static void StartUninstallCleanup()
    {
        lock (Gate)
        {
            if (!File.Exists(LedgerPath) || Read().Count == 0) return;
        }

        var source = Path.Combine(AppContext.BaseDirectory, "ServiceModules");
        var script = Path.Combine(source, "uninstall-owned-services.ps1");
        if (!File.Exists(script))
            throw new FileNotFoundException("Owned service uninstall helper is missing.", script);

        var staging = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "ServiceUninstallStaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        foreach (var file in Directory.EnumerateFiles(source, "*.json"))
            File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
        File.Copy(script, Path.Combine(staging, Path.GetFileName(script)));

        var info = new ProcessStartInfo("powershell.exe") {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = false, RedirectStandardError = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(staging, "uninstall-owned-services.ps1"),
            "-ManifestDir", staging, "-LedgerPath", LedgerPath
        }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)
            ?? throw new IOException("Failed to start owned-service uninstall worker.");
        // Not an upgrade hook. Worker remains self-contained if cleanup exceeds this bound.
        if (process.WaitForExit(1500) && process.ExitCode != 0)
            HostAssets.AppendLog("[OwnedServiceUninstall] Cleanup reported exit code " + process.ExitCode);
        else if (!process.HasExited)
            HostAssets.AppendLog("[OwnedServiceUninstall] Cleanup continues independently, PID=" + process.Id);
    }
}
