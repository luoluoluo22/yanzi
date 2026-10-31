using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenQuickHost;

/// <summary>
/// A pinned, hash-verified EasyTier CLI runtime for optional P2P service modules.
/// Never modify system VPN, proxy, DNS, routes or another user's installation.
/// </summary>
internal static class YanziPortableEasyTierInstaller
{
    private const string Version = "2.6.4";
    private const string ArchiveHash = "27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84";
    private const string Archive = "easytier-windows-x86_64-v2.6.4.zip";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(6) };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "Runtimes", "EasyTier", Version);

    internal static async Task<(string Core, string Cli)> EnsureAsync(CancellationToken ct = default)
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("easytier_runtime_requires_windows_x64");
        var target = Path.Combine(Root, "easytier-windows-x86_64");
        var core = Path.Combine(target, "easytier-core.exe");
        var cli = Path.Combine(target, "easytier-cli.exe");
        if (File.Exists(core) && File.Exists(cli)) return (core, cli);

        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(core) && File.Exists(cli)) return (core, cli);
            Directory.CreateDirectory(Root);
            var staging = Path.Combine(Root, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var archive = Path.Combine(staging, "source.zip");
                var url = "https://github.com/EasyTier/EasyTier/releases/download/v" +
                    Version + "/" + Archive;
                using (var response = await Http.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is { } length && length > 100_000_000)
                        throw new InvalidDataException("easytier_archive_too_large");
                    await using var input = await response.Content.ReadAsStreamAsync(ct);
                    await using var output = File.Create(archive);
                    await input.CopyToAsync(output, ct);
                }
                if (new FileInfo(archive).Length is < 10_000_000 or > 100_000_000)
                    throw new InvalidDataException("easytier_archive_invalid_size");
                await using (var input = File.OpenRead(archive))
                {
                    var sha = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
                    if (!sha.Equals(ArchiveHash, StringComparison.Ordinal))
                        throw new InvalidDataException("easytier_archive_sha256_mismatch");
                }
                var extracted = Path.Combine(staging, "binary");
                Directory.CreateDirectory(extracted);
                // A strict filename allowlist avoids ZIP traversal and unexpected executables.
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "easytier-core.exe", "easytier-cli.exe", "Packet.dll", "WinDivert64.sys",
                    "wintun.dll"
                };
                using (var zip = ZipFile.OpenRead(archive))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var file = Path.GetFileName(entry.FullName);
                        if (!allowed.Contains(file) || entry.Length is < 0 or > 80_000_000)
                            continue;
                        if (File.Exists(Path.Combine(extracted, file)))
                            throw new InvalidDataException("easytier_duplicate_archive_entry");
                        entry.ExtractToFile(Path.Combine(extracted, file));
                    }
                }
                if (!File.Exists(Path.Combine(extracted, "easytier-core.exe")) ||
                    !File.Exists(Path.Combine(extracted, "easytier-cli.exe")))
                    throw new InvalidDataException("easytier_archive_missing_binaries");
                if (Directory.Exists(target))
                    throw new IOException("easytier_incomplete_runtime_requires_review");
                Directory.Move(extracted, target);
                return (core, cli);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        finally { Gate.Release(); }
    }
}
