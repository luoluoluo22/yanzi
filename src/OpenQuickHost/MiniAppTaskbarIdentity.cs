using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenQuickHost;

/// <summary>
/// Per-mini-app Windows taskbar identity. The hosted WebView remains in Yanzi's
/// single host process, but its taskbar button and the matching Start Menu
/// shortcut have an independent AppUserModelID, icon and relaunch argument.
/// </summary>
internal static class MiniAppTaskbarIdentity
{
    internal const string LaunchPrefix = "--miniapp=";
    private const string MessagePrefix = "__miniapp__:";
    private static readonly Guid AppModelFormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    internal static void Attach(Window window, CommandItem command)
    {
        window.SourceInitialized += (_, _) =>
        {
            if (window.ShowInTaskbar)
                Ensure(window, command);
        };
    }

    internal static void Ensure(Window window, CommandItem command)
    {
        if (!window.ShowInTaskbar || command.App is null || string.IsNullOrWhiteSpace(command.ExtensionId))
            return;
        try
        {
            Configure(window, command);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("[MiniAppTaskbar] Identity setup skipped: " +
                command.ExtensionId + " " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static string? FindLaunchToken(string[] arguments)
    {
        return arguments.FirstOrDefault(arg => arg.StartsWith(LaunchPrefix, StringComparison.OrdinalIgnoreCase))
            ?[LaunchPrefix.Length..];
    }

    internal static string ToMessage(string token) => MessagePrefix + token;

    internal static bool TryDecodeMessage(string message, out string extensionId)
    {
        extensionId = "";
        if (!message.StartsWith(MessagePrefix, StringComparison.Ordinal))
            return false;
        return TryDecodeToken(message[MessagePrefix.Length..], out extensionId);
    }

    internal static bool TryDecodeToken(string? token, out string extensionId)
    {
        extensionId = "";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 ||
            token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and '_'))
            return false;
        try
        {
            var base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length > 2048) return false;
            extensionId = new UTF8Encoding(false, true).GetString(bytes);
            return extensionId.Length is > 0 and <= 1024 &&
                   !extensionId.Any(char.IsControl);
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static void Configure(Window window, CommandItem command)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.Icon is null) return;
        var hash = HashId(command.ExtensionId);
        ConfigureHandle(handle, command, WriteStableIcon(window.Icon, hash));
    }

