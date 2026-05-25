using System.Diagnostics;
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
        return RunStaClipboardAction(() => Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText), out error);
    }

    public static bool TrySetDataObject(object dataObject, bool copy, out string? error)
    {
        return RunStaClipboardAction(
            () => Forms.Clipboard.SetDataObject(dataObject, copy, RetryCount, RetryDelayMilliseconds),
            out error);
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
        Exception? lastError = null;
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }

        throw lastError ?? new InvalidOperationException("未知剪贴板错误。");
    }

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
