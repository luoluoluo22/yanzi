using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public static class ClipboardService
{
    private const int RetryCount = 12;
    private const int RetryDelayMilliseconds = 80;
    private static readonly TimeSpan StaTimeout = TimeSpan.FromSeconds(5);

    public static void SetText(string? text)
    {
        if (TrySetText(text ?? string.Empty, out var clipboardError))
        {
            return;
        }

        if (TrySetTextViaClipExe(text ?? string.Empty, out var fallbackError))
        {
            return;
        }

        throw new InvalidOperationException($"写入剪贴板失败：{fallbackError ?? clipboardError}");
    }

    public static void SetDataObject(object dataObject, bool copy)
    {
        if (TrySetDataObject(dataObject, copy, out var error))
        {
            return;
        }

        throw new InvalidOperationException($"写入剪贴板失败：{error}");
    }

    public static string? GetText()
    {
        if (TryGetText(out var text, out var error))
        {
            return text;
        }

        throw new InvalidOperationException($"读取剪贴板失败：{error}");
    }

    public static bool TrySetText(string text, out string? error)
    {
        // The UI thread must not enter the WinForms retry loop. That loop sleeps, and
        // SetDataObject(copy: true) also flushes every clipboard format before it returns.
        if (IsUiThread() && TrySetPlainTextFast(text, out error))
        {
            return true;
        }

        return RunStaClipboardAction(() => Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText), out error);
    }

    public static bool TrySetPlainTextFast(string text, out string? error)
    {
        var started = Stopwatch.GetTimestamp();
        error = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (TrySetPlainTextOnce(text, out error))
            {
                LogIfSlow(started, $"plain text write chars={text.Length}, attempts={attempt + 1}");
                return true;
            }

            if (attempt < 2)
            {
                Thread.Sleep(5);
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HostAssets.AppendLog($"ClipboardService plain text write failed: elapsedMs={elapsed:F1}, chars={text.Length}, error={error}");
        return false;
    }

    public static bool TrySetDataObject(object dataObject, bool copy, out string? error)
    {
        // One attempt per retry. The WinForms overload that retries internally was wrapped
        // in another 12-step sleep, so a busy clipboard froze the caller for several seconds.
        return RunStaClipboardAction(() => Forms.Clipboard.SetDataObject(dataObject, copy), out error);
    }

    public static bool TryGetText(out string? text, out string? error)
    {
        var textResult = string.Empty;
        var ok = RunStaClipboardAction(
            () => textResult = Forms.Clipboard.ContainsText() ? Forms.Clipboard.GetText() : string.Empty,
            out error);
        text = ok ? textResult : null;
        return ok;
    }

    private static bool RunStaClipboardAction(Action action, out string? error)
    {
        error = null;
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                ExecuteWithRetry(action);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        Exception? threadError = null;
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                ExecuteWithRetry(action);
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
            finally
            {
                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!done.Wait(StaTimeout))
        {
            error = "STA 剪贴板线程超时。";
            return false;
        }

        if (threadError == null)
        {
            return true;
        }

        error = threadError.Message;
        return false;
    }

    private static void ExecuteWithRetry(Action action)
    {
        var onUiThread = IsUiThread();
        var attempts = onUiThread ? 2 : RetryCount;
        var delay = onUiThread ? 10 : RetryDelayMilliseconds;
        var started = Stopwatch.GetTimestamp();
        Exception? lastError = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                action();
                LogIfSlow(started, $"retry write attempts={attempt + 1}, uiThread={onUiThread}");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(delay);
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HostAssets.AppendLog($"ClipboardService retry write failed: elapsedMs={elapsed:F1}, uiThread={onUiThread}, error={lastError?.Message}");
        throw lastError ?? new InvalidOperationException("未知剪贴板错误。");
    }

    private static bool IsUiThread()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher != null && dispatcher.CheckAccess();
    }

    private static void LogIfSlow(long started, string detail)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= 30)
        {
            HostAssets.AppendLog($"ClipboardService slow: elapsedMs={elapsed:F1}, {detail}");
        }
    }

    private static bool TrySetPlainTextOnce(string text, out string? error)
    {
        error = null;
        if (!OpenClipboard(IntPtr.Zero))
        {
            error = "剪贴板正被占用。";
            return false;
        }

        IntPtr handle = IntPtr.Zero;
        var handedOff = false;
        try
        {
            if (!EmptyClipboard())
            {
                error = "无法清空剪贴板。";
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text);
            handle = GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)(bytes.Length + 2));
            if (handle == IntPtr.Zero)
            {
                error = "无法分配剪贴板内存。";
                return false;
            }

            var destination = GlobalLock(handle);
            if (destination == IntPtr.Zero)
            {
                error = "无法锁定剪贴板内存。";
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
            {
                error = "写入剪贴板失败。";
                return false;
            }

            handedOff = true;
            return true;
        }
        finally
        {
            if (!handedOff && handle != IntPtr.Zero)
            {
                GlobalFree(handle);
            }

            CloseClipboard();
        }
    }

    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    private static bool TrySetTextViaClipExe(string text, out string? error)
    {
        error = null;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = Encoding.Unicode,
                CreateNoWindow = true
            };

            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();

            if (!process.WaitForExit(5000))
            {
                error = "clip.exe 超时。";
                return false;
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            error = $"clip.exe 返回了退出码 {process.ExitCode}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
