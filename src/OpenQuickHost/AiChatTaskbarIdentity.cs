using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OpenQuickHost;

/// <summary>
/// A WPF Window.Icon controls its title-bar glyph, but the Windows taskbar can
/// still group/pin it under the process-level Velopack AppUserModelID.
/// Give only AI chat windows an explicit window-level identity and an .ico
/// relaunch resource. The host's taskbar/tray identity remains unchanged.
/// </summary>
internal static class AiChatTaskbarIdentity
{
    internal const string RelaunchArgument = "--ai-chat";
    private const string OfficialAppId = "Yanzi.Desktop.AiChat";
    private const string DevelopmentAppId = "Yanzi.Desktop.AiChat.Dev";
    private static readonly Guid AppModelFormatId =
        new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Guid PropertyStoreInterfaceId =
        new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    internal static void Attach(Window window)
    {
        // SourceInitialized runs after HWND creation, before the taskbar button
        // becomes visible. Avoid a transient grouping under velopack.Yanzi.
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                Configure(window);
            }
            catch (Exception ex)
            {
                // Losing the separate taskbar identity must never crash a chat.
                HostAssets.AppendLog("[AiChatTaskbar] Window identity setup failed: " + ex);
            }
        };
    }

    private static void Configure(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("AI chat HWND not ready.");

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath))
            throw new InvalidOperationException("Cannot resolve executable for AI chat relaunch.");

        var iconPath = EnsurePersistentIcon(window);
        var appId = HostRuntimeProfile.IsDevelopment ? DevelopmentAppId : OfficialAppId;
        var iid = PropertyStoreInterfaceId;
        var hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        Marshal.ThrowExceptionForHR(hr);
        if (store is null)
            throw new InvalidOperationException("Windows did not return a window property store.");

        try
        {
            // Set relaunch properties BEFORE the ID: Explorer updates its taskbar
            // representation when PKEY_AppUserModel_ID is assigned.
            SetString(store, 2, "\"" + processPath + "\" " + RelaunchArgument);
            SetString(store, 3, iconPath + ",0");
            SetString(store, 5, appId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }

        HostAssets.AppendLog("[AiChatTaskbar] Configured window AppID=" + appId +
                             " hwnd=" + hwnd + " icon=" + iconPath);
    }

    private static string EnsurePersistentIcon(Window window)
    {
        if (window.Icon is not BitmapSource source)
            throw new InvalidOperationException("AI chat must provide a bitmap window icon.");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var png = new MemoryStream();
        encoder.Save(png);
        var pngBytes = png.ToArray();

        // An ICO container may contain PNG data for modern Windows. Its stable
        // absolute path is required by System.AppUserModel.RelaunchIconResource;
        // an in-memory WPF RenderTargetBitmap cannot serve as a relaunch icon.
        using var icoStream = new MemoryStream();
        using (var writer = new BinaryWriter(icoStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); // ICONDIR reserved
            writer.Write((ushort)1); // type = icon
            writer.Write((ushort)1); // image count
            writer.Write((byte)(source.PixelWidth == 256 ? 0 : source.PixelWidth));
            writer.Write((byte)(source.PixelHeight == 256 ? 0 : source.PixelHeight));
            writer.Write((byte)0); // palette size
            writer.Write((byte)0);
            writer.Write((ushort)1); // color planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(pngBytes.Length);
            writer.Write(22); // icon directory plus one entry
            writer.Write(pngBytes);
        }

        var path = HostAssets.ResolveDataFilePath("ai-chat-taskbar.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var iconBytes = icoStream.ToArray();
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(iconBytes))
            File.WriteAllBytes(path, iconBytes);
        return path;
    }

    private static void SetString(IPropertyStore store, uint propertyId, string text)
    {
        var key = new PropertyKey(AppModelFormatId, propertyId);
        var value = new PropVariant
        {
            VarType = 31, // VT_LPWSTR
            Pointer = Marshal.StringToCoTaskMemUni(text)
        };
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int PropVariantClear(ref PropVariant variant);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
