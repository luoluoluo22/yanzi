using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

/// <summary>Windows titlebar treatment only. No change to the host or other extensions.</summary>
internal static class GalleryWindowAppearance
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int valueSize);

    public static void Apply(Window window, YanziTheme theme)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = theme == YanziTheme.Dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