    /// <summary>
    /// Native C# mini-apps may open WPF windows on their own STA dispatcher
    /// instead of AppExtensionWindow. Assign the SAME mini-app AppID and icon
    /// on that window's HWND without changing the host/overlay taskbar entries.
    /// </summary>
    internal static void EnsureNativeHandle(IntPtr handle, CommandItem command)
    {
        if (handle == IntPtr.Zero || string.IsNullOrWhiteSpace(command.ExtensionId))
            return;
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null || app.Dispatcher.HasShutdownStarted)
                return;
            var iconPath = app.Dispatcher.Invoke(() =>
            {
                ImageSource? icon = command.IconSource;
                if (icon is null && command.VectorIcon is not null)
                    icon = new DrawingImage(new GeometryDrawing(
                        command.AccentBrush, null, command.VectorIcon));
                return icon is null ? null : WriteStableIcon(icon, HashId(command.ExtensionId));
            });
            if (string.IsNullOrEmpty(iconPath)) return;
            ConfigureHandle(handle, command, iconPath);
            NativeMiniAppWindowHook.ApplyWindowIcon(handle, iconPath);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("[MiniAppTaskbar] Native identity skipped: " +
                                 command.ExtensionId + " " + ex.GetType().Name);
        }
    }

    private static string HashId(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.Trim().ToLowerInvariant())))
            .ToLowerInvariant();

    private static void ConfigureHandle(IntPtr handle, CommandItem command, string iconPath)
    {
        var processPath = Environment.ProcessPath;
        if (handle == IntPtr.Zero || string.IsNullOrWhiteSpace(processPath) ||
            !Path.IsPathFullyQualified(processPath))
            return;

        var hash = HashId(command.ExtensionId);
        var appId = "Yanzi.MiniApp." + hash[..24] +
                    (HostRuntimeProfile.IsDevelopment ? ".Dev" : "");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(command.ExtensionId))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var argument = LaunchPrefix + encoded;
        var title = string.IsNullOrWhiteSpace(command.Title) ? "小程序" : command.Title.Trim();
        var filename = "燕子 · " + SanitizeFileName(title, 35) + " [" + hash[..8] + "]" +
                       (HostRuntimeProfile.IsDevelopment ? " (开发)" : "") + ".lnk";

        // A matching persistent Start Menu shortcut is required for pinning.
        EnsureLaunchShortcut(processPath, argument, iconPath, appId, title, filename, hash);

        var iid = PropertyStoreId;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(handle, ref iid, out var store));
        if (store is null) throw new InvalidOperationException("Window property store unavailable.");
        try
        {
            SetString(store, 5, appId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.ReleaseComObject(store); }
        HostAssets.AppendLog("[MiniAppTaskbar] configured " + command.ExtensionId +
                             " AppID=" + appId + " hwnd=" + handle);
    }

    private static string WriteStableIcon(ImageSource? icon, string hash)
    {
        if (icon is null) throw new InvalidOperationException("Mini-app has no icon to pin.");

        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawImage(icon, new Rect(0, 0, 64, 64));
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);
        var bytes = png.ToArray();

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)64);
            writer.Write((byte)64);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(bytes.Length);
            writer.Write(22);
            writer.Write(bytes);
        }
        var directory = HostAssets.ResolveDataDirectoryPath("MiniAppTaskbarIcons");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, hash + ".ico");
        var icoBytes = ico.ToArray();
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(icoBytes))
            File.WriteAllBytes(path, icoBytes);
        return path;
    }

    private static string SanitizeFileName(string value, int length)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Where(c => !invalid.Contains(c) && !char.IsControl(c)).Take(length).ToArray())
            .Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "小程序" : result;
    }

    private static void EnsureLaunchShortcut(
        string executable, string arguments, string icon, string appId,
        string title, string filename, string hash)
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(startMenu)) return;
        var shortcut = Path.Combine(startMenu, filename);
        if (File.Exists(shortcut))
        {
            var owner = ReadShortcutAppId(shortcut);
            var target = ReadShortcutTarget(shortcut);
            if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, appId, StringComparison.Ordinal))
                throw new IOException("Start Menu shortcut is owned by a different app.");
            if (!string.IsNullOrEmpty(target) &&
                !string.Equals(target, executable, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(owner, appId, StringComparison.Ordinal))
                throw new IOException("Start Menu shortcut belongs to a different executable.");
        }
        if (!File.Exists(shortcut) || !IsCorrect(shortcut, executable, arguments, icon, appId))
            WriteShortcut(shortcut, executable, arguments, icon, appId, title);

        // Fix only existing pins with THIS mini-app AppID. Never touch other
        // mini-apps, host Yanzi, unrelated software, or links to other EXEs.
        var pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        if (!Directory.Exists(pins)) return;
        foreach (var path in Directory.EnumerateFiles(pins, "*.lnk"))
        {
            try
            {
                if (!string.Equals(ReadShortcutAppId(path), appId, StringComparison.Ordinal) ||
                    !string.Equals(ReadShortcutTarget(path), executable, StringComparison.OrdinalIgnoreCase) ||
                    IsCorrect(path, executable, arguments, icon, appId))
                    continue;
                var backupDir = HostAssets.ResolveDataDirectoryPath("Backups");
                Directory.CreateDirectory(backupDir);
                var backup = Path.Combine(backupDir, "miniapp-pin-" + hash + "-" + Path.GetFileName(path) + ".bak");
                if (!File.Exists(backup)) File.Copy(path, backup);
                WriteShortcut(path, executable, arguments, icon, appId, title);
                HostAssets.AppendLog("[MiniAppTaskbar] repaired pin: " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog("[MiniAppTaskbar] pin repair skipped: " +
                                     Path.GetFileName(path) + " " + ex.GetType().Name);
            }
        }
    }

    private static bool IsCorrect(string path, string executable, string arguments, string icon, string appId)
    {
        return string.Equals(ReadShortcutAppId(path), appId, StringComparison.Ordinal) &&
               string.Equals(ReadShortcutTarget(path), executable, StringComparison.OrdinalIgnoreCase) &&
               WithShortcut(path, sc =>
                   string.Equals((string)sc.Arguments, arguments, StringComparison.Ordinal) &&
                   string.Equals((string)sc.IconLocation, icon + ",0", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadShortcutTarget(string path) =>
        WithShortcut(path, sc => (string?)sc.TargetPath);

    private static T WithShortcut<T>(string path, Func<dynamic, T> action)
    {
        dynamic? shell = null;
        dynamic? link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            link = shell!.CreateShortcut(path);
            return action(link);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void WriteShortcut(
        string path, string executable, string arguments, string icon, string appId, string title)
    {
        WithShortcut(path, sc =>
        {
            sc.TargetPath = executable;
            sc.Arguments = arguments;
            sc.IconLocation = icon + ",0";
            sc.Description = "燕子 · " + title;
            sc.WorkingDirectory = Path.GetDirectoryName(executable)!;
            sc.Save();
            return true;
        });
        var iid = PropertyStoreId;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(
            path, IntPtr.Zero, 2, ref iid, out var store));
        if (store is null) throw new InvalidOperationException("No shortcut property store.");
        try
        {
            SetString(store, 5, appId);
            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.ReleaseComObject(store); }
        if (!IsCorrect(path, executable, arguments, icon, appId))
            throw new InvalidOperationException("Mini-app launch shortcut verification failed.");
        SHChangeNotify(0x2000, 0x0005, path, IntPtr.Zero);
    }

    private static string? ReadShortcutAppId(string path)
    {
        dynamic? shell = null;
        dynamic? folder = null;
        dynamic? item = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = shell!.NameSpace(Path.GetDirectoryName(path)!);
            item = folder?.ParseName(Path.GetFileName(path));
            return item?.ExtendedProperty("System.AppUserModel.ID")?.ToString();
        }
        finally
        {
            if (item is not null) Marshal.FinalReleaseComObject(item);
            if (folder is not null) Marshal.FinalReleaseComObject(folder);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void SetString(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey(AppModelFormatId, id);
        var value = new PropVariant { VarType = 31, Pointer = Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { PropVariantClear(ref value); }
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path, IntPtr bindContext, uint flags, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string path, IntPtr unused);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int PropVariantClear(ref PropVariant value);

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

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
