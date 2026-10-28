using OpenQuickHost.Sync;

internal static class ExtensionWatcherRaceVerification
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "yanzi-watcher-race-" + Guid.NewGuid().ToString("N"));
        var ext = Path.Combine(root, "legitimate-extension");
        Directory.CreateDirectory(ext);
        try
        {
            var manifest = Path.Combine(ext, "manifest.json");
            File.WriteAllText(manifest, "{}");
            Check(ExtensionSyncIgnorePolicy.ShouldInclude(ext, manifest), "Normal manifest should be included");
            var stage = Path.Combine(root, ".yanzi-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var tempManifest = Path.Combine(stage, "manifest.json");
            File.WriteAllText(tempManifest, "{}");
            Check(!ExtensionSyncIgnorePolicy.ShouldInclude(root, tempManifest),
                "Installer staging files must not be eligible for cloud sync");
            Check(!ExtensionSyncIgnorePolicy.ShouldInclude(root, stage, true),
                "Installer staging folder must not be eligible for sync");
            Directory.Delete(stage, recursive: true);
            Check(!ExtensionSyncIgnorePolicy.ShouldInclude(stage, tempManifest),
                "Deleted temporary installation must not throw or be packaged");
            Check(!ExtensionSyncIgnorePolicy.ShouldInclude(ext, Path.Combine(ext, "missing.json")),
                "Removed watcher entries must be skipped");

            var nested = Path.Combine(ext, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(ext, ".yanziignore"), "*.secret\n");
            File.WriteAllText(Path.Combine(ext, "example.secret"), "private");
            File.WriteAllText(Path.Combine(nested, "content.txt"), "ok");
            Check(!ExtensionSyncIgnorePolicy.ShouldInclude(ext, Path.Combine(ext, "example.secret")),
                "Sensitive ignored files must remain excluded");
            Check(ExtensionSyncIgnorePolicy.ShouldInclude(ext, Path.Combine(nested, "content.txt")),
                "Ordinary nested files must remain included");

            // Concurrent temporary file and directory removal used to race with
            // FileSystemWatcher callback's ShouldInclude and the package scanner.
            var race = Path.Combine(ext, "race");
            Directory.CreateDirectory(race);
            Exception? failure = null;
            var writer = Task.Run(() =>
            {
                for (var i = 0; i < 250; i++)
                {
                    try
                    {
                        Directory.CreateDirectory(race);
                        var file = Path.Combine(race, "manifest.json");
                        File.WriteAllText(file, "{}");
                        Directory.Delete(race, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            });
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 300; i++)
                {
                    try
                    {
                        ExtensionSyncIgnorePolicy.ShouldInclude(ext, Path.Combine(race, "manifest.json"));
                        ExtensionSyncIgnorePolicy.EnumerateIncludedFiles(ext).ToArray();
                    }
                    catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); return; }
                }
            })).ToArray();
            Task.WaitAll(readers.Append(writer).ToArray());
            if (failure != null) throw new Exception("File watcher and directory removal race still fails", failure);
            Console.WriteLine("Extension watcher race passed: staging excluded, deleted path safe, ignored secrets, concurrent directory removals.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool predicate, string message)
    {
        if (!predicate) throw new Exception("Watcher race regression: " + message);
    }
}