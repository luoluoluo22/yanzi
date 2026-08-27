using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace OpenQuickHost;

public static class DesktopShellHelper
{
    /// <summary>
    /// 在真实 Windows 桌面上直接高亮选中指定文件 (无外部文件夹弹窗)
    /// </summary>
    public static bool SelectDesktopFile(string fileNameKeyword = "示例文件")
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;

            dynamic? shellApp = Activator.CreateInstance(shellType);
            dynamic? windows = shellApp?.Windows();
            if (windows == null) return false;

            // 0x08 = SWC_DESKTOP 代表当前 Windows 桌面视图
            for (int w = 0; w < windows.Count; w++)
            {
                try
                {
                    dynamic? win = windows.Item(w);
                    dynamic? doc = win?.Document;
                    dynamic? folder = doc?.Folder;
                    dynamic? items = folder?.Items();
                    if (items == null) continue;

                    for (int i = 0; i < items.Count; i++)
                    {
                        dynamic? item = items.Item(i);
                        string? name = item?.Name;
                        if (!string.IsNullOrEmpty(name) && name.Contains(fileNameKeyword, StringComparison.OrdinalIgnoreCase))
                        {
                            // 1 (SVSI_SELECT) | 4 (SVSI_DESELECTOTHERS) | 8 (SVSI_ENSUREVISIBLE) | 16 (SVSI_FOCUSED)
                            doc.SelectItem(item, 1 | 4 | 8 | 16);
                            HostAssets.AppendLog($"[DesktopShellHelper] Selected '{name}' on desktop via Shell COM.");
                            return true;
                        }
                    }
                }
                catch
                {
                    // Ignore single window query failure
                }
            }

            // 兜底直接尝试通过 Background/Progman
            try
            {
                dynamic? desktopWin = windows.Item(8);
                dynamic? doc = desktopWin?.Document;
                dynamic? folder = doc?.Folder;
                dynamic? items = folder?.Items();
                if (items != null)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        dynamic? item = items.Item(i);
                        string? name = item?.Name;
                        if (!string.IsNullOrEmpty(name) && name.Contains(fileNameKeyword, StringComparison.OrdinalIgnoreCase))
                        {
                            doc.SelectItem(item, 1 | 4 | 8 | 16);
                            HostAssets.AppendLog($"[DesktopShellHelper] Selected '{name}' on desktop (SWC_DESKTOP).");
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore fallback failure
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[DesktopShellHelper] SelectDesktopFile failed: {ex.Message}");
        }

        return false;
    }
}
