using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;

internal static class ReleaseReadinessVerification
{
    internal static void VerifyUninstallDataRetention()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var appSource = File.ReadAllText(Path.Combine(sourceRoot, "src/OpenQuickHost/App.xaml.cs"));
        var start = appSource.IndexOf("OnBeforeUninstallFastCallback", StringComparison.Ordinal);
        var end = start < 0 ? -1 : appSource.IndexOf(".Run()", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Uninstall hook located");
        var uninstall = appSource[start..end];
        Check(!uninstall.Contains("rd /s", StringComparison.OrdinalIgnoreCase)
            && !uninstall.Contains("Directory.Delete", StringComparison.Ordinal)
            && !uninstall.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase), "Uninstall must retain user data");
    }

    internal static async Task RunAsync(string artifactRoot)
    {
        var root = Path.GetFullPath(artifactRoot);
        var feed = Path.Combine(root, "installer");
        var full = Directory.GetFiles(feed, "*-full.nupkg")
            .Single(p => Path.GetFileName(p).Contains("rc."));
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(feed, "releases.win.json")));
        var target = manifest.RootElement.GetProperty("Assets").EnumerateArray()
            .Single(a => a.GetProperty("FileName").GetString() == Path.GetFileName(full));
        var version = target.GetProperty("Version").GetString()!;
        var baseline = Path.Combine(feed, "Yanzi-1.0.6-full.nupkg");
        var baselineHash = Hash(baseline);
        var portable = Path.Combine(root, "portable");
        ZipFile.ExtractToDirectory(Directory.GetFiles(feed, "*-Portable-*.zip").Single(), portable, true);
        Check(File.Exists(Path.Combine(portable, "Update.exe")) && File.Exists(Path.Combine(portable, ".portable"))
            && File.Exists(Path.Combine(portable, "current/Yanzi.Core.dll")), "Portable updater, marker and core payload present");
        VerifyUninstallDataRetention();

        foreach (var corruptDelta in new[] { false, true })
        {
            var scenario = Path.Combine(root, (corruptDelta ? "update-corrupt-delta-" : "update-delta-") + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scenario);
            var source = Path.Combine(scenario, "feed");
            var packages = Path.Combine(scenario, "packages");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(packages);
            foreach (var path in Directory.GetFiles(feed, "*.nupkg"))
                File.Copy(path, Path.Combine(source, Path.GetFileName(path)), true);
            File.Copy(Path.Combine(feed, "releases.win.json"), Path.Combine(source, "releases.win.json"), true);
            File.Copy(baseline, Path.Combine(packages, Path.GetFileName(baseline)), true);
            if (corruptDelta)
                File.WriteAllBytes(Directory.GetFiles(source, "*-delta.nupkg").Single(), [1, 2, 3]);
            var log = new VerificationLogger();
            var locator = new TestVelopackLocator("Yanzi", "1.0.6", packages,
                Path.Combine(scenario, "current"), scenario, Path.Combine(portable, "Update.exe"), "win", log);
            var manager = new UpdateManager(source, new UpdateOptions(), locator);
            var update = await manager.CheckForUpdatesAsync();
            Check(update != null && update.TargetFullRelease.Version.ToString() == version, "Candidate update discovered");
            Check(update!.DeltasToTarget.Length > 0, "Delta update selected");
            await manager.DownloadUpdatesAsync(update);
            Check(log.Messages.Any(m => m.Contains(corruptDelta ? "falling back to full update" : "Delta update download complete", StringComparison.Ordinal)),
                corruptDelta ? "Updater reported corrupt-delta fallback" : "Updater applied actual delta without fallback");
            var downloaded = Path.Combine(packages, Path.GetFileName(full));
            Check(File.Exists(downloaded), "Update package prepared");
            Check(EntriesEqual(full, downloaded), corruptDelta ? "Corrupt delta falls back to full package" : "Delta reconstructs identical payload");
            Check(Hash(baseline) == baselineHash, "Independent rollback package remains intact");
        }

        var data = Path.Combine(root, "desktop-data-fixture");
        Directory.CreateDirectory(data);
        using (HostAssets.UseExistingDataRootForVerification(data))
        {
            // Dummy credentials only. This scope never opens the real user's profile.
            File.WriteAllText(AppSettingsStore.SettingsPath, "{\"themeMode\":\"Dark\",\"hasShownInitialWelcomePanel\":true}");
            SyncSessionStore.Save(new SyncSession { AccessToken = "offline-release-fixture", UserId = "release-fixture", Username = "升级中文用户", ExpiresAt = 4102444800 });
            var sentinel = Path.Combine(data, "user-file.txt");
            File.WriteAllText(sentinel, "用户数据\n升级与回滚");
            var sentinelHash = Hash(sentinel);
            var settings = AppSettingsStore.Load();
            Check(settings.ThemeMode == "Dark", "Legacy configuration loads");
            AppSettingsStore.Save(settings);
            Check(AppSettingsStore.Load().ThemeMode == "Dark", "Configuration survives save and reload");
            Check(SyncSessionStore.Load()?.AccessToken == "offline-release-fixture", "Session retained during migration");
            var shippedContext = new AssemblyLoadContext("release-candidate", true);
            var shippedPath = Path.Combine(portable, "current/Yanzi.dll");
            var shippedResolver = new AssemblyDependencyResolver(shippedPath);
            shippedContext.Resolving += (_, name) => shippedResolver.ResolveAssemblyToPath(name) is { } path ? shippedContext.LoadFromAssemblyPath(path) : null;
            try
            {
                var shippedAssembly = shippedContext.LoadFromAssemblyPath(shippedPath);
                var assets = shippedAssembly.GetType("OpenQuickHost.HostAssets", true)!;
                assets.GetField("_isolatedVerificationDataRootPath", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, data);
                var store = shippedAssembly.GetType("OpenQuickHost.AppSettingsStore", true)!;
                var shippedSettings = store.GetMethod("Load")!.Invoke(null, null)!;
                Check((string?)shippedSettings.GetType().GetProperty("ThemeMode")!.GetValue(shippedSettings) == "Dark", "Actual Release binary reads legacy settings");
                store.GetMethod("Save")!.Invoke(null, [shippedSettings]);
                var session = shippedAssembly.GetType("OpenQuickHost.Sync.SyncSessionStore", true)!.GetMethod("Load")!.Invoke(null, null)!;
                Check((string?)session.GetType().GetProperty("AccessToken")!.GetValue(session) == "offline-release-fixture", "Actual Release binary retains session");
            }
            finally { shippedContext.Unload(); }
            // Deserialize with the actual old shipped model, rather than a duplicated schema.
            var oldDir = Path.Combine(root, "baseline-model");
            Directory.CreateDirectory(oldDir);
            using (var archive = ZipFile.OpenRead(baseline))
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.Name.EndsWith(".dll")))
                    entry.ExtractToFile(Path.Combine(oldDir, entry.Name), true);
            var context = new AssemblyLoadContext("release-baseline", true);
            var resolver = new AssemblyDependencyResolver(Path.Combine(oldDir, "Yanzi.dll"));
            context.Resolving += (_, name) => resolver.ResolveAssemblyToPath(name) is { } path ? context.LoadFromAssemblyPath(path) : null;
            try
            {
                var assembly = context.LoadFromAssemblyPath(Path.Combine(oldDir, "Yanzi.dll"));
                var oldType = assembly.GetType(typeof(AppSettings).FullName!, true)!;
                var oldSettings = JsonSerializer.Deserialize(File.ReadAllText(AppSettingsStore.SettingsPath), oldType,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                Check((string?)oldType.GetProperty("ThemeMode")!.GetValue(oldSettings) == "Dark", "Previous release can read migrated settings");
                File.WriteAllText(AppSettingsStore.SettingsPath, JsonSerializer.Serialize(oldSettings, oldType));
                Check(AppSettingsStore.Load().ThemeMode == "Dark", "Re-upgrade reads baseline saved settings");
            }
            finally { context.Unload(); }
            Check(Hash(sentinel) == sentinelHash && SyncSessionStore.Load()?.UserId == "release-fixture", "Rollback model round trip preserves session and files");
        }
        File.WriteAllText(Path.Combine(root, "desktop-release-verification.json"), JsonSerializer.Serialize(new
        {
            version, passed = true, deltaPayloadEquality = true, corruptDeltaFullFallback = true,
            baselineModelRoundTrip = true, isolatedUserData = true,
            installerExecuted = false, liveAuthenticationTested = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Desktop release readiness verification passed.");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool EntriesEqual(string left, string right)
    {
        using var a = ZipFile.OpenRead(left);
        using var b = ZipFile.OpenRead(right);
        var x = a.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, e => EntryHash(e));
        var y = b.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, e => EntryHash(e));
        return x.Count == y.Count && x.All(e => y.TryGetValue(e.Key, out var hash) && hash == e.Value);
    }
    private static string EntryHash(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
    private sealed class VerificationLogger : IVelopackLogger
    {
        internal List<string> Messages { get; } = [];
        public void Log(VelopackLogLevel level, string? message, Exception? exception)
        {
            if (message != null) Messages.Add(message);
        }
    }
}
