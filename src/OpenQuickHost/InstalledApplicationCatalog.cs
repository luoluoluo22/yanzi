using System.IO;

namespace OpenQuickHost;

internal static class InstalledApplicationCatalog
{
    public static IReadOnlyList<InstalledApplicationEntry> Load()
    {
        var results = new List<InstalledApplicationEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                if (entry == null)
                {
                    continue;
                }

                var dedupeKey = $"{entry.NormalizedTitle}|{entry.NormalizedLaunchTarget}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }
                
                seenTitles.Add(entry.NormalizedTitle);
                results.Add(entry);
            }

            totalScannedFiles += scanResult.ScannedFiles;
            totalSkippedDirectories += scanResult.SkippedDirectories;
            HostAssets.AppendLog(
                $"InstalledApplicationCatalog root scanned: path={root}, recurse={scanRoot.Recurse}, files={scanResult.ScannedFiles}, skippedDirectories={scanResult.SkippedDirectories}, acceptedSoFar={results.Count}.");
        }

        ScanAppsFolder(results, seen, seenTitles);

        HostAssets.AppendLog(
            $"InstalledApplicationCatalog load summary: roots={GetScanRoots().Count()}, scannedFiles={totalScannedFiles}, skippedDirectories={totalSkippedDirectories}, accepted={results.Count}.");

        return results
            .OrderBy(static entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ScanAppsFolder(List<InstalledApplicationEntry> results, HashSet<string> seen, HashSet<string> seenTitles)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            dynamic? folder = shell?.NameSpace("shell:AppsFolder");
            if (folder == null) return;

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

                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        continue;
                    }

                    string launchTarget = $"shell:AppsFolder\\{path}";
                    string iconPath = $"shell:AppsFolder\\{path}";
                    string displayPath = $"shell:AppsFolder\\{path}";

                    var entry = CreateEntry(
                        title: name,
                        launchTarget: launchTarget,
                        displayPath: displayPath,
                        iconPath: iconPath,
                        sourcePath: path
                    );

                    var dedupeKey = $"{entry.NormalizedTitle}|{entry.NormalizedLaunchTarget}";
                    if (seen.Add(dedupeKey))
                    {
                        if (seenTitles.Add(entry.NormalizedTitle))
                        {
                            results.Add(entry);
                        }
                    }
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

                var targetExt = Path.GetExtension(normalizedTargetPath).ToLowerInvariant();
                if (targetExt != ".exe" && targetExt != ".bat" && targetExt != ".cmd" && 
                    targetExt != ".msc" && targetExt != ".cpl" && targetExt != ".appref-ms")
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
        var aliases = BuildAliases(title, displayPath, arguments);
        var extensionId = $"app-{ComputeStableId(title, displayPath, sourcePath)}";
        var subtitle = string.IsNullOrWhiteSpace(arguments)
            ? displayPath
            : $"{displayPath} {arguments}".Trim();

        return new InstalledApplicationEntry(
            extensionId,
            title,
            subtitle,
            launchTarget,
            displayPath,
            iconPath,
            aliases,
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

    private static bool ShouldExcludeTitle(string title)
    {
        var lowered = title.ToLowerInvariant();
        return lowered.Contains("卸载") ||
               lowered.Contains("uninstall") ||
               lowered.Contains("install") ||
               lowered.Contains("repair") ||
               lowered.Contains("update") ||
               lowered.Contains("帮助") ||
               lowered.Contains("help") ||
               lowered.Contains("readme");
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
}

internal sealed record ApplicationScanResult(
    IReadOnlyList<string> Files,
    int ScannedFiles,
    int SkippedDirectories);

internal readonly record struct ApplicationScanRoot(string Path, bool Recurse);
