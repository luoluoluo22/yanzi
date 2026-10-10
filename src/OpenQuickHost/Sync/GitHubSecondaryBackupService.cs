using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

/// <summary>
/// An independent, append-only GitHub disaster-recovery copy. Never participates in
/// the live multi-device merge, so a stale backup cannot overwrite live records.
/// </summary>
public static class GitHubSecondaryBackupService
{
    private const int MaxArchiveBytes = 30 * 1024 * 1024;
    private const int MaxSingleFileBytes = 16 * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset ZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] StorageScopes =
    [
        "yanzi-notes", "clipboard-history", "inspiration-board", "taskbar-calendar",
        "yanzi-album", "ext_e9e37068c8284b1e808f5454181fb418"
    ];

    public static string RecoveryKeyPath =>
        HostAssets.ResolveDataFilePath("github-backup-recovery-key.dat");
    private static string StatePath =>
        HostAssets.ResolveDataFilePath("github-backup-state.json");

    public static async Task<GitHubBackupResult> BackupAsync(
        AppSettings settings, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!settings.PersonalSync.GitHubBackupEnabled && !force)
            return new GitHubBackupResult(false, "disabled", 0, null);
        if (!settings.PersonalSync.GitHubBackupRecoveryKeyConfirmed)
            return new GitHubBackupResult(false, "offline-recovery-key-not-confirmed", 0, null);
        var secrets = PersonalSyncSecretStore.Load();
        // Prefer the existing synchronized GH_TOKEN instead of a stale duplicate.
        var environmentToken = AppEnvironmentVariableStore.GetValue("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
            secrets.GitHubToken = environmentToken;
        if (string.IsNullOrWhiteSpace(secrets.GitHubToken) ||
            string.IsNullOrWhiteSpace(settings.PersonalSync.GitHub.Repo))
            throw new InvalidOperationException("GitHub backup requires an authenticated repository.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = LoadState();
            if (!force && state?.LastCheckedUtc > DateTimeOffset.UtcNow.AddHours(-24))
                return new GitHubBackupResult(false, "not-due", 0, state.LastSnapshotPath);

            var plain = BuildArchive();
            try
            {
                if (plain.Length > MaxArchiveBytes)
                    throw new InvalidOperationException("Encrypted backup exceeded the 30 MiB archive limit; no partial backup was sent.");
                var digest = Convert.ToHexString(SHA256.HashData(plain));
                if (!force && state?.ArchiveSha256 == digest)
                {
                    SaveState(new GitHubBackupState(DateTimeOffset.UtcNow, digest, state.LastSnapshotPath));
                    return new GitHubBackupResult(false, "unchanged", plain.Length, state.LastSnapshotPath);
                }
                var key = LoadOrCreateKey();
                try
                {
                    var payload = Encrypt(plain, key);
                    var backend = new GitHubPersonalSyncBackend(settings.PersonalSync.GitHub, secrets);
                    await backend.VerifyPrivateRepositoryAsync(cancellationToken);
                    var device = new string(Environment.MachineName.ToLowerInvariant()
                        .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
                    if (device.Length == 0) device = "device";
                    var name = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
                    var backupRoot = $"redundant-backup/v1/{device}/{name}-{digest[..16]}-{Guid.NewGuid():N}";
                    var path = await GitHubBackupChunkTransport.UploadVerifiedChunksAsync(backend, backupRoot, payload, cancellationToken);
                    var downloaded = await GitHubBackupChunkTransport.DownloadVerifiedChunksAsync(backend, path, cancellationToken);
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), SHA256.HashData(downloaded)))
                        throw new CryptographicException("Remote encrypted snapshot does not match uploaded bytes.");
                    var verified = Decrypt(downloaded, key);
                    try
                    {
                        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(plain), SHA256.HashData(verified)))
                            throw new CryptographicException("Downloaded snapshot failed plaintext SHA256 verification.");
                    }
                    finally { CryptographicOperations.ZeroMemory(verified); }
                    SaveState(new GitHubBackupState(DateTimeOffset.UtcNow, digest, path));
                    return new GitHubBackupResult(true, "verified", plain.Length, path);
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally { Gate.Release(); }
    }

    public static async Task<GitHubBackupResult> RunIfDueAsync(AppSettings settings)
    {
        if (!settings.PersonalSync.GitHubBackupEnabled)
            return new GitHubBackupResult(false, "disabled", 0, null);
        try
        {
            var result = await BackupAsync(settings);
            if (result.Created)
                HostAssets.AppendLog($"GitHub backup verified: bytes={result.ArchiveBytes}, path={result.Path}");
            return result;
        }
        catch (Exception e)
        {
            // Backup failures never block or overwrite the primary Gitee/Cloudflare sync.
            HostAssets.AppendLog($"GitHub secondary backup failed: {e.GetType().Name}: {e.Message}");
            return new GitHubBackupResult(false, "failed: " + e.GetType().Name, 0, null);
        }
    }

    internal static byte[] BuildArchive()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "meta/format.json",
                Encoding.UTF8.GetBytes("{\"format\":\"yanzi-github-disaster-recovery\",\"schema\":1}"));
            AddFile(zip, "configuration/appsettings.local.json", AppSettingsStore.SettingsPath);

            AddUnprotectedSecret(zip, "credentials/environment-variables.json",
                HostAssets.ResolveDataFilePath("environment-variables.dat"));
            AddUnprotectedSecret(zip, "credentials/personalsync-secrets.json",
                PersonalSyncSecretStore.SecretPath);

            foreach (var id in StorageScopes)
            {
                var dir = ExtensionStorageService.GetExtensionStorageDirectoryPath(id);
                if (!Directory.Exists(dir)) continue;
                foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .OrderBy(x => x, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(dir, path).Replace('\\', '/');
                    if (!ExtensionStorageService.IsPortableDataForExtension(id, relative) &&
                        !(id == "inspiration-board" && relative == "workspace-config.json"))
                        continue;
                    AddFile(zip, $"storage/{id}/{relative}", path);
                }
            }
            var extensionsRoot = HostAssets.ResolveDataFilePath("Extensions");
            if (Directory.Exists(extensionsRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(extensionsRoot)
                    .OrderBy(x => x, StringComparer.Ordinal))
                {
                    var id = Path.GetFileName(dir);
                    if (id.StartsWith('.') || id.Contains('/') || id.Contains('\\')) continue;
                    foreach (var name in new[] { "manifest.json", "main.cs", "icon.png",
                                                 "icon.svg", "icon.webp" })
                        AddFile(zip, $"extensions/{id}/{name}", Path.Combine(dir, name));
                }
            }
        }
        var archive = stream.ToArray();
        if (archive.Length > MaxArchiveBytes)
            throw new InvalidOperationException("Snapshot exceeds maximum; backup aborted without omissions.");
        return archive;
    }

    private static void AddUnprotectedSecret(ZipArchive zip, string entry, string path)
    {
        if (!File.Exists(path)) return;
        var ciphertext = File.ReadAllBytes(path);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
            AddEntry(zip, entry, plain);
        }
        finally
        {
            if (plain != null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void AddFile(ZipArchive zip, string entry, string path)
    {
        if (!File.Exists(path)) return;
        var attr = File.GetAttributes(path);
        if ((attr & FileAttributes.ReparsePoint) != 0) return;
        if (new FileInfo(path).Length > MaxSingleFileBytes)
            throw new InvalidOperationException($"Backup source exceeds per-file limit: {entry}");
        AddEntry(zip, entry, File.ReadAllBytes(path));
    }

    private static void AddEntry(ZipArchive zip, string name, byte[] bytes)
    {
        if (bytes.Length > MaxSingleFileBytes) throw new InvalidOperationException("Backup entry too large.");
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = ZipTimestamp;
        using var output = entry.Open();
        output.Write(bytes);
    }

    internal static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Backup key must be 256 bits.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using (var aes = new AesGcm(key, tag.Length)) aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return JsonSerializer.SerializeToUtf8Bytes(
            new EncryptedBackupEnvelope(1, Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag), Convert.ToHexString(SHA256.HashData(plaintext)),
                Convert.ToBase64String(ciphertext)), JsonOptions);
    }

    internal static byte[] Decrypt(byte[] envelopeBytes, byte[] key)
    {
        var env = JsonSerializer.Deserialize<EncryptedBackupEnvelope>(envelopeBytes, JsonOptions)
                  ?? throw new InvalidDataException("Missing encrypted backup envelope.");
        if (env.Version != 1) throw new InvalidDataException("Unsupported backup format.");
        var nonce = Convert.FromBase64String(env.Nonce);
        var tag = Convert.FromBase64String(env.Tag);
        var ciphertext = Convert.FromBase64String(env.Ciphertext);
        if (nonce.Length != 12 || tag.Length != 16 || ciphertext.Length > MaxArchiveBytes)
            throw new InvalidDataException("Invalid encrypted snapshot size or nonce.");
        var plain = new byte[ciphertext.Length];
        try
        {
            using (var aes = new AesGcm(key, tag.Length)) aes.Decrypt(nonce, ciphertext, tag, plain);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(plain)), env.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Backup checksum mismatch.");
            return plain;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plain);
            throw;
        }
    }

    internal static int RestoreToIsolatedDirectory(byte[] envelopeBytes, byte[] key, string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Restore destination must be empty.");
        var plain = Decrypt(envelopeBytes, key);
        try
        {
            using var archive = new ZipArchive(new MemoryStream(plain, writable: false), ZipArchiveMode.Read);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var files = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith('/') || name.Split('/').Any(p => p is "" or "." or "..") ||
                    name.Contains(':')) throw new InvalidDataException("Unsafe backup entry name.");
                if (!(name.StartsWith("storage/") || name.StartsWith("configuration/") ||
                      name.StartsWith("credentials/") || name.StartsWith("extensions/") ||
                      name.StartsWith("meta/"))) throw new InvalidDataException("Unexpected backup entry.");
                var path = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Restore path escapes its isolated directory.");
                if (entry.Length > MaxSingleFileBytes || ++files > 10000)
                    throw new InvalidDataException("Restore entry limit exceeded.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: false);
            }
            return files;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    /// <summary>
    /// Verify and extract a GitHub snapshot only into an isolated empty directory.
    /// Live settings, extensions and secrets are never modified automatically.
    /// </summary>
    public static async Task<int> RestoreSnapshotToIsolatedDirectoryAsync(
        AppSettings settings, string path, string directory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("redundant-backup/v1/", StringComparison.Ordinal) ||
            !path.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unrecognized GitHub recovery snapshot path.", nameof(path));
        var backend = new GitHubPersonalSyncBackend(settings.PersonalSync.GitHub,
            PersonalSyncSecretStore.Load());
        var snapshot = await GitHubBackupChunkTransport.DownloadVerifiedChunksAsync(backend, path, cancellationToken);
        var key = LoadOrCreateKey();
        try { return RestoreToIsolatedDirectory(snapshot, key, directory); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    /// <summary>Explicit export only: store this secret offline, never in the GitHub repository.</summary>
    public static void ExportRecoveryKey(string destination)
    {
        if (File.Exists(destination)) throw new IOException("Recovery-key export must not overwrite a file.");
        var key = LoadOrCreateKey();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            File.WriteAllText(destination, "YANZI-GITHUB-RECOVERY-KEY-V1:" + Convert.ToBase64String(key));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    /// <summary>For disaster recovery on a new Windows device; only use a trusted offline key.</summary>
    public static void ImportRecoveryKey(string source)
    {
        if (File.Exists(RecoveryKeyPath))
            throw new InvalidOperationException("Refusing to replace an existing backup encryption key.");
        var text = File.ReadAllText(source).Trim();
        const string prefix = "YANZI-GITHUB-RECOVERY-KEY-V1:";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery-key file format mismatch.");
        var key = Convert.FromBase64String(text[prefix.Length..]);
        try
        {
            if (key.Length != 32) throw new InvalidDataException("Recovery key must be 256 bits.");
            var tmp = RecoveryKeyPath + ".tmp";
            File.WriteAllBytes(tmp, ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
            File.Move(tmp, RecoveryKeyPath, overwrite: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static byte[] LoadOrCreateKey()
    {
        if (File.Exists(RecoveryKeyPath))
        {
            var protectedBytes = File.ReadAllBytes(RecoveryKeyPath);
            var key = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            if (key.Length != 32) throw new CryptographicException("Invalid stored backup recovery key.");
            return key;
        }
        var newKey = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryKeyPath)!);
        var tmp = RecoveryKeyPath + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(newKey, null, DataProtectionScope.CurrentUser));
        File.Move(tmp, RecoveryKeyPath, overwrite: false);
        return newKey;
    }

    private static GitHubBackupState? LoadState()
    {
        try { return File.Exists(StatePath)
            ? JsonSerializer.Deserialize<GitHubBackupState>(File.ReadAllText(StatePath), JsonOptions)
            : null; }
        catch { return null; }
    }

    private static void SaveState(GitHubBackupState state)
    {
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tmp, StatePath, overwrite: true);
    }

    private sealed record EncryptedBackupEnvelope(int Version, string Nonce, string Tag,
        string Sha256, string Ciphertext);
    private sealed record GitHubBackupState(DateTimeOffset LastCheckedUtc, string ArchiveSha256,
        string LastSnapshotPath);
}
public sealed record GitHubBackupResult(bool Created, string Status, int ArchiveBytes, string? Path);
