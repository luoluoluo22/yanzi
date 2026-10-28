using System.Runtime.InteropServices;
using System.Text;

namespace Yanzi.Capture;

/// <summary>
/// Writes Unicode text directly using the Win32 clipboard ownership protocol.
/// Unlike OLE's SetDataObject(copy:true), a successful SetClipboardData does not
/// subsequently throw during OleFlushClipboard after already setting the text.
/// </summary>
internal static class CaptureTextClipboard
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfUnicodeText = 13;
    private static readonly IntPtr HwndMessage = new(-3);

    public static async Task<ClipboardWriteResult> WriteAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new ClipboardWriteResult(false, 0, "empty");

        // One owner window per operation; ownership and native call stay on the
        // screenshot extension's existing STA dispatcher, including after awaits.
        var hwnd = CreateWindowExW(0, "STATIC", "Yanzi.Capture.Clipboard",
            0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            return new ClipboardWriteResult(false, 0, "create_owner:" + Marshal.GetLastWin32Error());

        try
        {
            var bytes = Encoding.Unicode.GetBytes(text + "\0");
            var lastError = "unknown";
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                if (OpenClipboard(hwnd))
                {
                    try
                    {
                        if (!EmptyClipboard())
                        {
                            lastError = "empty:" + Marshal.GetLastWin32Error();
                        }
                        else
                        {
                            var buffer = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
                            if (buffer == IntPtr.Zero)
                            {
                                lastError = "alloc:" + Marshal.GetLastWin32Error();
                            }
                            else
                            {
                                try
                                {
                                    var ptr = GlobalLock(buffer);
                                    if (ptr == IntPtr.Zero)
                                    {
                                        lastError = "lock:" + Marshal.GetLastWin32Error();
                                    }
                                    else
                                    {
                                        try { Marshal.Copy(bytes, 0, ptr, bytes.Length); }
                                        finally { GlobalUnlock(buffer); }
                                        var given = SetClipboardData(CfUnicodeText, buffer);
                                        if (given != IntPtr.Zero)
                                        {
                                            // Clipboard now owns the allocation.
                                            buffer = IntPtr.Zero;
                                            return new ClipboardWriteResult(true, attempt, "native-success");
                                        }
                                        lastError = "set:" + Marshal.GetLastWin32Error();
                                    }
                                }
                                finally
                                {
                                    if (buffer != IntPtr.Zero)
                                        GlobalFree(buffer);
                                }
                            }
                        }
                    }
                    finally { CloseClipboard(); }
                }
                else
                {
                    lastError = "open:" + Marshal.GetLastWin32Error();
                }

                if (attempt < 10)
                    await Task.Delay(35 + 15 * attempt);
            }
            return new ClipboardWriteResult(false, 10, lastError);
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className,
        string name, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);
}

internal sealed record ClipboardWriteResult(bool Success, int Attempts, string Detail);
