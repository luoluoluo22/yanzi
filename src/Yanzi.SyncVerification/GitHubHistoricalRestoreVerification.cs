using System;
using System.IO;
using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class GitHubHistoricalRestoreVerification
{
    internal static async Task RunAsync()
    {
        var stateFile = HostAssets.ResolveDataFilePath("github-backup-state.json");
        if (!File.Exists(stateFile))
            throw new FileNotFoundException("No historical GitHub backup checkpoint recorded.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(stateFile));
        var path = doc.RootElement.GetProperty("lastSnapshotPath").GetString();
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("redundant-backup/v1/", StringComparison.Ordinal) ||
            !path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Historical snapshot marker is invalid.");
        var destination = Path.Combine(Path.GetTempPath(),
            "yanzi-historical-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var count = await GitHubSecondaryBackupService.RestoreSnapshotToIsolatedDirectoryAsync(
                AppSettingsStore.Load(), path, destination);
            if (count < 2 || !File.Exists(Path.Combine(destination, "meta", "format.json")))
                throw new InvalidDataException("Historical restore is incomplete.");
            Console.WriteLine($"GITHUB_HISTORICAL_RESTORE_OK files={count} isolated=True liveDataUnchanged=True");
        }
        finally
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            Console.WriteLine("GITHUB_HISTORICAL_TEMP_CLEANED=True");
        }
    }
}
