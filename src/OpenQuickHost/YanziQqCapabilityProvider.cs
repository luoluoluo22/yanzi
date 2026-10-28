using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public static class YanziQqCapabilityProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private const int SwRestore = 9;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private sealed record QqWindow(IntPtr Handle, int ProcessId, NativeRect Rect)
    {
        public bool LooksLikeMain => Rect.Width >= 800 && Rect.Height >= 600;
    }

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "qq.status",
            Description = "查询 QQ NT 是否运行、是否处于主界面，以及当前窗口尺寸状态",
            Permissions = ["application.read"],
            Category = "qq",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "qq.self.sendText",
            Description = "确保 QQ 已打开并进入主界面，通过 QQ 自带“我的手机”跨端会话发送文字；发送前校验输入内容，发送后用输入框清空与聊天区变化双重确认。",
            Permissions = ["application.run", "ui.automation"],
            Category = "qq",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "text":{"type":"string","minLength":1},
                "loginWaitSeconds":{"type":"integer","minimum":5,"maximum":120,"default":60}
              },
              "required":["text"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = SendSelfTextAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var window = FindBestWindow();
        return Task.FromResult<object?>(new
        {
            running = Process.GetProcessesByName("QQ").Length > 0,
            state = window == null ? "not_visible" : window.LooksLikeMain ? "main" : "login_or_unknown",
            loggedIn = window?.LooksLikeMain == true,
            processId = window?.ProcessId,
            window = window == null ? null : new { width = window.Rect.Width, height = window.Rect.Height }
        });
    }

    private static async Task<object?> SendSelfTextAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var text = input.GetProperty("text").GetString() ?? string.Empty;
        if (text.Length > 4000)
            throw new ArgumentException("QQ 文字消息不能超过 4000 个字符。");
        var loginWaitSeconds = input.TryGetProperty("loginWaitSeconds", out var wait) ? wait.GetInt32() : 60;

        await Gate.WaitAsync();
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            await EnsureVisibleAsync();
            var window = FindBestWindow() ?? throw new InvalidOperationException("QQ 已启动，但未找到可见窗口。");

            if (!window.LooksLikeMain)
            {
                var capture = CaptureVerificationWindow(window);
                try
                {
                    await YanziCapabilityRegistry.InvokeAsync(
                        "chat.send",
                        new
                        {
                            text = "QQ 当前不在主界面，可能需要登录或验证。请按截图提示完成操作，燕子会继续等待。",
                            filePath = capture,
                            kind = "photo",
                            waitForAck = false
                        },
                        YanziCapabilityCaller.LocalAgent);
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog("QQ verification screenshot delivery failed: " + ex.Message);
                }

                var deadline = DateTimeOffset.UtcNow.AddSeconds(loginWaitSeconds);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(500);
                    window = FindBestWindow();
                    if (window?.LooksLikeMain == true) break;
                }

                if (window?.LooksLikeMain != true)
                {
                    return new
                    {
                        status = "verification_required",
                        sent = false,
                        confirmed = false,
                        needsUserAction = true,
                        verificationCapture = capture
                    };
                }
            }

            await OpenMyPhoneConversationAsync(window);
            window = FindBestWindow() ?? window;
            FocusWindow(window.Handle);
            ClickRelative(window, 0.70, 0.88);
            await Task.Delay(160);

            PasteTextPreservingClipboard(text);
            await Task.Delay(180);

            var preparedText = ReadFocusedTextPreservingClipboard();
            var inputPrepared = string.Equals(NormalizeText(preparedText), NormalizeText(text), StringComparison.Ordinal);
            if (!inputPrepared)
                throw new InvalidOperationException("QQ 输入框校验失败，未发送消息，避免误发。");

            using var before = CaptureWindow(window);

            RunSta(() =>
            {
                Forms.SendKeys.SendWait("{END}");
                Forms.SendKeys.SendWait("{ENTER}");
            });

            await Task.Delay(650);
            var confirmation = await ConfirmSentAsync(window, before, text);
            var sendShortcut = "Enter";

            if (!confirmation.Confirmed &&
                confirmation.VisualDifferenceRatio < 0.002 &&
                string.Equals(NormalizeText(confirmation.RemainingInput), NormalizeText(text), StringComparison.Ordinal))
            {
                RunSta(() =>
                {
                    Forms.SendKeys.SendWait("{END}");
                    Forms.SendKeys.SendWait("^{ENTER}");
                });
                sendShortcut = "Ctrl+Enter";
                await Task.Delay(650);
                confirmation = await ConfirmSentAsync(window, before, text);
            }

            return new
            {
                status = confirmation.Confirmed ? "sent" : "uncertain",
                sent = confirmation.Confirmed,
                confirmed = confirmation.Confirmed,
                recipient = "我的手机",
                sendShortcut,
                confirmation = new
                {
                    inputPrepared,
                    inputCleared = confirmation.InputCleared,
                    chatChanged = confirmation.VisualDifferenceRatio >= 0.002,
                    visualDifferenceRatio = Math.Round(confirmation.VisualDifferenceRatio, 5),
                    method = "input_cleared_and_chat_region_changed"
                },
                startedAtUtc = startedAt,
                completedAtUtc = DateTimeOffset.UtcNow,
                message = confirmation.Confirmed
                    ? "QQ 消息已在本地 UI 中确认发送。"
                    : "发送结果不确定；为避免重复消息，燕子没有自动重发。"
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task EnsureVisibleAsync()
    {
        var existing = FindBestWindow();
        if (existing != null)
        {
            FocusWindow(existing.Handle);
            return;
        }

        await YanziCapabilityRegistry.InvokeAsync(
            "app.open",
            new { app = "QQ" },
            YanziCapabilityCaller.LocalAgent);

        for (var i = 0; i < 40; i++)
        {
            var window = FindBestWindow();
            if (window != null)
            {
                FocusWindow(window.Handle);
                return;
            }
            await Task.Delay(150);
        }

        throw new InvalidOperationException("已尝试启动 QQ，但窗口未出现。");
    }

    private static async Task OpenMyPhoneConversationAsync(QqWindow window)
    {
        FocusWindow(window.Handle);

        // QQ NT 当前搜索框位于主窗口左上角。使用窗口相对位置，避免绝对屏幕坐标。
        ClickRelative(window, 0.115, 0.055);
        await Task.Delay(140);

        RunSta(() =>
        {
            Forms.SendKeys.SendWait("^a");
            Forms.SendKeys.SendWait("{BACKSPACE}");
        });
        await Task.Delay(80);

        PasteTextPreservingClipboard("我的手机");
        await Task.Delay(550);

        // 精确搜索“我的手机”时顶部是 QQ 自带“我的手机”工具入口。
        ClickRelative(window, 0.18, 0.17);
        await Task.Delay(600);

        ClickRelative(window, 0.70, 0.88);
        await Task.Delay(120);
    }

    private sealed record SendConfirmation(bool Confirmed, bool InputCleared, double VisualDifferenceRatio, string RemainingInput);

    private static async Task<SendConfirmation> ConfirmSentAsync(QqWindow window, Bitmap before, string expectedText)
    {
        double difference = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var after = CaptureWindow(window);
            difference = Math.Max(difference, ComputeChatDifference(before, after));
            if (difference >= 0.002) break;
            await Task.Delay(400);
        }

        FocusWindow(window.Handle);
        ClickRelative(window, 0.70, 0.88);
        await Task.Delay(100);
        var remaining = ReadFocusedTextPreservingClipboard();
        var inputCleared = string.IsNullOrEmpty(NormalizeText(remaining));
        return new SendConfirmation(inputCleared && difference >= 0.002, inputCleared, difference, remaining);
    }

    private static QqWindow? FindBestWindow()
    {
        var processIds = Process.GetProcessesByName("QQ").Select(process => process.Id).ToHashSet();
        if (processIds.Count == 0) return null;

        var candidates = new List<QqWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!processIds.Contains((int)pid)) return true;
            if (!GetWindowRect(hwnd, out var rect) || rect.Width < 240 || rect.Height < 240) return true;
            candidates.Add(new QqWindow(hwnd, (int)pid, rect));
            return true;
        }, IntPtr.Zero);

        return candidates
            .OrderByDescending(candidate => candidate.Rect.Width * candidate.Rect.Height)
            .FirstOrDefault();
    }

    private static void FocusWindow(IntPtr hwnd)
    {
        ShowWindow(hwnd, SwRestore);
        try
        {
            if (!WindowSensorHelper.ForceSetForegroundWindow(hwnd))
                SetForegroundWindow(hwnd);
        }
        catch
        {
            SetForegroundWindow(hwnd);
        }
        Thread.Sleep(120);
    }

    private static void ClickRelative(QqWindow window, double xRatio, double yRatio)
    {
        if (!GetWindowRect(window.Handle, out var rect))
            throw new InvalidOperationException("无法读取 QQ 窗口位置。");

        var x = rect.Left + (int)Math.Round(rect.Width * xRatio);
        var y = rect.Top + (int)Math.Round(rect.Height * yRatio);
        SetCursorPos(x, y);
        Thread.Sleep(80);
        mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(45);
        mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
    }

    private static Bitmap CaptureWindow(QqWindow window)
    {
        if (!GetWindowRect(window.Handle, out var rect) || rect.Width < 1 || rect.Height < 1)
            throw new InvalidOperationException("QQ 窗口尺寸不可用。");

        FocusWindow(window.Handle);
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static double ComputeChatDifference(Bitmap before, Bitmap after)
    {
        if (before.Width != after.Width || before.Height != after.Height) return 1;

        var left = (int)(before.Width * 0.24);
        var right = (int)(before.Width * 0.98);
        var top = (int)(before.Height * 0.08);
        var bottom = (int)(before.Height * 0.73);
        long sampled = 0;
        long changed = 0;

        for (var y = top; y < bottom; y += 3)
        {
            for (var x = left; x < right; x += 3)
            {
                var a = before.GetPixel(x, y);
                var b = after.GetPixel(x, y);
                sampled++;
                var delta = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                if (delta >= 45) changed++;
            }
        }

        return sampled == 0 ? 0 : changed / (double)sampled;
    }

    private static void PasteTextPreservingClipboard(string text)
    {
        RunSta(() =>
        {
            var backup = CaptureClipboard();
            try
            {
                SetClipboardTextWithRetry(text);
                Thread.Sleep(80);
                Forms.SendKeys.SendWait("^v");
                Thread.Sleep(100);
            }
            finally
            {
                RestoreClipboard(backup);
            }
        });
    }

    private static string ReadFocusedTextPreservingClipboard()
    {
        return RunSta(() =>
        {
            var backup = CaptureClipboard();
            var sentinel = "__YANZI_QQ_EMPTY_" + Guid.NewGuid().ToString("N") + "__";
            try
            {
                SetClipboardTextWithRetry(sentinel);
                Forms.SendKeys.SendWait("^a");
                Thread.Sleep(50);
                Forms.SendKeys.SendWait("^c");
                Thread.Sleep(110);
                var value = GetClipboardTextWithRetry();
                Forms.SendKeys.SendWait("{END}");
                return string.Equals(value, sentinel, StringComparison.Ordinal) ? string.Empty : value;
            }
            finally
            {
                RestoreClipboard(backup);
            }
        });
    }

    private static Forms.DataObject? CaptureClipboard()
    {
        try
        {
            var source = Forms.Clipboard.GetDataObject();
            if (source == null) return null;
            var copy = new Forms.DataObject();
            foreach (var format in source.GetFormats())
            {
                try
                {
                    var data = source.GetData(format);
                    if (data != null) copy.SetData(format, data);
                }
                catch { }
            }
            return copy;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreClipboard(Forms.DataObject? backup)
    {
        if (backup == null) return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Forms.Clipboard.SetDataObject(backup, true);
                return;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
    }

    private static void SetClipboardTextWithRetry(string text)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(40);
            }
        }
        throw new InvalidOperationException("无法临时写入剪贴板。", last);
    }

    private static string GetClipboardTextWithRetry()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                return Forms.Clipboard.ContainsText(Forms.TextDataFormat.UnicodeText)
                    ? Forms.Clipboard.GetText(Forms.TextDataFormat.UnicodeText)
                    : string.Empty;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return string.Empty;
    }

    private static T RunSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        done.Wait();
        if (failure != null) throw new InvalidOperationException("QQ UI 操作失败。", failure);
        return result!;
    }

    private static void RunSta(Action action)
        => RunSta(() =>
        {
            action();
            return true;
        });

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\r', '\n');

    private static string CaptureVerificationWindow(QqWindow window)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            HostRuntimeProfile.DataDirectoryName,
            "QqCaptures");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"qq-login-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var bitmap = CaptureWindow(window);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
