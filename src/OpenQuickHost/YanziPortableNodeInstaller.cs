using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Host-owned, per-user Node fallback for PCs where WinGet is unavailable or fails.
/// Extensions must declare manifest.requires; none should implement their own runtime downloader.
/// </summary>
internal static class YanziPortableNodeInstaller
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    internal static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "Runtimes", "node");

    internal static IEnumerable<string> GetCachedExecutables()
    {
        if (!Directory.Exists(InstallRoot))
            yield break;

        foreach (var directory in Directory.EnumerateDirectories(InstallRoot, "v*"))
        {
            var nodeExe = Path.Combine(directory, "node.exe");
            if (File.Exists(nodeExe) && File.Exists(Path.Combine(directory, "npm.cmd")))
                yield return nodeExe;
        }
    }

    internal static async Task<string> EnsureAsync(Version? minimumVersion, CancellationToken cancellationToken)
    {
        if (!Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("Node 便携版安装需要 64 位 Windows。");

        Directory.CreateDirectory(InstallRoot);

        // The exclusive file handle remains valid across awaited operations and threads.
        // A Windows Mutex is thread-affine and cannot safely span async continuations.
        await using var installLock = await AcquireInstallLockAsync(cancellationToken)
            .ConfigureAwait(false);
        {
            foreach (var candidate in GetCachedExecutables())
            {
                var version = ReadDirectoryVersion(Path.GetFileName(Path.GetDirectoryName(candidate)));
                if (version != null && (minimumVersion == null || version >= minimumVersion))
                    return candidate;
            }

            var architecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException("当前 Windows CPU 架构不支持 Node 便携版。")
            };

            // Prefer the newest supported LTS; its official index records which archives exist.
            using var index = JsonDocument.Parse(await Client.GetStringAsync(
                "https://nodejs.org/dist/index.json", cancellationToken));
            string? selectedVersion = null;
            Version? selected = null;
            var platform = "win-" + architecture + "-zip";

            foreach (var release in index.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("version", out var versionElement) ||
                    !release.TryGetProperty("lts", out var ltsElement) ||
                    ltsElement.ValueKind != JsonValueKind.String ||
                    !release.TryGetProperty("files", out var filesElement) ||
                    filesElement.ValueKind != JsonValueKind.Array)
                    continue;

                var name = versionElement.GetString();
                var version = ReadDirectoryVersion(name);
                if (name == null || version == null || version.Major < 22 ||
                    (minimumVersion != null && version < minimumVersion) ||
                    !filesElement.EnumerateArray().Any(file => file.GetString() == platform))
                    continue;

                if (selected == null || version > selected)
                {
                    selected = version;
                    selectedVersion = name;
                }
            }

            if (selectedVersion == null)
                throw new InvalidOperationException("Node 官方发行列表中没有满足版本要求的 Windows LTS 便携包。");

            var archiveName = "node-" + selectedVersion + "-win-" + architecture + ".zip";
            var releaseUrl = "https://nodejs.org/dist/" + selectedVersion + "/";
            var checksums = await Client.GetStringAsync(releaseUrl + "SHASUMS256.txt", cancellationToken);
            var expectedHash = checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 2 && parts[1] == archiveName &&
                    parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
                .Select(parts => parts[0])
                .SingleOrDefault();
            if (expectedHash == null)
                throw new InvalidDataException("Node 官方 SHA-256 清单缺少目标安装包。");

            var target = Path.Combine(InstallRoot, selectedVersion);
            var scratch = Path.Combine(InstallRoot, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var archive = Path.Combine(scratch, archiveName);
                using (var response = await Client.GetAsync(
                    releaseUrl + archiveName, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var destination = File.Create(archive);
                    await source.CopyToAsync(destination, cancellationToken);
                }

                await using (var archiveStream = File.OpenRead(archive))
                {
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken));
                    if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Node 安装包 SHA-256 与官方清单不一致，拒绝安装。");
                }

                var extracted = Path.Combine(scratch, "extracted");
                ZipFile.ExtractToDirectory(archive, extracted);
                var unpacked = Path.Combine(extracted,
                    "node-" + selectedVersion + "-win-" + architecture);
                if (!File.Exists(Path.Combine(unpacked, "node.exe")) ||
                    !File.Exists(Path.Combine(unpacked, "npm.cmd")))
                    throw new InvalidDataException("官方 Node 压缩包缺少 node.exe 或 npm.cmd。");

                if (!Directory.Exists(target))
                    Directory.Move(unpacked, target);

                var installed = Path.Combine(target, "node.exe");
                if (!File.Exists(installed) || !File.Exists(Path.Combine(target, "npm.cmd")))
                    throw new InvalidDataException("Node 运行时目录安装不完整。");

                return installed;
            }
            finally
            {
                try { Directory.Delete(scratch, recursive: true); }
                catch (IOException) { /* Future cleanup can remove an interrupted scratch dir. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task<FileStream> AcquireInstallLockAsync(CancellationToken cancellationToken)
    {
        var lockFile = Path.Combine(InstallRoot, ".install.lock");
        for (var attempt = 0; attempt < 300; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockFile, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new TimeoutException("另一个燕子进程正在安装 Node，等待超时。");
    }

    private static Version? ReadDirectoryVersion(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !folder.StartsWith('v'))
            return null;
        return Version.TryParse(folder[1..], out var version) ? version : null;
    }
}
