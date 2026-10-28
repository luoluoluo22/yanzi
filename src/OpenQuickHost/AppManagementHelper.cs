using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace OpenQuickHost;

internal static class AppManagementHelper
{
    private static readonly string[] RunRegistryKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
    ];

    private static readonly (RegistryKey Root, string SubKey)[] UninstallRegistryRoots =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
    ];

    private static readonly string[] CommonUninstallerNames =
    [
        "unins000.exe",
        "unins001.exe",
        "uninstall.exe",
        "uninst.exe",
        "uninstaller.exe"
    ];

    public record UninstallInfo(string Command, string? Arguments, string DisplayName, bool IsFromRegistry);

    /// <summary>
    /// 解析快捷方式（.lnk）获取实际目标文件路径；若不是快捷方式则返回去除引号后的原路径。
    /// </summary>
    public static string ResolveActualExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var cleanPath = path.Trim('\"', ' ');
        if (!cleanPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(cleanPath))
        {
            return cleanPath;
        }

        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    shortcut = shell.CreateShortcut(cleanPath);
                    if (shortcut != null)
                    {
                        string? target = (string?)shortcut.TargetPath;
                        if (!string.IsNullOrWhiteSpace(target) && File.Exists(target.Trim('\"', ' ')))
                        {
                            return target.Trim('\"', ' ');
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[AppManagementHelper] ResolveActualExecutablePath failed for '{cleanPath}': {ex.Message}");
        }
        finally
        {
            if (shortcut != null)
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); } catch { }
            }
            if (shell != null)
            {
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }

        return cleanPath;
    }

    public sealed class StartupSnapshot
    {
        public HashSet<string> ExePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExeNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Titles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsStartup(string? pathOrLnk, string? appTitle)
        {
            if (!string.IsNullOrWhiteSpace(pathOrLnk))
            {
                var exePath = ResolveActualExecutablePath(pathOrLnk);
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    if (ExePaths.Contains(exePath)) return true;
                    var exeName = Path.GetFileName(exePath);
                    if (!string.IsNullOrEmpty(exeName) && ExeNames.Contains(exeName)) return true;
                    var exeBaseName = Path.GetFileNameWithoutExtension(exePath);
                    if (!string.IsNullOrEmpty(exeBaseName) && Titles.Contains(exeBaseName)) return true;
                }
            }
            if (!string.IsNullOrWhiteSpace(appTitle) && Titles.Contains(appTitle.Trim()))
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 一次性扫描系统所有生效的自启动项并生成快照，以便在列表加载时进行 O(1) 的高效匹配。
    /// </summary>
    public static StartupSnapshot GetStartupSnapshot()
    {
        var snapshot = new StartupSnapshot();
        try
        {
            ScanRegistryRunForSnapshot(Registry.CurrentUser, snapshot);
            ScanRegistryRunForSnapshot(Registry.LocalMachine, snapshot);

            ScanStartupFolderForSnapshot(Environment.GetFolderPath(Environment.SpecialFolder.Startup), snapshot);
            ScanStartupFolderForSnapshot(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), snapshot);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[AppManagementHelper] GetStartupSnapshot failed: {ex.Message}");
        }
        return snapshot;
    }

    private static void ScanRegistryRunForSnapshot(RegistryKey rootKey, StartupSnapshot snapshot)
    {
        foreach (var subKeyPath in RunRegistryKeys)
        {
            using var runKey = rootKey.OpenSubKey(subKeyPath, writable: false);
            if (runKey == null) continue;

            foreach (var valueName in runKey.GetValueNames())
            {
                if (!IsRunValueApproved(valueName)) continue;

                snapshot.Titles.Add(valueName);
                var valObj = runKey.GetValue(valueName);
                if (valObj is string valStr && !string.IsNullOrWhiteSpace(valStr))
                {
                    var exePath = ExtractExePathFromCommandLine(valStr);
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        var actual = ResolveActualExecutablePath(exePath);
                        snapshot.ExePaths.Add(actual);
                        var fileName = Path.GetFileName(actual);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            snapshot.ExeNames.Add(fileName);
                        }
                    }
                }
            }
        }
    }

    private static void ScanStartupFolderForSnapshot(string folderPath, StartupSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsStartupFolderItemApproved(fileName))
                    {
                        var target = ResolveActualExecutablePath(file);
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            snapshot.ExePaths.Add(target);
                            snapshot.ExeNames.Add(Path.GetFileName(target));
                            snapshot.Titles.Add(Path.GetFileNameWithoutExtension(file));
                        }
                    }
                }
                else if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.ExePaths.Add(file);
                    snapshot.ExeNames.Add(fileName);
                    snapshot.Titles.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
        }
        catch
        {
        }
    }

    private static string ExtractExePathFromCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;
        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith("\""))
        {
            var endQuote = trimmed.IndexOf('\"', 1);
            if (endQuote > 1)
            {
                return trimmed.Substring(1, endQuote - 1).Trim();
            }
        }
        var exeIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx > 0)
        {
            return trimmed.Substring(0, exeIdx + 4).Trim('\"', ' ');
        }
        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
        {
            return trimmed.Substring(0, spaceIdx).Trim('\"', ' ');
        }
        return trimmed.Trim('\"', ' ');
    }

    /// <summary>
    /// 检查指定应用是否已启用开机自启动（检查注册表 Run 键与开始菜单 Startup 文件夹，并验证 StartupApproved 状态）。
    /// </summary>
    public static bool IsStartupEnabled(string pathOrLnk, string appTitle)
    {
        try
        {
            var exePath = ResolveActualExecutablePath(pathOrLnk);
            var exeName = Path.GetFileName(exePath);
            var exeBaseName = Path.GetFileNameWithoutExtension(exePath);

            // 1. 检查 HKCU Run 注册表
            if (CheckRegistryRunKey(Registry.CurrentUser, exePath, exeName, exeBaseName, appTitle, out bool isApprovedRunHkcu))
            {
                return isApprovedRunHkcu;
            }

            // 2. 检查 HKLM Run 注册表
            if (CheckRegistryRunKey(Registry.LocalMachine, exePath, exeName, exeBaseName, appTitle, out bool isApprovedRunHklm))
            {
                return isApprovedRunHklm;
            }

            // 3. 检查当前用户与公共 Startup 文件夹
            if (CheckStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), exePath, exeName))
            {
                return true;
            }

            if (CheckStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), exePath, exeName))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[AppManagementHelper] IsStartupEnabled check failed: {ex.Message}");
        }

        return false;
    }

    private static bool CheckRegistryRunKey(
        RegistryKey rootKey, 
        string exePath, 
        string exeName, 
        string exeBaseName, 
        string appTitle, 
        out bool isApproved)
    {
        isApproved = false;
        foreach (var subKeyPath in RunRegistryKeys)
        {
            using var runKey = rootKey.OpenSubKey(subKeyPath, writable: false);
            if (runKey == null) continue;

            foreach (var valueName in runKey.GetValueNames())
            {
                var valObj = runKey.GetValue(valueName);
                if (valObj is not string valStr) continue;

                var isMatch = false;
                if (!string.IsNullOrWhiteSpace(exePath) && valStr.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrWhiteSpace(exeName) && valStr.Contains(exeName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(valueName, appTitle, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(valueName, exeBaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                    }
                }
                else if (string.Equals(valueName, appTitle, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(valueName, exeBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }

                if (isMatch)
                {
                    isApproved = IsRunValueApproved(valueName);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsRunValueApproved(string valueName)
    {
        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", 
                writable: false);
            if (approvedKey != null)
            {
                if (approvedKey.GetValue(valueName) is byte[] data && data.Length > 0)
                {
                    // 奇数表示已在任务管理器中被禁用（0x01, 0x03），偶数表示启用（0x02, 0x00）
                    return data[0] % 2 == 0;
                }
            }
        }
        catch
        {
        }
        return true;
    }

    private static bool CheckStartupFolder(string folderPath, string exePath, string exeName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var fileName = Path.GetFileName(file);
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var target = ResolveActualExecutablePath(file);
                    if (string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsStartupFolderItemApproved(fileName))
                        {
                            return true;
                        }
                    }
                }
                else if (string.Equals(file, exePath, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(fileName, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsStartupFolderItemApproved(string fileName)
    {
        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", 
                writable: false);
            if (approvedKey != null)
            {
                if (approvedKey.GetValue(fileName) is byte[] data && data.Length > 0)
                {
                    return data[0] % 2 == 0;
                }
            }
        }
        catch
        {
        }
        return true;
    }

    /// <summary>
    /// 设置或取消应用的开机自启动。
    /// </summary>
    public static (bool Success, string Message) SetStartupEnabled(string pathOrLnk, string appTitle, bool enable)
    {
        try
        {
            var exePath = ResolveActualExecutablePath(pathOrLnk);
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return (false, "找不到可执行文件目标路径，无法设置自启动。");
            }

            var exeBaseName = Path.GetFileNameWithoutExtension(exePath);
            var entryName = !string.IsNullOrWhiteSpace(appTitle) ? appTitle.Trim() : exeBaseName;

            if (enable)
            {
                // 1. 写入 HKCU Run 键
                using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (runKey == null)
                {
                    return (false, "无法打开注册表启动项。");
                }

                runKey.SetValue(entryName, $"\"{exePath}\"");

                // 2. 重置 StartupApproved\Run 状态为启用（0x02）
                try
                {
                    using var approvedKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                    if (approvedKey != null)
                    {
                        var enabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(entryName, enabledBytes, RegistryValueKind.Binary);
                    }
                }
                catch
                {
                }

                HostAssets.AppendLog($"[AppManagementHelper] Enabled startup for '{entryName}' -> '{exePath}'");
                return (true, $"已为“{entryName}”开启开机自启动");
            }
            else
            {
                // 1. 删除 HKCU Run 注册表中的匹配项
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                {
                    if (runKey != null)
                    {
                        foreach (var name in runKey.GetValueNames())
                        {
                            var val = runKey.GetValue(name) as string;
                            if (string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(name, exeBaseName, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrWhiteSpace(val) && val.Contains(exePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                runKey.DeleteValue(name, throwOnMissingValue: false);
                            }
                        }
                    }
                }

                // 2. 从 Startup 文件夹删除快捷方式
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    foreach (var file in Directory.EnumerateFiles(startupFolder))
                    {
                        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            var target = ResolveActualExecutablePath(file);
                            if (string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(file); } catch { }
                            }
                        }
                    }
                }

                // 3. 在 StartupApproved 标记为禁用（0x03），兜底系统级自启项
                try
                {
                    using var approvedKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                    if (approvedKey != null)
                    {
                        var disabledBytes = new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(entryName, disabledBytes, RegistryValueKind.Binary);
                        approvedKey.SetValue(exeBaseName, disabledBytes, RegistryValueKind.Binary);
                    }
                }
                catch
                {
                }

                HostAssets.AppendLog($"[AppManagementHelper] Disabled startup for '{entryName}' -> '{exePath}'");
                return (true, $"已为“{entryName}”关闭开机自启动");
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[AppManagementHelper] SetStartupEnabled failed: {ex}");
            return (false, $"设置开机自启动失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 寻找应用的卸载信息（从注册表或应用所在目录）。
    /// </summary>
    public static UninstallInfo? FindUninstallInfo(string pathOrLnk, string appTitle)
    {
        var exePath = ResolveActualExecutablePath(pathOrLnk);
        var appDir = !string.IsNullOrWhiteSpace(exePath) && (File.Exists(exePath) || Directory.Exists(exePath))
            ? (Directory.Exists(exePath) ? exePath : Path.GetDirectoryName(exePath))
            : string.Empty;
        var exeBaseName = Path.GetFileNameWithoutExtension(exePath);

        // 1. 注册表卸载搜索
        var registryResult = SearchRegistryUninstall(exePath, appDir, appTitle, exeBaseName);
        if (registryResult != null)
        {
            return registryResult;
        }

        // 2. 程序目录与父级目录搜索独立卸载程序
        if (!string.IsNullOrWhiteSpace(appDir) && Directory.Exists(appDir))
        {
            var dirResult = SearchDirectoryUninstaller(appDir, appTitle);
            if (dirResult != null)
            {
                return dirResult;
            }

            var parentDir = Path.GetDirectoryName(appDir);
            if (!string.IsNullOrWhiteSpace(parentDir) && Directory.Exists(parentDir))
            {
                var parentResult = SearchDirectoryUninstaller(parentDir, appTitle);
                if (parentResult != null)
                {
                    return parentResult;
                }
            }
        }

        return null;
    }

    private static UninstallInfo? SearchRegistryUninstall(string exePath, string? appDir, string appTitle, string exeBaseName)
    {
        UninstallInfo? bestMatch = null;
        var highestScore = 0;

        foreach (var (root, subKeyPath) in UninstallRegistryRoots)
        {
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key == null) continue;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var itemKey = key.OpenSubKey(subKeyName, writable: false);
                if (itemKey == null) continue;

                var uninstallString = itemKey.GetValue("UninstallString") as string
                                      ?? itemKey.GetValue("QuietUninstallString") as string;
                if (string.IsNullOrWhiteSpace(uninstallString))
                {
                    continue;
                }

                var displayName = itemKey.GetValue("DisplayName") as string ?? subKeyName;
                var installLocation = itemKey.GetValue("InstallLocation") as string;
                var displayIcon = itemKey.GetValue("DisplayIcon") as string;

                var score = 0;

                // 1. 如果 InstallLocation 匹配应用目录
                if (!string.IsNullOrWhiteSpace(installLocation) && !string.IsNullOrWhiteSpace(appDir))
                {
                    var cleanInstallLocation = installLocation.Trim('\"', ' ');
                    if (appDir.StartsWith(cleanInstallLocation, StringComparison.OrdinalIgnoreCase) ||
                        cleanInstallLocation.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 50;
                    }
                }

                // 2. 如果 DisplayIcon 包含 exe 完整路径或所在目录
                if (!string.IsNullOrWhiteSpace(displayIcon))
                {
                    var cleanDisplayIcon = displayIcon.Trim('\"', ' ');
                    if (!string.IsNullOrWhiteSpace(exePath) && cleanDisplayIcon.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 40;
                    }
                    else if (!string.IsNullOrWhiteSpace(appDir) && cleanDisplayIcon.Contains(appDir, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 30;
                    }
                }

                // 3. 如果 UninstallString 路径位于 appDir 下
                if (!string.IsNullOrWhiteSpace(appDir) && uninstallString.Contains(appDir, StringComparison.OrdinalIgnoreCase))
                {
                    score += 40;
                }

                // 4. 名称相似度
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    if (string.Equals(displayName, appTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 35;
                    }
                    else if (displayName.Contains(appTitle, StringComparison.OrdinalIgnoreCase) ||
                             appTitle.Contains(displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 20;
                    }

                    if (!string.IsNullOrWhiteSpace(exeBaseName) &&
                        displayName.Contains(exeBaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 15;
                    }
                }

                if (score > highestScore && score >= 35)
                {
                    highestScore = score;
                    var (cmd, args) = SplitCommandAndArguments(uninstallString);
                    bestMatch = new UninstallInfo(cmd, args, displayName, IsFromRegistry: true);
                }
            }
        }

        return bestMatch;
    }

    private static UninstallInfo? SearchDirectoryUninstaller(string dir, string appTitle)
    {
        try
        {
            foreach (var uninstallerName in CommonUninstallerNames)
            {
                var candidate = Path.Combine(dir, uninstallerName);
                if (File.Exists(candidate))
                {
                    return new UninstallInfo(candidate, null, appTitle, IsFromRegistry: false);
                }
            }

            var exeFiles = Directory.EnumerateFiles(dir, "*unins*.exe", SearchOption.TopDirectoryOnly);
            foreach (var file in exeFiles)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Contains("update", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return new UninstallInfo(file, null, appTitle, IsFromRegistry: false);
            }
        }
        catch
        {
        }

        return null;
    }

    public static (string Command, string? Arguments) SplitCommandAndArguments(string rawString)
    {
        var trimmed = rawString.Trim();
        if (trimmed.StartsWith('\"'))
        {
            var nextQuote = trimmed.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                var command = trimmed.Substring(1, nextQuote - 1).Trim();
                var args = trimmed.Substring(nextQuote + 1).Trim();
                return (command, string.IsNullOrWhiteSpace(args) ? null : args);
            }
        }

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex > 0)
        {
            var possibleCmd = trimmed.Substring(0, spaceIndex);
            if (File.Exists(possibleCmd) || possibleCmd.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return (possibleCmd, trimmed.Substring(spaceIndex + 1).Trim());
            }
        }

        return (trimmed, null);
    }

    /// <summary>
    /// 触发应用卸载交互与执行。
    /// </summary>
    public static void UninstallApp(Window owner, string pathOrLnk, string appTitle)
    {
        var uninstallInfo = FindUninstallInfo(pathOrLnk, appTitle);
        var targetDisplayName = !string.IsNullOrWhiteSpace(uninstallInfo?.DisplayName) 
            ? uninstallInfo.DisplayName 
            : appTitle;

        if (uninstallInfo != null && !string.IsNullOrWhiteSpace(uninstallInfo.Command))
        {
            var confirm = System.Windows.MessageBox.Show(
                owner,
                $"确定要卸载应用“{targetDisplayName}”吗？\n\n卸载程序：\n{uninstallInfo.Command}{(string.IsNullOrWhiteSpace(uninstallInfo.Arguments) ? "" : $" {uninstallInfo.Arguments}")}",
                "卸载应用",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = uninstallInfo.Command,
                    Arguments = uninstallInfo.Arguments ?? string.Empty,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
                HostAssets.AppendLog($"[AppManagementHelper] Started uninstaller for '{targetDisplayName}': {uninstallInfo.Command} {uninstallInfo.Arguments}");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[AppManagementHelper] Failed to start uninstaller: {ex}");
                var openSettings = System.Windows.MessageBox.Show(
                    owner,
                    $"启动卸载程序失败：{ex.Message}\n\n是否打开系统“已安装的应用”设置页面？",
                    "卸载应用",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (openSettings == MessageBoxResult.Yes)
                {
                    OpenAppsFeaturesSettings();
                }
            }
        }
        else
        {
            var confirm = System.Windows.MessageBox.Show(
                owner,
                $"未检测到“{targetDisplayName}”的独立卸载程序（可能为免安装绿色软件或系统内置应用）。\n\n是否打开系统“已安装的应用”设置页面进行卸载？",
                "卸载应用",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (confirm == MessageBoxResult.Yes)
            {
                OpenAppsFeaturesSettings();
            }
        }
    }

    public static void OpenAppsFeaturesSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "ms-settings:appsfeatures",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[AppManagementHelper] Failed to open ms-settings:appsfeatures: {ex}");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "control.exe",
                    Arguments = "appwiz.cpl",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }
}
