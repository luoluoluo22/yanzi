using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Win32;

namespace OpenQuickHost;

internal static class InstalledApplicationCatalog
{
    private static readonly Regex DuplicateSuffixRegex = new(@"\s*\((?:\d+|副本|快捷方式)\)$|\s*-\s*(?:副本|快捷方式)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<InstalledApplicationEntry> Load()
    {
        var rawEntries = new List<InstalledApplicationEntry>();
        var totalScannedFiles = 0;
        var totalSkippedDirectories = 0;

        foreach (var scanRoot in GetScanRoots())
        {
            var root = scanRoot.Path;
            if (!Directory.Exists(root))
            {
                continue;
            }

            var scanResult = EnumerateSupportedFiles(root, scanRoot.Recurse);

            foreach (var file in scanResult.Files)
            {
                var entry = TryCreateEntry(file);
                if (entry != null)
                {
                    rawEntries.Add(entry);
                }
            }

            totalScannedFiles += scanResult.ScannedFiles;
            totalSkippedDirectories += scanResult.SkippedDirectories;
            HostAssets.AppendLog(
                $"InstalledApplicationCatalog root scanned: path={root}, recurse={scanRoot.Recurse}, files={scanResult.ScannedFiles}, skippedDirectories={scanResult.SkippedDirectories}, acceptedSoFar={rawEntries.Count}.");
        }

        ScanRegistryApplications(rawEntries);
        ScanAppsFolder(rawEntries);

        var finalResults = DeduplicateAndRefine(rawEntries);

        HostAssets.AppendLog(
            $"InstalledApplicationCatalog load summary: roots={GetScanRoots().Count()}, scannedFiles={totalScannedFiles}, skippedDirectories={totalSkippedDirectories}, raw={rawEntries.Count}, accepted={finalResults.Count}.");

        return finalResults;
    }

    private static IReadOnlyList<InstalledApplicationEntry> DeduplicateAndRefine(
        List<InstalledApplicationEntry> rawEntries)
    {
        if (rawEntries.Count == 0)
        {
            return rawEntries;
        }

        // 阶段一：相同启动命令去重（LaunchTarget + Arguments）
        // 目标和参数完全一致说明启动行为100%相同，只保留质量最高的一项（例如 Debuggable Package Manager 去除多余副本）
        var commandGroups = new Dictionary<string, List<InstalledApplicationEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rawEntries)
        {
            var cmdKey = $"{entry.NormalizedLaunchTarget}|{entry.NormalizedArguments}";
            if (!commandGroups.TryGetValue(cmdKey, out var list))
            {
                list = new List<InstalledApplicationEntry>();
                commandGroups[cmdKey] = list;
            }
            list.Add(entry);
        }

        var uniqueByCommand = new List<InstalledApplicationEntry>();
        foreach (var group in commandGroups.Values)
        {
            if (group.Count == 1)
            {
                uniqueByCommand.Add(group[0]);
            }
            else
            {
                var best = group.OrderByDescending(CalculateEntryQuality).First();
                uniqueByCommand.Add(best);
            }
        }

        // 阶段一半：同标题 + 同物理 exe 视为同一个应用入口。
        // 开始菜单常会额外带 --scene=startmenu 等上下文参数；不能因此重复显示一次。
        var executableIdentityGroups = new Dictionary<string, List<InstalledApplicationEntry>>(StringComparer.OrdinalIgnoreCase);
        var uniqueByExecutableIdentity = new List<InstalledApplicationEntry>();
        foreach (var entry in uniqueByCommand)
        {
            if (!string.IsNullOrWhiteSpace(entry.DisplayPath) &&
                entry.DisplayPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var key = $"{entry.NormalizedTitle}|{entry.NormalizedDisplayPath}";
                if (!executableIdentityGroups.TryGetValue(key, out var list))
                {
                    list = new List<InstalledApplicationEntry>();
                    executableIdentityGroups[key] = list;
                }
                list.Add(entry);
            }
            else
            {
                uniqueByExecutableIdentity.Add(entry);
            }
        }

        foreach (var group in executableIdentityGroups.Values)
        {
            var best = group
                .OrderByDescending(entry => CalculateEntryQuality(entry) +
                    (string.IsNullOrWhiteSpace(entry.Arguments) ? 20 : 0))
                .First();
            uniqueByExecutableIdentity.Add(best);
        }

        // 阶段二：标题清洗与副本编号智能微调（例如 Visual Studio 2022 (2) -> Visual Studio 2022）
        var refinedEntries = new List<InstalledApplicationEntry>(uniqueByExecutableIdentity.Count);
        foreach (var entry in uniqueByExecutableIdentity)
        {
            var refinedTitle = RefineDuplicateTitle(entry, uniqueByCommand);
            if (!string.Equals(refinedTitle, entry.Title, StringComparison.Ordinal))
            {
                refinedEntries.Add(WithTitle(entry, refinedTitle));
            }
            else
            {
                refinedEntries.Add(entry);
            }
        }

        // 阶段三：同名但不同启动目标的应用必须共存。
        // 过去这里按 Title 只保留一项，会把新版/旧版微信、多个 IDE 版本等错误合并。
        // 现在保留质量最高的一项作为自然名称，其余条目加可读后缀并使用路径稳定 ID。
        var titleGroups = new Dictionary<string, List<InstalledApplicationEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in refinedEntries)
        {
            var titleKey = entry.NormalizedTitle;
            if (!titleGroups.TryGetValue(titleKey, out var list))
            {
                list = new List<InstalledApplicationEntry>();
                titleGroups[titleKey] = list;
            }
            list.Add(entry);
        }

