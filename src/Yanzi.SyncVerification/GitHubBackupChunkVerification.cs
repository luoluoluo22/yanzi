using OpenQuickHost.Sync;
using System.Security.Cryptography;

internal static class GitHubBackupChunkVerification
{
    public static async Task RunAsync()
    {
        var payload = RandomNumberGenerator.GetBytes(410_000);
        var backend = new MemoryBackend();
        var path = await GitHubBackupChunkTransport.UploadVerifiedChunksAsync(
            backend, "redundant-backup/v1/mock/demo", payload, default);
        var restored = await GitHubBackupChunkTransport.DownloadVerifiedChunksAsync(backend, path, default);
        if (!payload.AsSpan().SequenceEqual(restored))
            throw new Exception("Chunk transport altered snapshot bytes.");
        backend.Bytes["redundant-backup/v1/mock/demo/part-0001.bin"][1] ^= 1;
        try
        {
            await GitHubBackupChunkTransport.DownloadVerifiedChunksAsync(backend, path, default);
            throw new Exception("Corrupt part was accepted.");
        }
        catch (CryptographicException) { }

        var failed = new MemoryBackend { FailOnPart = true };
        try
        {
            await GitHubBackupChunkTransport.UploadVerifiedChunksAsync(
                failed, "redundant-backup/v1/mock/failing", payload, default);
            throw new Exception("Injected upload failure was ignored.");
        }
        catch (IOException) { }
        if (failed.Bytes.Keys.Any(x => x.EndsWith("/manifest.json")))
            throw new Exception("Incomplete snapshot was incorrectly marked recoverable.");
        Console.WriteLine("SECONDARY_BACKUP_CHUNK_OK tamperRejected=True incompleteManifestAbsent=True");
    }

    private sealed class MemoryBackend : IPersonalSyncBackend
    {
        public readonly Dictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);
        public bool FailOnPart;
        public string DisplayRoot => "mock://in-memory";
        public Task ProbeAsync(CancellationToken token) => Task.CompletedTask;
        public Task<byte[]?> TryReadBytesAsync(string path, CancellationToken token) =>
            Task.FromResult(Bytes.TryGetValue(path, out var data) ? data.ToArray() : null);
        public Task WriteBytesAsync(string path, byte[] content, string contentType, CancellationToken token)
        {
            if (FailOnPart && path.EndsWith("part-0001.bin", StringComparison.Ordinal))
                throw new IOException("Simulated link loss.");
            Bytes[path] = content.ToArray();
            return Task.CompletedTask;
        }
        public Task DeleteFileAsync(string path, CancellationToken token)
        {
            Bytes.Remove(path);
            return Task.CompletedTask;
        }
    }
}
