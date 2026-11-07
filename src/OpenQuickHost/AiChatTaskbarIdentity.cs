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
    private static string RelaunchArguments => HostRuntimeProfile.IsDevelopment
        ? "--dev " + RelaunchArgument : RelaunchArgument;
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

        // A window-level AppID alone is not enough to produce a persistent pin.
        // Explorer must find a real shortcut with the same AppID, icon and --ai-chat
        // argument; otherwise the pin falls back to Yanzi.exe without arguments.
        try { EnsureLaunchShortcut(appId, processPath, iconPath); }
        catch (Exception ex)
        {
            HostAssets.AppendLog("[AiChatTaskbar] Shortcut registration failed: " + ex);
        }

        ConfigureWindowProperties(hwnd, processPath, iconPath, appId, window.Title);

        HostAssets.AppendLog("[AiChatTaskbar] Configured window AppID=" + appId +
                             " hwnd=" + hwnd + " icon=" + iconPath);
    }

    internal static void ConfigureWindowProperties(
        IntPtr hwnd, string processPath, string iconPath, string appId, string title)
    {
        var iid = PropertyStoreInterfaceId;
        var hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        Marshal.ThrowExceptionForHR(hr);
        if (store is null)
            throw new InvalidOperationException("Windows did not return a window property store.");

        try
        {
            // Explorer can create a new pin directly from this HWND instead of
            // selecting the Start Menu link. Supply the complete relaunch tuple
            // before the AppID: command and display name must be set together.
            SetString(store, 2, "\"" + processPath + "\" " + RelaunchArguments);
            SetString(store, 3, iconPath + ",0");
            SetString(store, 4, title);
            SetString(store, 5, appId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
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

    private static void EnsureLaunchShortcut(string appId, string processPath, string iconPath)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programs))
            throw new InvalidOperationException("Windows Start Menu programs folder is unavailable.");

        var shortcutPath = Path.Combine(programs, HostRuntimeProfile.IsDevelopment
            ? "燕子 AI 对话 (开发).lnk" : "燕子 AI 对话.lnk");
        Directory.CreateDirectory(programs);
        if (File.Exists(shortcutPath))
        {
            var existingId = ReadShortcutAppId(shortcutPath);
            var existingTarget = ReadShortcutTarget(shortcutPath);
            if (!string.IsNullOrEmpty(existingId) &&
                !string.Equals(existingId, appId, StringComparison.Ordinal))
                throw new IOException("The AI Start Menu shortcut belongs to a different app.");
            if (!string.IsNullOrEmpty(existingTarget) &&
                !string.Equals(existingTarget, processPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(existingId, appId, StringComparison.Ordinal))
                throw new IOException("Refusing to overwrite an unrelated Start Menu shortcut.");
        }
        if (!File.Exists(shortcutPath) ||
            !string.Equals(ReadShortcutAppId(shortcutPath), appId, StringComparison.Ordinal) ||
            !IsShortcutCorrect(shortcutPath, processPath, iconPath))
            UpdateShortcut(shortcutPath, processPath, iconPath, appId);

        // Repair only existing AI pins. Never rewrite the ordinary Yanzi host
        // pin (velopack.Yanzi), other programs, or shortcuts targeting a
        // different executable. Save one backup before modifying a user's pin.
        var pinnedDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        if (!Directory.Exists(pinnedDirectory))
            return;

        foreach (var pin in Directory.EnumerateFiles(pinnedDirectory, "*.lnk"))
        {
            try
            {
                if (!string.Equals(ReadShortcutAppId(pin), appId, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(ReadShortcutTarget(pin), processPath,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsShortcutCorrect(pin, processPath, iconPath))
                    continue;

                var backupDir = HostAssets.ResolveDataDirectoryPath("Backups");
                Directory.CreateDirectory(backupDir);
                var backup = Path.Combine(backupDir,
                    "ai-chat-taskbar-pin-" + Path.GetFileName(pin) + ".bak");
                if (!File.Exists(backup))
                    File.Copy(pin, backup);

                UpdateShortcut(pin, processPath, iconPath, appId);
                HostAssets.AppendLog("[AiChatTaskbar] Repaired pinned shortcut: " +
                                     Path.GetFileName(pin));
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog("[AiChatTaskbar] Pinned shortcut repair skipped: " +
                    Path.GetFileName(pin) + " " + ex.GetType().Name);
            }
        }
    }

    private static bool IsShortcutCorrect(string path, string processPath, string iconPath)
    {
        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = shell!.CreateShortcut(path);
            return string.Equals((string)link.TargetPath, processPath,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((string)link.Arguments, RelaunchArguments,
                       StringComparison.Ordinal) &&
                   string.Equals((string)link.IconLocation, iconPath + ",0",
                       StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string? ReadShortcutTarget(string path)
    {
        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = shell!.CreateShortcut(path);
            return (string)link.TargetPath;
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void UpdateShortcut(
        string path, string processPath, string iconPath, string appId)
    {
        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = shell!.CreateShortcut(path);
            link.TargetPath = processPath;
            link.Arguments = RelaunchArguments;
            link.IconLocation = iconPath + ",0";
            link.Description = "燕子 · AI 对话";
            link.WorkingDirectory = Path.GetDirectoryName(processPath)!;
            link.Save();
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }

        // WScript.Shell cannot set AppUserModelID. The Shell property store is
        // mandatory; without it Windows pins this under velopack.Yanzi.
        var iid = PropertyStoreInterfaceId;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(
            path, IntPtr.Zero, 2 /* GPS_READWRITE */, ref iid, out var store));
        if (store is null)
            throw new InvalidOperationException("No shortcut property store.");
        try
        {
            SetString(store, 5, appId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }

        if (!string.Equals(ReadShortcutAppId(path), appId, StringComparison.Ordinal) ||
            !IsShortcutCorrect(path, processPath, iconPath))
            throw new InvalidOperationException("AI shortcut verification failed.");

        // Explorer caches icons and pinned links, so notify it about updates
        // rather than restarting explorer.exe or changing unrelated pins.
        SHChangeNotify(0x00002000 /* SHCNE_UPDATEITEM */,
            0x0005 /* SHCNF_PATHW */, path, IntPtr.Zero);
    }

    private static string? ReadShortcutAppId(string path)
    {
        // The generic SHGetPropertyStoreFromParsingName reader can expose VT_EMPTY
        // for a newly saved .lnk even though its AppID was persisted correctly.
        // Explorer uses Shell.Application's ExtendedProperty; use that same
        // effective property view for verification and selective pin repair.
        dynamic? shell = null;
        dynamic? folder = null;
        dynamic? item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = shell!.NameSpace(Path.GetDirectoryName(path)!);
            if (folder is null) return null;
            item = folder.ParseName(Path.GetFileName(path));
            if (item is null) return null;
            object? appId = item.ExtendedProperty("System.AppUserModel.ID");
            return appId?.ToString();
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path, IntPtr bindContext, uint flags, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint eventId, uint flags, string item1, IntPtr item2);

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
