using System.Security.Cryptography;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class GitHubSecondaryBackupVerification
{
    public static async Task RunAsync(bool live)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var incorrect = RandomNumberGenerator.GetBytes(32);
        var root = Path.Combine(Path.GetTempPath(), "yanzi-restore-audit-" + Guid.NewGuid().ToString("N"));
        using var isolatedData = HostAssets.UseIsolatedDataRootForVerification(Path.Combine(root, "source-device-profile"));
        if (GitHubSecondaryBackupService.IsRecoveryKeyConfirmed()) throw new Exception("A new device must require an exported key.");
        Directory.CreateDirectory(root);
        var extension = Path.Combine(HostAssets.ResolveDataFilePath("Extensions"), "synthetic-backup-test");
        Directory.CreateDirectory(extension);
        File.WriteAllText(Path.Combine(extension, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(extension, "main.cs"), "// synthetic test");
        var unconfirmed = new AppSettings();
        unconfirmed.PersonalSync.GitHubBackupEnabled = true;
        var guard = await GitHubSecondaryBackupService.BackupAsync(unconfirmed, force: true);
        if (guard.Created || guard.Status != "offline-recovery-key-not-confirmed")
            throw new Exception("Backup must not run before the device key is exported.");
        try
        {
            var archive = GitHubSecondaryBackupService.BuildArchive();
            try
            {
                var ciphertext = GitHubSecondaryBackupService.Encrypt(archive, key);
                var opened = GitHubSecondaryBackupService.Decrypt(ciphertext, key);
                if (!opened.AsSpan().SequenceEqual(archive))
                    throw new Exception("AES snapshot roundtrip differs.");
                CryptographicOperations.ZeroMemory(opened);
                try
                {
                    GitHubSecondaryBackupService.Decrypt(ciphertext, incorrect);
                    throw new Exception("Wrong encryption key was accepted.");
                }
                catch (CryptographicException) { }
                var count = GitHubSecondaryBackupService.RestoreToIsolatedDirectory(ciphertext, key,
                    Path.Combine(root, "sample"));
                if (count < 2)
                    throw new Exception("Isolated restore missing data.");
                Console.WriteLine($"SECONDARY_BACKUP_CRYPTO_OK bytes={archive.Length} files={count}");
            }
            finally { CryptographicOperations.ZeroMemory(archive); }

            // A new Windows profile must be able to import an offline recovery key.
            var rescueFile = Path.Combine(root, "offline-key.txt");
            GitHubSecondaryBackupService.ExportRecoveryKey(rescueFile);
            if (GitHubSecondaryBackupService.IsRecoveryKeyConfirmed()) throw new Exception("Export alone must not confirm offline storage.");
            GitHubSecondaryBackupService.ConfirmExportedRecoveryKey(rescueFile);
            if (!GitHubSecondaryBackupService.IsRecoveryKeyConfirmed()) throw new Exception("Explicit confirmation must bind this device key.");
            var marker = HostAssets.ResolveDataFilePath("github-backup-key-confirmation.sha256");
            File.WriteAllBytes(marker, new byte[32]);
            if (GitHubSecondaryBackupService.IsRecoveryKeyConfirmed()) throw new Exception("A marker for a different key must not be accepted.");
            var secondFile = Path.Combine(root, "offline-key-second.txt");
            GitHubSecondaryBackupService.ExportRecoveryKey(secondFile);
            GitHubSecondaryBackupService.ConfirmExportedRecoveryKey(secondFile);
            var sourceKey = GitHubSecondaryBackupService.LoadOrCreateKey();
            using (HostAssets.UseIsolatedDataRootForVerification(Path.Combine(root, "new-device-profile")))
            {
                GitHubSecondaryBackupService.ImportRecoveryKey(rescueFile);
                if (!GitHubSecondaryBackupService.IsRecoveryKeyConfirmed()) throw new Exception("Imported offline key must be confirmed.");
                var imported = GitHubSecondaryBackupService.LoadOrCreateKey();
                if (!sourceKey.AsSpan().SequenceEqual(imported))
                    throw new Exception("Off-machine recovery key cannot decrypt snapshots.");
                CryptographicOperations.ZeroMemory(imported);
            }
            CryptographicOperations.ZeroMemory(sourceKey);
            Console.WriteLine("SECONDARY_BACKUP_RECOVERY_KEY_OK independentDevice=True");
            await GitHubBackupChunkVerification.RunAsync();
            if (!live) return;
            var settings = AppSettingsStore.Load();
            settings.PersonalSync.GitHub.Username = "luoluoluo22";
            var result = await GitHubSecondaryBackupService.BackupAsync(settings, force: true);
            if (!result.Created || result.Path == null || result.Status != "verified")
                throw new Exception("GitHub upload verification incomplete.");
            var files = await GitHubSecondaryBackupService.RestoreSnapshotToIsolatedDirectoryAsync(
                settings, result.Path, Path.Combine(root, "github"));
            if (files < 2) throw new Exception("GitHub recovery verification failed.");
            Console.WriteLine($"SECONDARY_BACKUP_GITHUB_OK bytes={result.ArchiveBytes} files={files} path={result.Path}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(incorrect);
            Directory.Delete(root, recursive: true);
        }
    }
}
