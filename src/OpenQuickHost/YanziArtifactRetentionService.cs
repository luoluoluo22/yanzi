using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OpenQuickHost;

/// <summary>
/// Bounded retention for Yanzi-owned installation backups. Never touches user data,
/// Velopack's live packages directory, or extension configuration/storage.
/// </summary>
public static class YanziArtifactRetentionService
{
    public const int KeepRecentBackups = 2;
    public const int MinimumBackupAgeDays = 3;
    public const int MaximumBackupAgeDays = 14;
    public const long MaximumBackupBytes = 1024L * 1024 * 1024;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FirstSweepDelay = TimeSpan.FromMinutes(2);
    private static int _started;

    /// <summary>Runs in background only for the official single-process host.</summary>
    public static void Start()
    {
        if (HostRuntimeProfile.IsDevelopment || !HostRuntimeProfile.OwnsBackgroundServices ||
            !IsInstalledSingleProcessHost()) return;
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(FirstSweepDelay).ConfigureAwait(false);
            while (true)
            {
                try
                {
                    var root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yanzi");
                    var outcome = Sweep(root, DateTime.UtcNow, delete: true);
                    var legacy = SweepLegacyRuntime(root, DateTime.UtcNow, delete: true);
                    if (legacy.DeletedCount > 0 || legacy.Errors > 0)
                        HostAssets.AppendLog($"Legacy Runtime retention: deleted={legacy.DeletedCount}, " +
                            $"freedBytes={legacy.FreedBytes}, errors={legacy.Errors}");
                    if (outcome.DeletedCount > 0 || outcome.Errors > 0)
                        HostAssets.AppendLog($"Yanzi artifact retention: deleted={outcome.DeletedCount}, " +
                            $"freedBytes={outcome.FreedBytes}, errors={outcome.Errors}, " +
                            $"kept={outcome.KeptCount}");
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog("Yanzi artifact retention skipped: " + ex.GetType().Name);
                }
                await Task.Delay(SweepInterval).ConfigureAwait(false);
            }
        });
    }

    private static bool IsInstalledSingleProcessHost()
    {
        try
        {
            var current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current)) return false;
            var official = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Yanzi", "current", "Yanzi.exe");
            return Path.GetFullPath(current).Equals(
                Path.GetFullPath(official), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Public for isolated verification; root must be a Yanzi installation directory,
    // and all deletions are restricted to the explicitly named snapshot prefixes.
    public static RetentionOutcome Sweep(string installRoot, DateTime utcNow, bool delete, long maxBytes = MaximumBackupBytes)
    {
        if (!Path.GetFileName(Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar))
                .Equals("Yanzi", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a Yanzi installation root is allowed.", nameof(installRoot));
        if (!File.Exists(Path.Combine(installRoot, "current", "Yanzi.exe")))
            return new RetentionOutcome(0, 0, 0, 0);

        var now = utcNow.ToUniversalTime();
        var backups = new List<Entry>();
        foreach (var path in Directory.EnumerateDirectories(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var dir = new DirectoryInfo(path);
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0 || !IsManagedBackup(dir.Name))
                continue;
            var newestTimestamp = dir.CreationTimeUtc > dir.LastWriteTimeUtc
                ? dir.CreationTimeUtc : dir.LastWriteTimeUtc;
            backups.Add(new Entry(dir.FullName, newestTimestamp, GetDirectoryBytes(dir.FullName)));
        }
        backups.Sort(static (a, b) => b.ModifiedAtUtc.CompareTo(a.ModifiedAtUtc));
        var total = backups.Sum(static b => b.Bytes);
        var kept = 0;
        var deleted = 0;
        var errors = 0;
        long freed = 0;
        for (var i = 0; i < backups.Count; i++)
        {
            var entry = backups[i];
            var age = now - entry.ModifiedAtUtc;
            var tooYoung = age < TimeSpan.FromDays(MinimumBackupAgeDays);
            var shouldDelete = i > 0 && !tooYoung &&
                (i >= KeepRecentBackups || age >= TimeSpan.FromDays(MaximumBackupAgeDays)
                 || total > maxBytes);
            if (!shouldDelete)
            {
                kept++;
                continue;
            }
            if (!delete)
            {
                deleted++;
                freed += entry.Bytes;
                total -= entry.Bytes;
                continue;
            }
            try
            {
                var live = new DirectoryInfo(entry.Path);
                if (!live.Exists || (live.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !IsManagedBackup(live.Name) || IsOwnedByLiveProcess(live.FullName) ||
                    !string.Equals(Path.GetDirectoryName(live.FullName),
                        Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    kept++;
                    continue;
                }
                if (live.CreationTimeUtc > entry.ModifiedAtUtc ||
                    live.LastWriteTimeUtc > entry.ModifiedAtUtc)
                {
                    kept++;
                    continue;
                }
                Directory.Delete(live.FullName, recursive: true);
                deleted++;
                freed += entry.Bytes;
                total -= entry.Bytes;
            }
            catch (Exception ex)
            {
                errors++;
                kept++;
                HostAssets.AppendLog("Yanzi artifact cleanup skipped: " +
                    Path.GetFileName(entry.Path) + " (" + ex.GetType().Name + ")");
            }
        }
        return new RetentionOutcome(deleted, freed, kept, errors);
    }


    /// <summary>
    /// After two weeks on the single-process architecture, prune legacy executable
    /// snapshots but keep the newest snapshot in each legacy group for emergency recovery.
    /// All pointer JSON files and arbitrary files at the legacy root remain untouched.
    /// </summary>
    public static RetentionOutcome SweepLegacyRuntime(string installRoot, DateTime utcNow, bool delete)
    {
        if (!Path.GetFileName(Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar))
                .Equals("Yanzi", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a Yanzi installation root is allowed.", nameof(installRoot));
        if (!File.Exists(Path.Combine(installRoot, "current", "Yanzi.exe")))
            return new RetentionOutcome(0, 0, 0, 0);

        var localAppData = Directory.GetParent(Path.GetFullPath(installRoot));
        if (localAppData == null) return new RetentionOutcome(0, 0, 0, 0);
        var legacyRoot = Path.Combine(localAppData.FullName, "YanziRuntime");
        if (!Directory.Exists(legacyRoot) ||
            (new DirectoryInfo(legacyRoot).Attributes & FileAttributes.ReparsePoint) != 0)
            return new RetentionOutcome(0, 0, 0, 0);

        // First observation starts the grace period. Never trust legacy directory dates,
        // which can be years old even on a newly migrated installation.
        var graceMarker = Path.Combine(installRoot, ".legacy-runtime-retention-since");
        if (!File.Exists(graceMarker))
        {
            if (delete) File.WriteAllText(graceMarker, utcNow.ToUniversalTime().ToString("O"));
            return new RetentionOutcome(0, 0, 0, 0);
        }
        if (!DateTime.TryParse(File.ReadAllText(graceMarker), out var observed))
            return new RetentionOutcome(0, 0, 0, 0);
        if (utcNow.ToUniversalTime() - observed.ToUniversalTime() < TimeSpan.FromDays(14))
            return new RetentionOutcome(0, 0, 0, 0);

        // A still-registered legacy startup or running Runtime makes retirement unsafe.
        if (Process.GetProcessesByName("Yanzi.Runtime").Length > 0 ||
            IsOwnedByLiveProcess(legacyRoot) || HasLegacyRuntimeStartupReference(legacyRoot))
            return new RetentionOutcome(0, 0, 0, 0);

        // Even though legacy Runtime is no longer started, preserve the explicit
        // last-known recovery targets from its active pointer.
        var protectedExecutables = new List<string>();
        var activePointer = Path.Combine(legacyRoot, "runtime.json");
        if (File.Exists(activePointer))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(activePointer));
                foreach (var key in new[] { "executable", "stableShell" })
                    if (document.RootElement.TryGetProperty(key, out var prop) &&
                        prop.ValueKind == System.Text.Json.JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(prop.GetString()))
                        protectedExecutables.Add(Path.GetFullPath(prop.GetString()!));
            }
            catch { return new RetentionOutcome(0, 0, 0, 0); } // Keep everything if pointer cannot be interpreted.
        }

        var removed = 0;
        var kept = 0;
        var errors = 0;
        long freed = 0;
        foreach (var groupName in new[] { "versions", "shells", "bundled", "staged" })
        {
            var groupPath = Path.Combine(legacyRoot, groupName);
            if (!Directory.Exists(groupPath) ||
                (new DirectoryInfo(groupPath).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var directories = Directory.GetDirectories(groupPath, "*", SearchOption.TopDirectoryOnly)
                .Select(static path => new DirectoryInfo(path))
                .Where(static d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(static d => d.LastWriteTimeUtc)
                .ToArray();
            // Always retain the newest snapshot per group, even when very old.
            for (var i = 0; i < directories.Length; i++)
            {
                var entry = directories[i];
                if (i == 0 || protectedExecutables.Any(path => path.StartsWith(
                        entry.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    kept++;
                    continue;
                }
                var bytes = GetDirectoryBytes(entry.FullName);
                if (!delete)
                {
                    removed++;
                    freed += bytes;
                    continue;
                }
                try
                {
                    if (IsOwnedByLiveProcess(entry.FullName) ||
                        (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        kept++;
                        continue;
                    }
                    Directory.Delete(entry.FullName, recursive: true);
                    freed += bytes;
                    removed++;
                }
                catch (Exception ex)
                {
                    errors++;
                    kept++;
                    HostAssets.AppendLog("Legacy Runtime snapshot cleanup skipped: " + ex.GetType().Name);
                }
            }
        }
        return new RetentionOutcome(removed, freed, kept, errors);
    }

    private static bool HasLegacyRuntimeStartupReference(string legacyRoot)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName) as string;
                    if (value != null && value.Contains(legacyRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { return true; } // Fail closed when startup references cannot be verified.
        return false;
    }

    private static bool IsManagedBackup(string name) =>
        name.StartsWith("rollback-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("backup-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("failed-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("staging-", StringComparison.OrdinalIgnoreCase);

    private static long GetDirectoryBytes(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
                try { total = checked(total + new FileInfo(file).Length); }
                catch (IOException) { }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return total;
    }

    private static bool IsOwnedByLiveProcess(string directory)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable != null && executable.StartsWith(
                            directory.TrimEnd(Path.DirectorySeparatorChar) +
                            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (NotSupportedException) { }
            }
        }
        return false;
    }

    private sealed record Entry(string Path, DateTime ModifiedAtUtc, long Bytes);
}

public readonly record struct RetentionOutcome(int DeletedCount, long FreedBytes, int KeptCount, int Errors);
