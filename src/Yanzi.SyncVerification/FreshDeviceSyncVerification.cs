using System.IO.Compression;
using System.Security.Cryptography;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class FreshDeviceSyncVerification
{
    public static async Task RunAccountOnlyAsync()
    {
        var sourceCommands = LocalExtensionCatalog.LoadCommands()
            .Where(command => !string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
            .ToDictionary(command => command.ExtensionId, StringComparer.OrdinalIgnoreCase);
        await VerifyAccountPrivateLibraryAsync(new CloudSyncClient(SyncConfigLoader.Load()), sourceCommands);
    }

    public static async Task RunAsync()
    {
        var originalRoot = HostAssets.DataRootPath;
        var settings = AppSettingsStore.Load();
        var liveBackend = PersonalSyncBackendFactory.Create(settings)
            ?? throw new InvalidOperationException("当前个人同步未启用或未配置，无法验证真实仓库下载。");
        var backend = new IsolatedRemoteOverlay(liveBackend);
        var accountClient = new CloudSyncClient(SyncConfigLoader.Load());
        var remoteIndex = SyncPackageSafety.ReadIndex(await backend.TryReadBytesAsync("index.json", default));
        var active = remoteIndex.Items.Where(item => !item.Deleted && !item.Purged).ToArray();
        var sourceCommands = LocalExtensionCatalog.LoadCommands()
            .Where(command => !string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
            .ToDictionary(command => command.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var sandbox = Path.Combine(Path.GetTempPath(), "yanzi-fresh-device-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            using (HostAssets.UseIsolatedDataRootForVerification(sandbox))
            {
                var service = new PersonalSyncService(backend);
                var deviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
                if (File.Exists(Path.Combine(originalRoot, "device-identity.json")) &&
                    File.ReadAllText(Path.Combine(originalRoot, "device-identity.json")).Contains(deviceId, StringComparison.Ordinal))
                    throw new InvalidOperationException("模拟设备复用了原设备身份。");

                var first = await service.SyncExtensionsAsync(PersonalConfigSyncMode.Bidirectional);
                var yanmFirst = await service.SyncYanmStateAsync();
                var failures = new List<string>();
                var matchedSource = 0;
                var sourceDifferent = new List<string>();
                var sourceMissing = new List<string>();
                var restoredDifferent = new List<string>();
                var restoredCommands = LocalExtensionCatalog.LoadCommands().ToDictionary(command => command.ExtensionId, StringComparer.OrdinalIgnoreCase);
                foreach (var item in active)
                {
                    var package = await backend.TryReadBytesAsync(item.PackagePath, default);
                    if (package == null)
                    {
                        failures.Add($"{item.Title}: 远端包缺失");
                        continue;
                    }

                    try
                    {
                        SyncPackageSafety.VerifyHash(package, item.PackageHash);
                        VerifyInstalledFiles(item, package);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{item.Title}: {ex.Message}");
                    }

                    if (restoredCommands.TryGetValue(item.ExtensionId, out var restoredCommand))
                    {
                        var rebuilt = ExtensionPackageService.BuildPackage(restoredCommand, restoredCommand.DeclaredVersion);
                        var rebuiltHash = Convert.ToHexString(SHA256.HashData(rebuilt)).ToLowerInvariant();
                        if (!rebuiltHash.Equals(item.PackageHash, StringComparison.OrdinalIgnoreCase))
                            restoredDifferent.Add($"{item.Title} ({item.ExtensionId}, {item.PackageHash[..8]} -> {rebuiltHash[..8]})");
                    }

                    if (!sourceCommands.TryGetValue(item.ExtensionId, out var command))
                    {
                        sourceMissing.Add(item.Title);
                        continue;
                    }

                    var sourcePackage = ExtensionPackageService.BuildPackage(command, command.DeclaredVersion);
                    var sourceHash = Convert.ToHexString(SHA256.HashData(sourcePackage)).ToLowerInvariant();
                    if (sourceHash.Equals(item.PackageHash, StringComparison.OrdinalIgnoreCase)) matchedSource++;
                    else sourceDifferent.Add(item.Title);
                }

                if (first.UploadedCount != 0 || first.PulledCount != active.Length)
                    failures.Add($"首次同步计数异常：上传 {first.UploadedCount}，下载 {first.PulledCount}，远端有效项目 {active.Length}");
                if (yanmFirst.Uploaded || !yanmFirst.Pulled)
                    failures.Add($"燕幕状态首次同步异常：上传 {yanmFirst.Uploaded}，下载 {yanmFirst.Pulled}");

                var second = await service.SyncExtensionsAsync(PersonalConfigSyncMode.Bidirectional);
                var yanmSecond = await service.SyncYanmStateAsync();
                if (second.UploadedCount != 0 || second.PulledCount != 0 || second.ConfigUploaded || second.ConfigPulled ||
                    yanmSecond.Uploaded || yanmSecond.Pulled)
                    failures.Add("第二次同步未保持静默，存在重复上传或下载。");

                Console.WriteLine($"REMOTE_INDEX={remoteIndex.Items.Count}; ACTIVE={active.Length}; FIRST_UPLOAD={first.UploadedCount}; FIRST_DOWNLOAD={first.PulledCount}; CONFIG_PULL={first.ConfigPulled}; YANM_PULL={yanmFirst.Pulled}");
                Console.WriteLine($"SECOND_UPLOAD={second.UploadedCount}; SECOND_DOWNLOAD={second.PulledCount}; SECOND_CONFIG_UPLOAD={second.ConfigUploaded}; SECOND_YANM_UPLOAD={yanmSecond.Uploaded}");
                Console.WriteLine($"ORIGINAL_MATCH={matchedSource}; ORIGINAL_DIFFERENT={sourceDifferent.Count}; ORIGINAL_MISSING={sourceMissing.Count}; REMOTE_WRITES_INTERCEPTED={backend.WriteCount}; REMOTE_DELETES_INTERCEPTED={backend.DeleteCount}; BYTES_READ={backend.BytesRead}");
                if (backend.WrittenPaths.Count > 0) Console.WriteLine("INTERCEPTED_WRITE_PATHS=" + string.Join(", ", backend.WrittenPaths));
                if (restoredDifferent.Count > 0) Console.WriteLine("RESTORED_REPACK_DIFFERENT=" + string.Join(", ", restoredDifferent));
                if (sourceDifferent.Count > 0) Console.WriteLine("ORIGINAL_DIFFERENT_NAMES=" + string.Join(", ", sourceDifferent));
                if (sourceMissing.Count > 0) Console.WriteLine("ORIGINAL_MISSING_NAMES=" + string.Join(", ", sourceMissing));
                if (failures.Count > 0) throw new InvalidOperationException("新设备同步验证失败：" + string.Join(" | ", failures));
            }
            await VerifyAccountPrivateLibraryAsync(accountClient, sourceCommands);
            Console.WriteLine("Fresh-device sync verification passed.");
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static async Task VerifyAccountPrivateLibraryAsync(
        CloudSyncClient client,
        IReadOnlyDictionary<string, CommandItem> sourceCommands)
    {
        if (!client.HasCredential) throw new InvalidOperationException("缺少账号登录凭据，无法验证实际启动下载路径。");
        var allAccountItems = await client.GetUserExtensionsAsync();
        var configIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "yanzi-webdav-settings", "yanzi-quickpanel-settings", "yanzi-personal-sync-settings",
            "yanzi-ai-settings", "yanzi-general-settings"
        };
        var items = allAccountItems
            .Where(item => item.Enabled != 0 && item.HasArchive &&
                           !string.IsNullOrWhiteSpace(item.ExtensionId) && !configIds.Contains(item.ExtensionId))
            .ToArray();
        var accountIds = items.Select(item => item.ExtensionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onlyOnOriginal = sourceCommands.Values.Where(command => !accountIds.Contains(command.ExtensionId))
            .Select(command => command.Title).ToArray();
        var omittedDetails = sourceCommands.Values.Where(command => !accountIds.Contains(command.ExtensionId))
            .Select(command =>
            {
                var account = allAccountItems.FirstOrDefault(item => item.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                return $"{command.Title}: account={(account == null ? "absent" : $"private={account.IsPrivate}/enabled={account.Enabled}/archive={account.HasArchive}")}";
            }).ToArray();
        var sandbox = Path.Combine(Path.GetTempPath(), "yanzi-account-device-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var failures = new List<string>();
        var originalMatched = 0;
        var originalDifferent = new List<string>();
        var originalMissing = new List<string>();
        long bytesRead = 0;
        try
        {
            using (HostAssets.UseIsolatedDataRootForVerification(sandbox))
            {
                LocalExtensionCatalog.EnsureSampleExtension();
                foreach (var item in items)
                {
                    try
                    {
                        var package = await client.DownloadMyExtensionArchiveAsync(item.ExtensionId);
                        bytesRead += package.Length;
                        var cloudHash = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(item.ArchiveSha256) &&
                            !cloudHash.Equals(item.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("账号索引与下载包的 SHA-256 不一致");

                        var installed = await ExtensionInstallService.InstallPackageAsync(
                            package, item.ExtensionId, fallbackName: item.DisplayName);
                        if (!installed.ExtensionId.Equals(item.ExtensionId, StringComparison.OrdinalIgnoreCase) ||
                            !File.Exists(Path.Combine(installed.DirectoryPath, "manifest.json")))
                            throw new InvalidDataException("安装结果缺少小程序或 ID 不一致");

                        if (sourceCommands.TryGetValue(item.ExtensionId, out var source))
                        {
                            var original = ExtensionPackageService.BuildPackage(source, source.DeclaredVersion);
                            if (original.SequenceEqual(package)) originalMatched++;
                            else originalDifferent.Add($"{item.ExtensionId} ({DescribePackageDifference(original, package)})");
                        }
                        else originalMissing.Add(item.ExtensionId);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{item.ExtensionId}: {ex.Message}");
                    }
                }

                var installedIds = LocalExtensionCatalog.LoadCommands()
                    .Select(command => command.ExtensionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingInstall = items.Where(item => !installedIds.Contains(item.ExtensionId)).Select(item => item.ExtensionId).ToArray();
                if (missingInstall.Length > 0) failures.Add("账号包未出现在新设备目录：" + string.Join(", ", missingInstall));
                var missingFromOriginal = sourceCommands.Values.Where(command => !installedIds.Contains(command.ExtensionId))
                    .Select(command => command.Title).ToArray();
                var extraOnFreshDevice = installedIds.Where(id => !sourceCommands.ContainsKey(id)).ToArray();
                Console.WriteLine($"FRESH_TOTAL={installedIds.Count}; ORIGINAL_TOTAL={sourceCommands.Count}; FRESH_MISSING_FROM_ORIGINAL={missingFromOriginal.Length}; FRESH_EXTRA={extraOnFreshDevice.Length}");
                if (missingFromOriginal.Length > 0) Console.WriteLine("FRESH_MISSING_NAMES=" + string.Join(", ", missingFromOriginal));
                if (extraOnFreshDevice.Length > 0) Console.WriteLine("FRESH_EXTRA_IDS=" + string.Join(", ", extraOnFreshDevice));
            }
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }

        Console.WriteLine($"ACCOUNT_ACTIVE={items.Length}; ACCOUNT_INSTALLED={items.Length - failures.Count}; ACCOUNT_ORIGINAL_BYTE_MATCH={originalMatched}; ACCOUNT_ORIGINAL_DIFFERENT={originalDifferent.Count}; ACCOUNT_ORIGINAL_MISSING={originalMissing.Count}; ACCOUNT_BYTES_READ={bytesRead}");
        Console.WriteLine($"ORIGINAL_NOT_IN_ACCOUNT={onlyOnOriginal.Length}; ORIGINAL_NOT_IN_ACCOUNT_NAMES={string.Join(", ", onlyOnOriginal)}");
        Console.WriteLine("ORIGINAL_NOT_IN_ACCOUNT_DETAILS=" + string.Join("; ", omittedDetails));
        if (originalDifferent.Count > 0) Console.WriteLine("ACCOUNT_ORIGINAL_DIFFERENT_IDS=" + string.Join(", ", originalDifferent));
        if (originalMissing.Count > 0) Console.WriteLine("ACCOUNT_ORIGINAL_MISSING_IDS=" + string.Join(", ", originalMissing));
        if (failures.Count > 0) throw new InvalidOperationException("账号私有库新设备下载失败：" + string.Join(" | ", failures));
    }

    private static string DescribePackageDifference(byte[] original, byte[] remote)
    {
        static Dictionary<string, string> ReadHashes(byte[] bytes)
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return zip.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToDictionary(
                entry => entry.FullName,
                entry =>
                {
                    using var stream = entry.Open();
                    return Convert.ToHexString(SHA256.HashData(stream));
                }, StringComparer.OrdinalIgnoreCase);
        }

        var source = ReadHashes(original);
        var cloud = ReadHashes(remote);
        var differences = source.Keys.Union(cloud.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(path => !source.TryGetValue(path, out var left) || !cloud.TryGetValue(path, out var right) || left != right)
            .Take(10);
        return string.Join(",", differences);
    }

    private static void VerifyInstalledFiles(WebDavSyncEntry item, byte[] package)
    {
        var root = SyncPackageSafety.ResolveExtensionDirectory(HostAssets.ExtensionsPath, item.ExtensionId);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("下载后目录不存在");
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            expected.Add(relative);
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException($"包中文件未落地：{entry.FullName}");
            using var stream = entry.Open();
            var expectedHash = SHA256.HashData(stream);
            using var actualStream = File.OpenRead(path);
            if (!expectedHash.SequenceEqual(SHA256.HashData(actualStream)))
                throw new InvalidDataException($"文件内容不同：{entry.FullName}");
        }

        var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(expected)) throw new InvalidDataException("下载目录与云端包的文件清单不一致");
    }

    private sealed class IsolatedRemoteOverlay(IPersonalSyncBackend live) : IPersonalSyncBackend
    {
        private readonly Dictionary<string, byte[]?> _overlay = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);
        public string DisplayRoot => live.DisplayRoot + " (isolated verification)";
        public int WriteCount { get; private set; }
        public List<string> WrittenPaths { get; } = [];
        public int DeleteCount { get; private set; }
        public long BytesRead { get; private set; }
        public Task ProbeAsync(CancellationToken cancellationToken) => live.ProbeAsync(cancellationToken);

        public async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
        {
            if (_overlay.TryGetValue(relativePath, out var overlaid)) return overlaid;
            if (_cache.TryGetValue(relativePath, out var cached)) return cached;
            var bytes = await live.TryReadBytesAsync(relativePath, cancellationToken);
            _cache[relativePath] = bytes;
            BytesRead += bytes?.Length ?? 0;
            return bytes;
        }

        public Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
        {
            _overlay[relativePath] = content;
            WriteCount++;
            WrittenPaths.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
        {
            _overlay[relativePath] = null;
            DeleteCount++;
            return Task.CompletedTask;
        }
    }
}
