using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public static class SelectionCaptureService
{
    public static async Task<string> CaptureSelectedInputAsync(CancellationToken cancellationToken = default)
    {
        var before = GetClipboardSequenceNumber();
        HostAssets.AppendLog($"Selection capture: sending Ctrl+C, clipboard sequence before={before}, foreground={DescribeForegroundWindow()}.");
        var sent = SendCopyShortcut();
        HostAssets.AppendLog($"Selection capture: SendInput sent={sent}/4, inputSize={Marshal.SizeOf<INPUT>()}, lastError={Marshal.GetLastWin32Error()}.");

        var changed = false;
        var after = before;
        for (var i = 0; i < 8; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(60, cancellationToken);
            after = GetClipboardSequenceNumber();
            if (after != before)
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            HostAssets.AppendLog($"Selection capture: clipboard did not change after Ctrl+C, sequence={after}. Returning empty input.");
            return string.Empty;
        }

        HostAssets.AppendLog($"Selection capture: clipboard changed, sequence after={after}.");
        return ReadClipboardInput();
    }

    private static string ReadClipboardInput()
    {
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            var formats = data?.GetFormats() ?? [];
            HostAssets.AppendLog($"Selection capture: clipboard formats={string.Join(", ", formats)}.");

            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText().Trim();
                HostAssets.AppendLog($"Selection capture: recognized text input, length={text.Length}.");
                return text;
            }

            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = System.Windows.Clipboard.GetFileDropList()
                    .Cast<string>()
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                if (files.Length > 0)
                {
                    HostAssets.AppendLog($"Selection capture: recognized file input, count={files.Length}.");
                    return string.Join(Environment.NewLine, files);
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Selection capture: clipboard read failed: {ex.Message}");
            // Clipboard can be temporarily locked by the source app; treat that as empty input.
        }

        HostAssets.AppendLog("Selection capture: clipboard changed but no supported text/file format was found.");
        return string.Empty;
    }

    private static uint SendCopyShortcut()
    {
        var inputs = new[]
        {
            KeyInput(Forms.Keys.ControlKey, keyUp: false),
            KeyInput(Forms.Keys.C, keyUp: false),
            KeyInput(Forms.Keys.C, keyUp: true),
            KeyInput(Forms.Keys.ControlKey, keyUp: true)
        };

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static string DescribeForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return "hwnd=0x0";
        }

        var titleBuilder = new StringBuilder(256);
        _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return $"hwnd=0x{hwnd.ToInt64():X}, pid={processId}, title=\"{titleBuilder}\"";
    }

    private static INPUT KeyInput(Forms.Keys key, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)key,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        // SendInput validates cbSize against the full Win32 INPUT layout.
        // Include the larger union arms so Marshal.SizeOf<INPUT>() matches user32.dll on x64.
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
