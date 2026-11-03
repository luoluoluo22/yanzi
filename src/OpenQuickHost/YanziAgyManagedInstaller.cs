using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Downloads the official AGY CLI binary using the official updater manifest and
/// its SHA-512. Deletes only a verified binary installed into this private directory.
/// Existing independent AGY installations and Google login data are never touched.
/// </summary>
internal static class YanziAgyManagedInstaller
{
    private const string ManifestBase = "https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/";
    private const string Origin = "antigravity-official-manifest-sha512";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "ManagedDependencies", "yanzi-agy-cli");
    private static string Binary => Path.Combine(Root, "agy.exe");
    private static string ReceiptFile => Path.Combine(Root, "ownership.json");

    private sealed class ReleaseManifest
    {
        public string? Version { get; set; }
        public string? Url { get; set; }
        public string? Sha512 { get; set; }
    }
    private sealed class Receipt
    {
        public int SchemaVersion { get; set; } = 1;
        public string Origin { get; set; } = YanziAgyManagedInstaller.Origin;
        public string? Sha256 { get; set; }
        public string? Version { get; set; }
    }
    private static bool IsSafeOwnedFile(string path) =>
        File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    internal static string? FindOwned()
    {
        if (!Directory.Exists(Root) || (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0 ||
            !IsSafeOwnedFile(Binary) || !IsSafeOwnedFile(ReceiptFile)) return null;
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(ReceiptFile));
            if (receipt == null || receipt.SchemaVersion != 1 || receipt.Origin != Origin ||
                receipt.Sha256?.Length != 64 || !receipt.Sha256.All(Uri.IsHexDigit))
                return null;
            using var stream = File.OpenRead(Binary);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, receipt.Sha256, StringComparison.OrdinalIgnoreCase) ? Binary : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static async Task<string> EnsureInstalledAsync(CancellationToken cancellation = default)
    {
        var independent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        if (File.Exists(independent)) return independent;
        var owned = FindOwned();
        if (owned != null) return owned;
        await Gate.WaitAsync(cancellation);
        try
        {
            if (File.Exists(independent)) return independent;
            if ((owned = FindOwned()) != null) return owned;
            if (Directory.Exists(Root))
            {
                if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("AGY managed directory is a reparse point.");
                // Unrecognized or tampered installations are never overwritten.
                if (File.Exists(Binary) || File.Exists(ReceiptFile))
                    throw new IOException("AGY managed directory contains unowned files.");
            }
            else Directory.CreateDirectory(Root);
            var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            var platform = architecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "windows_amd64",
                System.Runtime.InteropServices.Architecture.Arm64 => "windows_arm64",
                _ => throw new PlatformNotSupportedException("AGY CLI requires x64 or arm64.")
            };
            using var manifestResponse = await Client.GetAsync(ManifestBase + platform + ".json", cancellation);
            manifestResponse.EnsureSuccessStatusCode();
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(
                await manifestResponse.Content.ReadAsStringAsync(cancellation),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new IOException("AGY manifest missing.");
            if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var source) ||
                source.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(source.Host, "storage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
                manifest.Sha512?.Length != 128 || !manifest.Sha512.All(Uri.IsHexDigit))
                throw new IOException("AGY official release manifest validation failed.");
            var staging = Path.Combine(Root, "agy.exe.partial");
            if (File.Exists(staging)) File.Delete(staging);
            try
            {
                using var response = await Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellation);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 350_000_000)
                    throw new IOException("AGY binary exceeds allowed size.");
                await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellation))
                await using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await sourceStream.CopyToAsync(target, cancellation);
                var expected = manifest.Sha512.ToUpperInvariant();
                using var verify = File.OpenRead(staging);
                var actual = Convert.ToHexString(SHA512.HashData(verify));
                if (actual != expected) throw new IOException("AGY SHA-512 verification failed.");
                verify.Position = 0;
                var sha256 = Convert.ToHexString(SHA256.HashData(verify));
                // Register ownership before publishing the binary. An interrupted operation
                // leaves a receipt or partial file; it never licenses removal of user files.
                var receipt = new Receipt { Version = manifest.Version, Sha256 = sha256 };
                await File.WriteAllTextAsync(ReceiptFile, JsonSerializer.Serialize(receipt), cancellation);
                File.Move(staging, Binary);
                return Binary;
            }
            finally
            {
                if (File.Exists(staging) && (File.GetAttributes(staging) & FileAttributes.ReparsePoint) == 0)
                    File.Delete(staging);
            }
        }
        finally { Gate.Release(); }
    }

    internal static string CleanupOwnedOnUninstall()
    {
        if (!Directory.Exists(Root)) return "absent";
        var owned = FindOwned();
        if (owned == null) return "unowned-or-modified-preserved";
        foreach (var process in Process.GetProcessesByName("agy"))
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, owned, StringComparison.OrdinalIgnoreCase))
                    continue;
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(3000)) return "owned-process-busy";
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { return "owned-process-access-denied"; }
            finally { process.Dispose(); }
        }
        if (FindOwned() == null) return "ownership-changed";
        File.Delete(Binary);
        File.Delete(ReceiptFile);
        if (!Directory.EnumerateFileSystemEntries(Root).Any()) Directory.Delete(Root);
        return "removed-owned-binary";
    }
}