        var uniqueByTitle = new List<InstalledApplicationEntry>();
        foreach (var group in titleGroups.Values)
        {
            if (group.Count == 1)
            {
                uniqueByTitle.Add(group[0]);
                continue;
            }

            var ordered = group
                .OrderByDescending(CalculateEntryQuality)
                .ThenBy(entry => entry.DisplayPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            uniqueByTitle.Add(ordered[0]);
            for (var index = 1; index < ordered.Length; index++)
            {
                var variant = ordered[index];
                var variantTitle = BuildVariantTitle(ordered[0], variant, index + 1);
                var variantId = $"app-{ComputeStableId(variantTitle, variant.DisplayPath, variant.LaunchTarget + "|" + variant.NormalizedArguments)}";
                uniqueByTitle.Add(WithTitleAndId(variant, variantTitle, variantId));
            }
        }

        // 阶段四：同一个 DisplayPath 且无参数的条目去重（例如桌面和开始菜单都有同一个裸 exe 且无参数）
        var exeGroups = new Dictionary<string, List<InstalledApplicationEntry>>(StringComparer.OrdinalIgnoreCase);
        var finalResults = new List<InstalledApplicationEntry>();

        foreach (var entry in uniqueByTitle)
        {
            if (string.IsNullOrWhiteSpace(entry.Arguments) &&
                !string.IsNullOrWhiteSpace(entry.DisplayPath) &&
                entry.DisplayPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var exeKey = entry.NormalizedDisplayPath;
                if (!exeGroups.TryGetValue(exeKey, out var list))
                {
                    list = new List<InstalledApplicationEntry>();
                    exeGroups[exeKey] = list;
                }
                list.Add(entry);
            }
            else
            {
                finalResults.Add(entry);
            }
        }

        foreach (var group in exeGroups.Values)
        {
            if (group.Count == 1)
            {
                finalResults.Add(group[0]);
            }
            else
            {
                var best = group.OrderByDescending(CalculateEntryQuality).First();
                finalResults.Add(best);
            }
        }

        return finalResults
            .OrderBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CalculateEntryQuality(InstalledApplicationEntry entry)
    {
        var score = 100;

        // 优先没有 (2)、副本等后缀的项
        if (DuplicateSuffixRegex.IsMatch(entry.Title))
        {
            score -= 50;
        }

        // 优先没有 Preview、Beta 的项（如果有正式版同命令项）
        if (entry.Title.Contains("Preview", StringComparison.OrdinalIgnoreCase) ||
            entry.Title.Contains("Beta", StringComparison.OrdinalIgnoreCase))
        {
            score -= 20;
        }

        // 物理本地文件和快捷方式优于 shell:AppsFolder 虚拟项
        if (!entry.LaunchTarget.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        // 快捷方式通常包含完整环境配置（工作目录、参数等），优于裸 exe
        if (entry.LaunchTarget.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        // 优先安装在标准 Program Files 或 Local Programs 目录
        var normPath = entry.DisplayPath.ToLowerInvariant();
        if (normPath.Contains(@"\program files") || normPath.Contains(@"\local\programs"))
        {
            score += 25;
        }
        else if (normPath.Contains(@"\desktop") || normPath.Contains(@"\备用") || normPath.Contains(@"\temp"))
        {
            score -= 15;
        }

        // 优先有图标的项
        if (!string.IsNullOrWhiteSpace(entry.IconPath))
        {
            score += 5;
        }

        // 已知应用的 ExecutableNames 顺序代表推荐优先级。
        // 例如微信优先 Weixin.exe（新版），WeChat.exe（旧版）仍保留为可选版本。
        var known = KnownApplicationCatalog.Match(entry.Title, entry.DisplayPath);
        var executableName = Path.GetFileName(entry.DisplayPath);
        if (known != null && !string.IsNullOrWhiteSpace(executableName))
        {
            var preferredIndex = known.ExecutableNames
                .Select((name, index) => new { name, index })
                .FirstOrDefault(item => executableName.Equals(item.name, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;
            if (preferredIndex >= 0)
                score += Math.Max(10, 60 - preferredIndex * 15);
        }

        // 参数有时代表完整配置，但只作为轻量信号；同 exe 的去重阶段优先裸启动入口。
        if (!string.IsNullOrWhiteSpace(entry.Arguments))
        {
            score += 5;
        }

        // 标题越简洁优雅得分稍高
        score -= Math.Min(entry.Title.Length, 30);

        return score;
    }

    private static string RefineDuplicateTitle(
        InstalledApplicationEntry entry,
        IReadOnlyList<InstalledApplicationEntry> allEntries)
    {
        var title = entry.Title;
        var match = DuplicateSuffixRegex.Match(title);
        var cleanTitle = DuplicateSuffixRegex.Replace(title, "").Trim();

        // 仅对开发人员命令提示符/PowerShell这类由 BuildTools 和 Community 共存导致的副本进行区分标注
        var isDevConsole = cleanTitle.Contains("Command Prompt", StringComparison.OrdinalIgnoreCase) ||
                           cleanTitle.Contains("PowerShell", StringComparison.OrdinalIgnoreCase);

        if (isDevConsole)
        {
            var info = $"{entry.Arguments} {entry.DisplayPath}".ToLowerInvariant();
            if (info.Contains("community"))
            {
                var hasBuildToolsSibling = allEntries.Any(other =>
                    other != entry &&
                    DuplicateSuffixRegex.Replace(other.Title, "").Trim().Equals(cleanTitle, StringComparison.OrdinalIgnoreCase) &&
                    $"{other.Arguments} {other.DisplayPath}".ToLowerInvariant().Contains("buildtools"));

                if (hasBuildToolsSibling)
                {
                    return $"{cleanTitle} (Community)";
                }
            }
            else if (info.Contains("buildtools"))
            {
                var hasCommunitySibling = allEntries.Any(other =>
                    other != entry &&
                    DuplicateSuffixRegex.Replace(other.Title, "").Trim().Equals(cleanTitle, StringComparison.OrdinalIgnoreCase) &&
                    $"{other.Arguments} {other.DisplayPath}".ToLowerInvariant().Contains("community"));

                if (hasCommunitySibling)
                {
                    return $"{cleanTitle} (Build Tools)";
                }
            }
        }

        if (!match.Success)
        {
            return title;
        }

        // 普通 (2) 副本，检查列表中是否已经有基础同名项
        var hasCleanSibling = allEntries.Any(other =>
            other != entry &&
            other.Title.Trim().Equals(cleanTitle, StringComparison.OrdinalIgnoreCase));

        return hasCleanSibling ? title : cleanTitle;
    }

    private static InstalledApplicationEntry WithTitle(InstalledApplicationEntry source, string newTitle)
        => WithTitleAndId(source, newTitle, source.ExtensionId);

    private static InstalledApplicationEntry WithTitleAndId(
        InstalledApplicationEntry source,
        string newTitle,
        string extensionId)
    {
        var aliases = source.Keywords
            .Append(source.Title)
            .Append(newTitle)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new InstalledApplicationEntry(
            extensionId,
            newTitle,
            source.Subtitle,
            source.LaunchTarget,
            source.DisplayPath,
            source.IconPath,
            aliases,
            source.Arguments,
            source.WorkingDirectory);
    }

    private static string BuildVariantTitle(
        InstalledApplicationEntry primary,
        InstalledApplicationEntry variant,
        int ordinal)
    {
        var primaryExe = Path.GetFileNameWithoutExtension(primary.DisplayPath);
        var variantExe = Path.GetFileNameWithoutExtension(variant.DisplayPath);

        if (primary.Title.Equals("微信", StringComparison.OrdinalIgnoreCase))
        {
            if (variantExe.Equals("WeChat", StringComparison.OrdinalIgnoreCase))
                return "微信 (旧版)";
            if (variantExe.Equals("Weixin", StringComparison.OrdinalIgnoreCase))
                return "微信 (新版)";
        }

        if (!string.IsNullOrWhiteSpace(variantExe) &&
            !variantExe.Equals(variant.Title, StringComparison.OrdinalIgnoreCase) &&
            !variantExe.Equals(primaryExe, StringComparison.OrdinalIgnoreCase))
            return $"{variant.Title} ({variantExe})";

        var parent = Path.GetDirectoryName(variant.DisplayPath);
        var parentName = string.IsNullOrWhiteSpace(parent) ? null : new DirectoryInfo(parent).Name;
        if (!string.IsNullOrWhiteSpace(parentName) &&
            !parentName.Equals(variant.Title, StringComparison.OrdinalIgnoreCase))
            return $"{variant.Title} ({parentName})";

        return $"{variant.Title} (版本 {ordinal})";
    }

    private static void ScanAppsFolder(List<InstalledApplicationEntry> results)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            dynamic? folder = shell?.NameSpace("shell:AppsFolder");
            if (folder == null) return;

            var seenAppsFolderTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (dynamic item in folder.Items())
            {
                try
                {
                    string name = item.Name;
                    string path = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    if (ShouldExcludeTitle(name))
                    {
                        continue;
                    }

                    // 0. 过滤 Windows 为传统桌面快捷方式自动生成的影子 AppId，防止与已扫描的开始菜单快捷方式重复
                    if (path.StartsWith("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 1. 过滤所有网络协议与在线网址（例如 Docs API: http://npgsql...）
                    if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("://", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 2. 过滤非可执行文件/文档扩展名（如 .url, .chm, .html, .htm, .pdf, .txt 等）
                    var pathExt = Path.GetExtension(path);
                    if (IsExcludedFileExtension(pathExt) || IsExcludedFileExtension(Path.GetExtension(name)))
                    {
                        continue;
                    }

                    // 3. 处理本地物理路径或 Windows 虚拟文件夹路径
                    if (path.StartsWith("{") || path.Contains('\\') || path.Contains('/'))
                    {
                        // AppsFolder 可能直接暴露物理 exe，但该程序不一定有开始菜单快捷方式。
                        // 物理 exe 直接纳入，后续按启动命令统一去重。
                        if (File.Exists(path))
                        {
                            var physicalExt = Path.GetExtension(path);
                            if (IsSupportedEntryExtension(physicalExt) && !ShouldExcludeTarget(path))
                            {
                                results.Add(CreateEntry(
                                    title: name,
                                    launchTarget: path,
                                    displayPath: path,
                                    iconPath: path,
                                    sourcePath: path));
                            }
                            continue;
                        }

                        if (Directory.Exists(path))
                        {
                            continue;
                        }

                        // 如果该路径不存在，且不是可执行扩展名，坚决丢弃（包括文档、死链等）
                        if (string.IsNullOrWhiteSpace(pathExt) ||
                            (!string.Equals(pathExt, ".exe", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(pathExt, ".bat", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(pathExt, ".cmd", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(pathExt, ".msc", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(pathExt, ".cpl", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(pathExt, ".appref-ms", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        // 即使是 .exe 扩展名，但如果是本地绝对物理路径（包含盘符）却不存在，坚决跳过
                        if (path.Length > 2 && path[1] == ':')
                        {
                            continue;
                        }
                    }

                    // 4. 忽略以常见异常字符开头的目标
                    if (path.StartsWith('?') || path.StartsWith('#'))
                    {
                        continue;
                    }

                    string launchTarget = $"shell:AppsFolder\\{path}";
                    string iconPath = $"shell:AppsFolder\\{path}";
                    string displayPath = $"shell:AppsFolder\\{path}";

                    if (!seenAppsFolderTargets.Add(launchTarget))
                    {
                        continue;
                    }

                    var entry = CreateEntry(
                        title: name,
                        launchTarget: launchTarget,
                        displayPath: displayPath,
                        iconPath: iconPath,
                        sourcePath: path
                    );

                    results.Add(entry);
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AppsFolder scan failed: {ex.Message}");
        }
    }


    private static void ScanRegistryApplications(List<InstalledApplicationEntry> results)
    {
        var before = results.Count;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    ScanRegistryAppPaths(baseKey, results);
                    ScanRegistryUninstall(baseKey, results);
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Registry application scan skipped: hive={hive}, view={view}, error={ex.Message}");
                }
            }
        }

        HostAssets.AppendLog($"InstalledApplicationCatalog registry scan: added={results.Count - before}.");
    }

    private static void ScanRegistryAppPaths(RegistryKey baseKey, List<InstalledApplicationEntry> results)
    {
        using var root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
        if (root == null) return;

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            try
            {
                using var key = root.OpenSubKey(subKeyName);
                var executable = NormalizeRegistryExecutable(key?.GetValue(null) as string);
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) ||
                    !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                    ShouldExcludeTarget(executable))
                    continue;

                var known = KnownApplicationCatalog.Match(null, executable);
                var title = known?.DisplayName ?? TryGetProductName(executable) ?? Path.GetFileNameWithoutExtension(executable);
                if (string.IsNullOrWhiteSpace(title) || ShouldExcludeTitle(title))
                    continue;

                results.Add(CreateEntry(
                    title,
                    executable,
                    executable,
                    executable,
                    $"registry:app-paths:{subKeyName}"));
            }
            catch
            {
            }
        }
    }

    private static void ScanRegistryUninstall(RegistryKey baseKey, List<InstalledApplicationEntry> results)
    {
        using var root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        if (root == null) return;

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            try
            {
                using var key = root.OpenSubKey(subKeyName);
                var title = (key?.GetValue("DisplayName") as string)?.Trim();
                if (string.IsNullOrWhiteSpace(title) || ShouldExcludeTitle(title))
                    continue;

                var executable = NormalizeRegistryExecutable(key?.GetValue("DisplayIcon") as string);
                var installLocation = NormalizeRegistryDirectory(key?.GetValue("InstallLocation") as string);

                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) ||
                    !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) ||
                    ShouldExcludeTarget(executable))
                {
                    executable = ResolveKnownExecutableFromInstallLocation(title, installLocation);
                }

                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    continue;

                results.Add(CreateEntry(
                    title,
                    executable,
                    executable,
                    executable,
                    $"registry:uninstall:{subKeyName}",
                    workingDirectory: Path.GetDirectoryName(executable)));
            }
            catch
            {
            }
        }
    }

    private static string? ResolveKnownExecutableFromInstallLocation(string title, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
            return null;

        var known = KnownApplicationCatalog.Match(title, null);
        if (known != null)
        {
            foreach (var executableName in known.ExecutableNames)
            {
                var candidate = Path.Combine(installLocation, executableName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? NormalizeRegistryExecutable(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = Environment.ExpandEnvironmentVariables(raw.Trim());

        if (value.StartsWith('"'))
        {
            var endQuote = value.IndexOf('"', 1);
            if (endQuote > 1)
                value = value[1..endQuote];
        }
        else
        {
            var comma = value.IndexOf(',');
            if (comma > 0)
                value = value[..comma];

            var exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0)
                value = value[..(exe + 4)];
        }

        value = value.Trim().Trim('"');
        try { return Path.GetFullPath(value); }
        catch { return null; }
    }

    private static string? NormalizeRegistryDirectory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        try { return Path.GetFullPath(value); }
        catch { return null; }
    }

    private static string? TryGetProductName(string executable)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            return string.IsNullOrWhiteSpace(info.ProductName) ? null : info.ProductName.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static ApplicationScanResult EnumerateSupportedFiles(string root, bool recurse)
    {
        var files = new List<string>();
        var scannedFiles = 0;
        var skippedDirectories = 0;

        if (!recurse)
        {
            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                skippedDirectories++;
                return new ApplicationScanResult(files, scannedFiles, skippedDirectories);
            }
            catch (IOException)
            {
                return new ApplicationScanResult(files, scannedFiles, skippedDirectories);
            }

            foreach (var file in candidates)
            {
                if (!IsSupportedEntryExtension(Path.GetExtension(file)))
                {
                    continue;
                }

                scannedFiles++;
                files.Add(file);
            }

            return new ApplicationScanResult(files, scannedFiles, skippedDirectories);
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();

            IEnumerable<string> currentFiles;
            try
            {
                currentFiles = Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                skippedDirectories++;
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in currentFiles)
            {
                if (!IsSupportedEntryExtension(Path.GetExtension(file)))
                {
                    continue;
                }

                scannedFiles++;
                files.Add(file);
            }

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                skippedDirectories++;
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(childDirectory);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skippedDirectories++;
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                pendingDirectories.Push(childDirectory);
            }
        }

        return new ApplicationScanResult(files, scannedFiles, skippedDirectories);
    }

    private static InstalledApplicationEntry? TryCreateEntry(string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName) || ShouldExcludeTitle(fileName))
            {
                return null;
            }

            if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return TryCreateShortcutEntry(filePath, fileName);
            }

            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".appref-ms", StringComparison.OrdinalIgnoreCase))
            {
                return CreateEntry(
                    title: fileName,
                    launchTarget: filePath,
                    displayPath: filePath,
                    iconPath: filePath,
                    sourcePath: filePath);
            }
        }
        catch
        {
            // Ignore broken entries and continue scanning.
        }

        return null;
    }

