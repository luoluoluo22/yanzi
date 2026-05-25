using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace OpenQuickHost;

public static class NativeFileIconService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetSystemCommandIcon(string? openTarget, string? extensionId = null)
    {
        var resolvedPath = ResolveSystemIconPath(openTarget, extensionId);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        return GetIcon(resolvedPath, isFolder: false);
    }

    public static ImageSource? GetIcon(string path, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cacheKey = BuildCacheKey(path, isFolder);
        return IconCache.GetOrAdd(cacheKey, _ => LoadSmallIcon(path, isFolder));
    }

    private static string BuildCacheKey(string path, bool isFolder)
    {
        if (isFolder)
        {
            return "__folder__";
        }

        var extension = Path.GetExtension(path);
        if (UsesPathSpecificIcon(extension))
        {
            return path;
        }

        return string.IsNullOrWhiteSpace(extension) ? path : extension;
    }

    private static bool UsesPathSpecificIcon(string? extension)
    {
        return extension is not null &&
               (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".url", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ico", StringComparison.OrdinalIgnoreCase));
    }

    private static ImageSource? LoadSmallIcon(string path, bool isFolder)
    {
        var attributes = isFolder ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon;
        var targetPath = path;
        if (!isFolder && IsShortcutPath(path) && TryResolveShortcutIconTarget(path, out var shortcutIconTarget))
        {
            targetPath = shortcutIconTarget;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            flags |= ShgfiUseFileAttributes;
            if (isFolder)
            {
                targetPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            }
        }

        var shinfo = new Shfileinfo();
        var targetAttributes = Directory.Exists(targetPath) ? FileAttributeDirectory : FileAttributeNormal;
        var handle = SHGetFileInfo(targetPath, targetAttributes, ref shinfo, (uint)Marshal.SizeOf<Shfileinfo>(), flags);
        if (handle == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                shinfo.hIcon,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private static bool IsShortcutPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveShortcutIconTarget(string shortcutPath, out string targetPath)
    {
        targetPath = string.Empty;
        dynamic? shell = null;
        dynamic? shortcut = null;

        try
        {
            if (!File.Exists(shortcutPath))
            {
                return false;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            shortcut = shell?.CreateShortcut(shortcutPath);
            if (shortcut == null)
            {
                return false;
            }

            var iconLocation = ((string?)shortcut.IconLocation)?.Trim();
            var iconPath = ParseShortcutIconPath(iconLocation);
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                targetPath = iconPath;
                return true;
            }

            var resolvedTarget = ((string?)shortcut.TargetPath)?.Trim();
            if (!string.IsNullOrWhiteSpace(resolvedTarget) &&
                (File.Exists(resolvedTarget) || Directory.Exists(resolvedTarget)))
            {
                targetPath = resolvedTarget;
                return true;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shortcut != null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
                catch
                {
                }
            }

            if (shell != null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(shell);
                }
                catch
                {
                }
            }
        }

        return false;
    }

    private static string? ParseShortcutIconPath(string? iconLocation)
    {
        if (string.IsNullOrWhiteSpace(iconLocation))
        {
            return null;
        }

        var value = iconLocation.Trim().Trim('"');
        var commaIndex = value.LastIndexOf(',');
        if (commaIndex > 0 && int.TryParse(value[(commaIndex + 1)..], out _))
        {
            value = value[..commaIndex].Trim().Trim('"');
        }

        return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
    }

    private static string? ResolveSystemIconPath(string? openTarget, string? extensionId)
    {
        if (string.IsNullOrWhiteSpace(openTarget))
        {
            return null;
        }

        var mappedPath = ResolveMappedSystemIconPath(extensionId);
        if (!string.IsNullOrWhiteSpace(mappedPath))
        {
            return mappedPath;
        }

        if (openTarget.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveSettingsAppPath(extensionId);
        }

        if (TryResolveExecutablePath(openTarget, out var executablePath))
        {
            return executablePath;
        }

        return null;
    }

    private static string? ResolveMappedSystemIconPath(string? extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return null;
        }

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var immersiveControlPanelPath = Path.Combine(windowsDirectory, "ImmersiveControlPanel", "SystemSettings.exe");

        return extensionId switch
        {
            "system-environment-variables" or "system-advanced-properties" => ResolveExistingPath(
                Path.Combine(systemDirectory, "SystemPropertiesAdvanced.exe"),
                Path.Combine(systemDirectory, "sysdm.cpl")),
            "system-classic-sound" or "system-playback-devices" or "system-recording-devices" or
            "system-sounds-tab" or "system-communications-audio" => ResolveExistingPath(
                Path.Combine(systemDirectory, "mmsys.cpl")),
            "system-device-manager" => ResolveExistingPath(
                Path.Combine(systemDirectory, "devmgmt.msc"),
                Path.Combine(systemDirectory, "mmc.exe")),
            "system-services" or "system-disk-management" or "system-event-viewer" => ResolveExistingPath(
                Path.Combine(systemDirectory, "mmc.exe")),
            "system-registry-editor" => ResolveExistingPath(
                Path.Combine(systemDirectory, "regedit.exe")),
            "system-settings-sound" or "system-settings-apps-volume" or "system-settings-microphone-privacy" or
            "system-settings-webcam-privacy" or "system-settings-printers" or "system-settings-default-apps" or
            "system-settings-power" or "system-settings-storage" or "system-settings-personalization" or
            "system-settings-windows-update" or "system-settings-default-output" or
            "system-settings-default-input" or "system-settings-sound-devices" or
            "system-settings-accessibility-audio" => ResolveExistingPath(immersiveControlPanelPath),
            _ => null
        };
    }

    private static string? ResolveSettingsAppPath(string? extensionId)
    {
        // Settings sub-pages do not expose distinct icon files directly.
        // Use the owning system app icon and vary by area where Windows has a clear dedicated executable.
        if (string.Equals(extensionId, "system-settings-network", StringComparison.OrdinalIgnoreCase))
        {
            var ncpaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ncpa.cpl");
            if (File.Exists(ncpaPath))
            {
                return ncpaPath;
            }
        }

        var immersiveControlPanelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "ImmersiveControlPanel",
            "SystemSettings.exe");
        if (File.Exists(immersiveControlPanelPath))
        {
            return immersiveControlPanelPath;
        }

        return null;
    }

    private static bool TryResolveExecutablePath(string openTarget, out string executablePath)
    {
        executablePath = string.Empty;

        if (Path.IsPathRooted(openTarget) && File.Exists(openTarget))
        {
            executablePath = Path.GetFullPath(openTarget);
            return true;
        }

        var candidate = openTarget.Trim();
        var quoteIndex = candidate.IndexOf('"');
        if (quoteIndex >= 0)
        {
            candidate = candidate.Replace("\"", string.Empty, StringComparison.Ordinal);
        }

        var firstSpaceIndex = candidate.IndexOf(' ');
        if (firstSpaceIndex > 0)
        {
            candidate = candidate[..firstSpaceIndex];
        }

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var searchCandidates = new[]
        {
            Path.Combine(systemDirectory, candidate),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), candidate)
        };

        foreach (var path in searchCandidates)
        {
            if (File.Exists(path))
            {
                executablePath = path;
                return true;
            }
        }

        return false;
    }

    private static string? ResolveExistingPath(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref Shfileinfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Shfileinfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
