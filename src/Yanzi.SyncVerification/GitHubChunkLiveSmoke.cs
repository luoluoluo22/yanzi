using System;
using System.IO;
using System.Security.Cryptography;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class GitHubChunkLiveSmoke
{
    internal static async Task RunAsync()
    {
        var settings = AppSettingsStore.Load();
        var secrets = PersonalSyncSecretStore.Load();
        var env = AppEnvironmentVariableStore.GetValue("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(env)) secrets.GitHubToken = env;
        var backend = new GitHubPersonalSyncBackend(settings.PersonalSync.GitHub, secrets);
        await backend.VerifyPrivateRepositoryAsync();
        var basePath = $"redundant-backup/v1/synthetic-verify-{Guid.NewGuid():N}";
        var payload = RandomNumberGenerator.GetBytes(GitHubBackupChunkTransport.ChunkSize + 91);
        string? manifestPath = null;
        try
        {
            manifestPath = await GitHubBackupChunkTransport.UploadVerifiedChunksAsync(
                backend, basePath, payload, CancellationToken.None);
            var downloaded = await GitHubBackupChunkTransport.DownloadVerifiedChunksAsync(
                backend, manifestPath, CancellationToken.None);
            if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(downloaded), SHA256.HashData(payload)))
                throw new CryptographicException("Live GitHub chunk round-trip failed.");
            Console.WriteLine($"GITHUB_CHUNK_LIVE_OK size={payload.Length} parts=2 repoPrivate=True");
        }
        finally
        {
            // Only dispose of the uniquely prefixed synthetic fixture, never normal backups.
            for (var i = 0; i < 2; i++)
            {
                var path = $"{basePath}/part-{i:D4}.bin";
                if (await backend.TryReadBytesAsync(path, CancellationToken.None) != null)
                    await backend.DeleteFileAsync(path, CancellationToken.None);
            }
            var marker = basePath + "/manifest.json";
            if (await backend.TryReadBytesAsync(marker, CancellationToken.None) != null)
                await backend.DeleteFileAsync(marker, CancellationToken.None);
            CryptographicOperations.ZeroMemory(payload);
            Console.WriteLine("GITHUB_CHUNK_TEST_CLEANED=True");
        }
    }
}
