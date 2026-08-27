using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Yanzi.Platform.Mac;

public static class MacPermissionHelper
{
    public static bool IsAccessibilityGranted()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try
        {
            return AXIsProcessTrusted();
        }
        catch
        {
            return false;
        }
    }

    public static bool IsInputMonitoringGranted()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try
        {
            return CGPreflightListenEventAccess();
        }
        catch
        {
            return false;
        }
    }

    public static bool RequestAccessibilityPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try
        {
            var key = CreateNSString("AXTrustedCheckOptionPrompt");
            var value = objc_msgSend_bool(objc_getClass("NSNumber"), sel_registerName("numberWithBool:"), 1);
            var options = objc_msgSend_objectKey(
                objc_getClass("NSDictionary"),
                sel_registerName("dictionaryWithObject:forKey:"),
                value,
                key);

            return options != IntPtr.Zero
                ? AXIsProcessTrustedWithOptions(options)
                : AXIsProcessTrusted();
        }
        catch
        {
            return AXIsProcessTrusted();
        }
    }

    public static bool RequestInputMonitoringPermission()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        try
        {
            return CGRequestListenEventAccess();
        }
        catch
        {
            return false;
        }
    }

    public static void OpenAccessibilitySettings()
    {
        OpenUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");
    }

    public static void OpenInputMonitoringSettings()
    {
        OpenUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent");
    }

    public static void OpenSecuritySettings()
    {
        OpenUrl("x-apple.systempreferences:com.apple.preference.security");
    }

    public static void RevealAppInFinder()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var current = new DirectoryInfo(baseDir);
            while (current != null && !current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                current = current.Parent;
            }

            var appPath = current?.FullName ?? Path.Combine(baseDir, "Yanzi.app");
            if (Directory.Exists(appPath) || File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-R \"{appPath}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{baseDir}\"",
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to reveal app in Finder: {ex.Message}");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{url}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open URL {url}: {ex.Message}");
        }
    }

    private static IntPtr CreateNSString(string value)
    {
        return objc_msgSend_string(
            objc_getClass("NSString"),
            sel_registerName("stringWithUTF8String:"),
            value);
    }

    #region P/Invoke

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern bool CGPreflightListenEventAccess();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern bool CGRequestListenEventAccess();

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_string(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.LPStr)] string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_bool(IntPtr receiver, IntPtr selector, byte value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_objectKey(IntPtr receiver, IntPtr selector, IntPtr obj, IntPtr key);

    #endregion
}
