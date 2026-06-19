using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OpenQuickHost;

public static class LegacyCleanupService
{
    /// <summary>
    /// 在新版安装过程中调用，检测并直接静默卸载旧版，不弹窗询问。
    /// </summary>
    public static void SilentUninstallOldVersion()
    {
        try
        {
            var legacyUninstallPath = FindLegacyUninstallPath();
            if (string.IsNullOrEmpty(legacyUninstallPath))
            {
                return;
            }

            HostAssets.AppendLog("Found legacy version during installation. Proceeding to silent uninstall.");
            RunLegacyUninstaller(legacyUninstallPath);
            
            // 标记已处理，防止后续误判
            var settings = AppSettingsStore.Load();
            settings.LegacyCleanupDismissed = true;
            AppSettingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to execute silent legacy cleanup: {ex.Message}");
        }
    }

    /// <summary>
    /// 每次启动时调用，检测是否存在旧版 Inno Setup 安装的燕子，并引导用户清理。
    /// 如果用户之前选择了"否"，则不再重复提示（通过 AppSettings.LegacyCleanupDismissed 标记）。
    /// </summary>
    public static void CheckAndPromptUninstallOldVersion()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            if (settings.LegacyCleanupDismissed)
            {
                return;
            }

            var legacyUninstallPath = FindLegacyUninstallPath();
            if (string.IsNullOrEmpty(legacyUninstallPath))
            {
                return;
            }

            var result = System.Windows.MessageBox.Show(
                "检测到您的电脑上安装了旧版燕子（位于 C:\\Program Files\\Yanzi）。\n\n"
                + "为了避免桌面快捷方式冲突以及旧版自启导致的体验异常，建议卸载旧版本。\n\n"
                + "（新版已迁移至用户目录，支持无感后台更新）\n\n"
                + "是否允许燕子现在为您卸载旧版本？",
                "清理旧版燕子",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                RunLegacyUninstaller(legacyUninstallPath);
            }
            else
            {
                // 用户拒绝，记住选择，不再提示
                settings.LegacyCleanupDismissed = true;
                AppSettingsStore.Save(settings);
                HostAssets.AppendLog("User declined to uninstall legacy version. Will not prompt again.");
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to execute legacy cleanup check: {ex.Message}");
        }
    }

    private static string? FindLegacyUninstallPath()
    {
        // 1. 先检查默认路径
        const string defaultPath = @"C:\Program Files\Yanzi\unins000.exe";
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        // 2. 从注册表读取（兼容不同安装路径）
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Yanzi_is1")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Yanzi_is1");

            if (key == null)
            {
                return null;
            }

            var uninstallString = key.GetValue("QuietUninstallString") as string
                                  ?? key.GetValue("UninstallString") as string;
            if (string.IsNullOrWhiteSpace(uninstallString))
            {
                return null;
            }

            // UninstallString 格式类似 "C:\Program Files\Yanzi\unins000.exe"
            var path = uninstallString.Trim('"', ' ');
            // 如果有参数附加在后面，只取可执行文件路径
            var spaceIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (spaceIndex > 0)
            {
                path = path[..(spaceIndex + 4)];
            }

            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RunLegacyUninstaller(string uninstallerPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = uninstallerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
                Verb = "runas" // 请求管理员权限（旧版安装在 Program Files 下）
            };
            var process = Process.Start(startInfo);
            HostAssets.AppendLog("Started legacy version uninstall process.");

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    process?.WaitForExit();
                    HostAssets.AppendLog($"Legacy uninstall process exited with code: {process?.ExitCode}");
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Legacy uninstall wait failed: {ex.Message}");
                }
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 用户在 UAC 弹窗点了"否"，拒绝了管理员权限
            HostAssets.AppendLog("User cancelled UAC elevation for legacy uninstall.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to start legacy uninstaller: {ex.Message}");
        }
    }
}