    private static InstalledApplicationEntry? TryCreateShortcutEntry(string shortcutPath, string shortcutName)
    {
        dynamic? shell = null;
        dynamic? shortcut = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                return null;
            }

            shortcut = shell.CreateShortcut(shortcutPath);
            if (shortcut == null)
            {
                return null;
            }

            string? targetPath = null;
            try { targetPath = (string?)shortcut.TargetPath; } catch {}
            string? arguments = null;
            try { arguments = (string?)shortcut.Arguments; } catch {}
            string? workingDirectory = null;
            try { workingDirectory = (string?)shortcut.WorkingDirectory; } catch {}
            string? iconLocation = null;
            try { iconLocation = (string?)shortcut.IconLocation; } catch {}


            var normalizedTargetPath = targetPath?.Trim();
            
            if (!string.IsNullOrWhiteSpace(normalizedTargetPath))
            {
                if (Directory.Exists(normalizedTargetPath))
                {
                    return null;
                }

                if (normalizedTargetPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    normalizedTargetPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    normalizedTargetPath.Contains("://", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var targetExt = Path.GetExtension(normalizedTargetPath).ToLowerInvariant();
                if (IsExcludedFileExtension(targetExt))
                {
                    return null;
                }

                // 批处理脚本（.bat, .cmd）与非执行脚本不是传统意义上的软件，严格排除
                if (targetExt != ".exe" && targetExt != ".msc" && targetExt != ".cpl" && targetExt != ".appref-ms")
                {
                    return null;
                }

                var targetFileName = Path.GetFileName(normalizedTargetPath).ToLowerInvariant();

                // 排除 rundll32.exe 调起的组件配置窗口
                if (targetFileName == "rundll32.exe")
                {
                    return null;
                }

                // 排除以命令行外壳（cmd.exe, powershell.exe）包装的批处理脚本或环境初始化控制台（带参数的）
                if ((targetFileName == "cmd.exe" || targetFileName == "powershell.exe") &&
                    !string.IsNullOrWhiteSpace(arguments))
                {
                    return null;
                }
            }

            var launchTarget = !string.IsNullOrWhiteSpace(normalizedTargetPath) && File.Exists(normalizedTargetPath)
                ? normalizedTargetPath
                : shortcutPath;
            var displayPath = !string.IsNullOrWhiteSpace(normalizedTargetPath) ? normalizedTargetPath : shortcutPath;
            if (string.IsNullOrWhiteSpace(displayPath) || ShouldExcludeTarget(displayPath))
            {
                return null;
            }

            var iconPath = ParseIconPath(iconLocation);
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                iconPath = displayPath;
            }

            var title = shortcutName.Trim();
            if (ShouldExcludeTitle(title))
            {
                return null;
            }

            var launchArguments = string.Equals(launchTarget, shortcutPath, StringComparison.OrdinalIgnoreCase)
                ? null
                : arguments;

            return CreateEntry(title, launchTarget, displayPath, iconPath, shortcutPath, launchArguments, workingDirectory);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shortcut != null)
            {
                try
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                }
                catch
                {
                }
            }

            if (shell != null)
            {
                try
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
                catch
                {
                }
            }
        }
    }

    private static InstalledApplicationEntry CreateEntry(
        string title,
        string launchTarget,
        string displayPath,
        string? iconPath,
        string sourcePath,
        string? arguments = null,
        string? workingDirectory = null)
    {
        var knownApplication = KnownApplicationCatalog.Match(title, displayPath);
        var aliases = BuildAliases(title, displayPath, arguments).ToList();
        if (knownApplication != null)
        {
            aliases.AddRange(knownApplication.Keywords);
            aliases.AddRange(knownApplication.TitleAliases);
        }

        var extensionId = knownApplication?.ExtensionId
            ?? $"app-{ComputeStableId(title, displayPath, sourcePath)}";
        var subtitle = displayPath;

        return new InstalledApplicationEntry(
            extensionId,
            title,
            subtitle,
            launchTarget,
            displayPath,
            iconPath,
            aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            arguments?.Trim(),
            workingDirectory?.Trim());
    }

    private static IReadOnlyList<string> BuildAliases(string title, string displayPath, string? arguments)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            title.Trim()
        };

        var fileName = Path.GetFileNameWithoutExtension(displayPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            aliases.Add(fileName);
        }

        foreach (var token in title.Split([' ', '-', '_', '(', ')', '[', ']', '·', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 2)
            {
                aliases.Add(token);
            }
        }

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            foreach (var token in arguments.Split([' ', '-', '_', '=', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length >= 2)
                {
                    aliases.Add(token);
                }
            }
        }

        var lowered = $"{title} {fileName}".ToLowerInvariant();
        if (lowered.Contains("wechat") || lowered.Contains("weixin"))
        {
            aliases.Add("微信");
            aliases.Add("wechat");
            aliases.Add("weixin");
        }

        if (lowered.Contains("qq"))
        {
            aliases.Add("QQ");
            aliases.Add("腾讯QQ");
        }

        if (lowered.Contains("wecom"))
        {
            aliases.Add("企业微信");
            aliases.Add("wecom");
        }

        if (lowered.Contains("code"))
        {
            aliases.Add("VSCode");
            aliases.Add("vscode");
            aliases.Add("visual studio code");
        }

        return aliases.ToList();
    }

    private static IEnumerable<ApplicationScanRoot> GetScanRoots()
    {
        yield return new ApplicationScanRoot(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), true);
        yield return new ApplicationScanRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), true);
        yield return new ApplicationScanRoot(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false);
        yield return new ApplicationScanRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false);
    }

    private static bool IsSupportedEntryExtension(string extension)
    {
        return string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedFileExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var lowered = extension.Trim().ToLowerInvariant();
        return lowered is ".url" or ".chm" or ".html" or ".htm" or ".pdf" or ".txt"
            or ".doc" or ".docx" or ".rtf" or ".hlp" or ".msi" or ".zip" or ".rar"
            or ".7z" or ".md" or ".log" or ".xml" or ".json" or ".ini" or ".cfg";
    }

    private static readonly string[] ExcludedTitleSubstrings =
    [
        "卸载",
        "修复",
        "帮助",
        "文档",
        "手册",
        "指南",
        "说明书",
        "使用说明",
        "网站",
        "官网",
        "主页",
        "许可协议",
        "常见问题",
        "更新日志",
        "服务条款",
        "隐私政策",
        "uninstall",
        "uninst",
        "repair",
        "readme",
        "read me",
        "documentation",
        "dokumentation",
        "user manual",
        "user guide",
        "release notes",
        "releasenotes",
        "whats new",
        "what's new",
        "whatsnew",
        "faq",
        "faqs",
        "website",
        "web site",
        "homepage",
        "home page",
        "license",
        "licence",
        "changelog",
        "change log",
        "legal information",
        "legal notice",
        "support center",
        "support by e-mail",
        "bug report",
        "module docs",
        "docs api",
    ];

    private static readonly string[] ExcludedTitleWords =
    [
        "doc",
        "docs",
        "help",
        "manual",
        "manuals",
        "guide",
        "history",
        "forum",
        "tutorial",
        "tutorials",
    ];

    private static bool ShouldExcludeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var lowered = title.ToLowerInvariant();
        if (lowered.Contains("install") || lowered.Contains("update"))
        {
            return true;
        }

        foreach (var sub in ExcludedTitleSubstrings)
        {
            if (lowered.Contains(sub))
            {
                return true;
            }
        }

        foreach (var word in ExcludedTitleWords)
        {
            if (ContainsWord(lowered, word))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var startOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endOk = index + word.Length == text.Length || !char.IsLetterOrDigit(text[index + word.Length]);
            if (startOk && endOk)
            {
                return true;
            }

            index += word.Length;
        }

        return false;
    }

    private static bool ShouldExcludeTarget(string targetPath)
    {
        var lowered = targetPath.ToLowerInvariant();
        return lowered.Contains("\\uninstall") ||
               lowered.Contains("unins") ||
               lowered.Contains("\\install") ||
               lowered.Contains("\\setup");
    }

    private static string? ParseIconPath(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return null;
        }

        var trimmed = iconLocation.Trim();
        var commaIndex = trimmed.IndexOf(',');
        if (commaIndex > 0)
        {
            trimmed = trimmed[..commaIndex];
        }

        trimmed = trimmed.Trim('"');
        return File.Exists(trimmed) ? trimmed : null;
    }

    private static string ComputeStableId(string title, string displayPath, string sourcePath)
    {
        var input = $"{title}|{displayPath}|{sourcePath}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}

internal sealed record InstalledApplicationEntry(
    string ExtensionId,
    string Title,
    string Subtitle,
    string LaunchTarget,
    string DisplayPath,
    string? IconPath,
    IReadOnlyList<string> Keywords,
    string? Arguments,
    string? WorkingDirectory)
{
    public string NormalizedTitle => Title.Trim().ToLowerInvariant();

    public string NormalizedLaunchTarget => LaunchTarget.Trim().ToLowerInvariant();

    public string NormalizedDisplayPath => DisplayPath.Trim().ToLowerInvariant();

    public string NormalizedArguments => (Arguments ?? string.Empty).Trim().ToLowerInvariant();
}

internal sealed record ApplicationScanResult(
    IReadOnlyList<string> Files,
    int ScannedFiles,
    int SkippedDirectories);

internal readonly record struct ApplicationScanRoot(string Path, bool Recurse);
