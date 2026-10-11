using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenQuickHost;

/// <summary>Installs a verified Mihomo core on demand. The core is never bundled into Yanzi or extension ZIPs.</summary>
internal static class YanziPortableMihomoInstaller
{
    internal const string Version = "1.19.31";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(8) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "Runtimes", "mihomo");

    internal static IEnumerable<string> GetCachedExecutables()
    {
        if (!Directory.Exists(InstallRoot)) yield break;
        foreach (var path in Directory.EnumerateFiles(InstallRoot, "mihomo.exe", SearchOption.AllDirectories))
        {
            if (File.Exists(path)) yield return path;
        }
    }

    internal static async Task<string> EnsureAsync(Version? minimum, CancellationToken cancellationToken)
    {
        var release = new Version(1, 19, 31);
        if (minimum != null && minimum > release)
            throw new NotSupportedException($"Pin Mihomo {Version} does not satisfy {minimum}. Update the verified release manifest.");
        // Use the baseline x64 build to support older PCs, not the v3/v4 ISA-specific variants.
        var asset = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => $"mihomo-windows-amd64-compatible-v{Version}.zip",
            Architecture.Arm64 => $"mihomo-windows-arm64-v{Version}.zip",
            _ => throw new PlatformNotSupportedException("Mihomo runtime currently supports Windows x64 and ARM64.")
        };
        var expectedHash = RuntimeInformation.OSArchitecture == Architecture.X64
            ? "93d14e9a13b49b2f2d256202d02cc8d14a7c4695edf084cae0f941986bc9c218"
            : "68659624a38ae1dbc4e1b92f45d4dc75da0eaedd4d732b0a6f01a308da945792";
        var target = Path.Combine(InstallRoot, Version, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        var exe = Path.Combine(target, "mihomo.exe");
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(exe)) return exe;
            Directory.CreateDirectory(InstallRoot);
            var scratch = Path.Combine(InstallRoot, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var archive = Path.Combine(scratch, "core.zip");
                var url = $"https://github.com/MetaCubeX/mihomo/releases/download/v{Version}/{asset}";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var from = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var to = File.Create(archive);
                    await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
                }
                await using (var stream = File.OpenRead(archive))
                {
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                    if (!string.Equals(actual, expectedHash, StringComparison.Ordinal))
                        throw new InvalidDataException("Mihomo archive SHA256 verification failed.");
                }
                using (var zip = ZipFile.OpenRead(archive))
                {
                    // Accept only the single expected Windows binary; never extract arbitrary ZIP paths.
                    var binary = zip.Entries.SingleOrDefault(e =>
                        Path.GetFileName(e.FullName).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && e.Length > 1024 * 1024);
                    if (binary == null) throw new InvalidDataException("Verified Mihomo archive lacks a Windows binary.");
                    binary.ExtractToFile(Path.Combine(scratch, "mihomo.exe"));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var stagedBin = Path.Combine(scratch, "binary");
                Directory.CreateDirectory(stagedBin);
                File.Move(Path.Combine(scratch, "mihomo.exe"), Path.Combine(stagedBin, "mihomo.exe"));
                if (!Directory.Exists(target)) Directory.Move(stagedBin, target);
                else File.Move(Path.Combine(stagedBin, "mihomo.exe"), exe, false);
                if (!File.Exists(exe)) throw new IOException("Mihomo binary was not installed.");
                return exe;
            }
            finally
            {
                if (Directory.Exists(scratch))
                    try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
            }
        }
        finally { Gate.Release(); }
    }
}
