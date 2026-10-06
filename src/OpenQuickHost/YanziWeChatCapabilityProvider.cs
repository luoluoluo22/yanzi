using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

/// <summary>
/// 微信（新版 Weixin.exe）专有能力。
/// Qt 客户端当前不暴露可用的 UIA 子树，因此这里采用：
/// 进程/窗口状态机 + 窗口相对坐标 + 视觉校验 + 临时剪贴板粘贴并恢复。
/// </summary>
public static class YanziWeChatCapabilityProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IntPtr _lastFileTransferWindow;
    private static ulong _lastFileTransferTitleHash;
    private static bool _hasFileTransferTitleHash;

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

    private sealed record WeChatWindow(IntPtr Handle, int ProcessId, NativeRect Rect)
    {
        public bool LooksLikeLogin => Rect.Width is >= 220 and <= 460 && Rect.Height is >= 260 and <= 560;
        public bool LooksLikeMain => Rect.Width >= 560 && Rect.Height >= 560;
    }

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "wechat.status",
            Description = "查询新版微信 Weixin.exe 是否运行、是否已进入主界面，以及当前窗口尺寸状态",
            Permissions = ["application.read"],
            Category = "wechat",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "wechat.fileTransfer.sendText",
            Description = "确保新版微信已打开并登录，进入文件传输助手发送文字；需要扫码/手机确认时自动把微信验证窗口截图发送到燕子手机。发送后通过输入框清空与聊天区域变化双重确认，结果不确定时绝不自动重发。",
            Permissions = ["application.run", "ui.automation", "device.message.send"],
            Category = "wechat",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "text":{"type":"string","minLength":1},
                "verificationTarget":{"type":"string","description":"需要登录验证时，二维码/确认截图发送到的燕子手机；省略则选择最近在线 Android 手机"},
                "loginWaitSeconds":{"type":"integer","minimum":5,"maximum":120,"default":60}
              },
              "required":["text"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = SendFileTransferTextAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var window = FindBestWindow();
        if (window == null)
            return Task.FromResult<object?>(new
            {
                running = Process.GetProcessesByName("Weixin").Length > 0,
                state = "not_visible",
                loggedIn = false
            });

        return Task.FromResult<object?>(new
        {
            running = true,
            state = window.LooksLikeMain ? "main" : window.LooksLikeLogin ? "login" : "unknown",
            loggedIn = window.LooksLikeMain,
            processId = window.ProcessId,
            window = new { width = window.Rect.Width, height = window.Rect.Height }
        });
    }

    private static async Task<object?> SendFileTransferTextAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var text = input.GetProperty("text").GetString() ?? "";
        if (text.Length > 4000)
            throw new ArgumentException("微信文字消息不能超过 4000 个字符。");
        var verificationTarget = input.TryGetProperty("verificationTarget", out var target) ? target.GetString() : null;
        var loginWaitSeconds = input.TryGetProperty("loginWaitSeconds", out var wait) ? wait.GetInt32() : 60;

        await Gate.WaitAsync();
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var launchResult = await EnsureVisibleWeixinAsync();
            var window = FindBestWindow() ?? throw new InvalidOperationException("新版微信已启动，但未找到可见窗口。");

            object? verificationDelivery = null;
            string? verificationCapture = null;
            var loginAction = "already_logged_in";

            if (!window.LooksLikeMain)
            {
                if (!window.LooksLikeLogin)
                    throw new InvalidOperationException($"微信窗口状态无法识别：{window.Rect.Width}x{window.Rect.Height}。");

                loginAction = "clicked_enter_wechat";
                FocusWindow(window.Handle);
                using (var loginBitmap = CaptureWindow(window))
                {
                    var point = FindPrimaryGreenButton(loginBitmap)
                        ?? new Point(loginBitmap.Width / 2, (int)Math.Round(loginBitmap.Height * 0.758));
                    ClickAbsolute(window.Rect.Left + point.X, window.Rect.Top + point.Y);
                }

                window = await WaitForMainWindowAsync(TimeSpan.FromSeconds(8)) ?? FindBestWindow()
                    ?? throw new InvalidOperationException("点击进入微信后，微信窗口消失。");

                if (!window.LooksLikeMain)
                {
                    loginAction = "verification_required";
                    verificationCapture = CaptureVerificationWindow(window);
                    try
                    {
                        verificationDelivery = await YanziCapabilityRegistry.InvokeAsync(
                            "chat.send",
                            new
                            {
                                target = verificationTarget,
                                text = "微信需要登录验证，请在手机上扫码或按画面提示确认。完成后燕子会继续等待登录。",
                                filePath = verificationCapture,
                                kind = "photo",
                                waitForAck = true,
                                timeoutSeconds = 30
                            },
                            YanziCapabilityCaller.LocalAgent);
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog("WeChat verification screenshot delivery failed: " + ex.Message);
                    }

                    window = await WaitForMainWindowAsync(TimeSpan.FromSeconds(loginWaitSeconds));
                    if (window == null || !window.LooksLikeMain)
                    {
                        return new
                        {
                            status = "verification_required",
                            sent = false,
                            confirmed = false,
                            needsUserAction = true,
                            loginAction,
                            verificationCapture,
                            verificationDelivery,
                            message = "微信仍在等待登录验证；完成手机扫码/确认后可再次调用该能力。"
                        };
                    }

                    loginAction = "verification_completed";
                }
            }

            await Task.Delay(800);
            window = FindBestWindow() ?? throw new InvalidOperationException("微信主窗口不可用。");
            if (!window.LooksLikeMain)
                throw new InvalidOperationException("微信尚未进入主界面。");

            var navigation = await EnsureFileTransferAssistantAsync(window);
            window = FindBestWindow() ?? window;
            FocusWindow(window.Handle);
            ClickRelative(window, 0.675, 0.866);
            await Task.Delay(180);

            using var before = CaptureWindow(window);
            PasteTextPreservingClipboard(text);
            await Task.Delay(180);

            var preparedText = ReadFocusedTextPreservingClipboard();
            var inputPrepared = string.Equals(NormalizeText(preparedText), NormalizeText(text), StringComparison.Ordinal);
            if (!inputPrepared)
            {
                throw new InvalidOperationException("微信输入框校验失败，未发送消息，避免误发。");
            }

            RunSta(() =>
            {
                Forms.SendKeys.SendWait("{END}");
                Forms.SendKeys.SendWait("{ENTER}");
            });

            await Task.Delay(700);
            var confirmation = await ConfirmSentAsync(window, before, text);

            // 如果 Enter 被用户设置成换行：只有在“文本仍完整存在且聊天区没有变化”时，
            // 才安全地尝试 Ctrl+Enter；其他不确定情况绝不重发，避免重复消息。
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
                await Task.Delay(700);
                confirmation = await ConfirmSentAsync(window, before, text);
            }

            if (confirmation.Confirmed)
            {
                using var current = CaptureWindow(window);
                _lastFileTransferTitleHash = ComputeTitleHash(current);
                _lastFileTransferWindow = window.Handle;
                _hasFileTransferTitleHash = true;
            }

            return new
            {
                status = confirmation.Confirmed ? "sent" : "uncertain",
                sent = confirmation.Confirmed,
                confirmed = confirmation.Confirmed,
                recipient = "文件传输助手",
                navigation,
                loginAction,
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
                    ? "微信消息已在本地 UI 中确认发送。"
                    : "发送结果不确定；为避免重复消息，燕子没有自动重发。"
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<object?> EnsureVisibleWeixinAsync()
    {
        var existing = FindBestWindow();
        if (existing != null)
        {
            FocusWindow(existing.Handle);
            return new { action = "existing" };
        }

        var result = await YanziCapabilityRegistry.InvokeAsync(
            "app.open",
            new { app = "微信" },
            YanziCapabilityCaller.LocalAgent);

        for (var i = 0; i < 40; i++)
        {
            var window = FindBestWindow();
            if (window != null)
            {
                FocusWindow(window.Handle);
                return result;
            }
            await Task.Delay(150);
        }

        throw new InvalidOperationException("已尝试启动新版微信，但窗口未出现。");
    }

    private static async Task<WeChatWindow?> WaitForMainWindowAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var window = FindBestWindow();
            if (window?.LooksLikeMain == true)
                return window;
            await Task.Delay(400);
        }
        return null;
    }

    private static async Task<string> EnsureFileTransferAssistantAsync(WeChatWindow window)
    {
        FocusWindow(window.Handle);
        using (var snapshot = CaptureWindow(window))
        {
            if (_hasFileTransferTitleHash &&
                _lastFileTransferWindow == window.Handle &&
                HammingDistance(_lastFileTransferTitleHash, ComputeTitleHash(snapshot)) <= 5)
            {
                return "direct-current-conversation";
            }
        }

        // 新版 Qt 客户端不暴露 UIA 子控件。首轮/无法确认当前会话时，
        // 搜索比猜测最近会话行更稳定。
        ClickRelative(window, 0.226, 0.075);
        await Task.Delay(180);

        // 搜索框右侧自带清除按钮，避免 Ctrl+A 被输入法/Qt 截获。
        ClickRelative(window, 0.354, 0.075);
        await Task.Delay(150);
        ClickRelative(window, 0.226, 0.075);
        await Task.Delay(120);

        PasteTextPreservingClipboard("文件传输助手");
        await Task.Delay(700);

        // 正确搜索词下，文件传输助手是顶部功能结果。
        ClickRelative(window, 0.270, 0.188);
        await Task.Delay(700);

        // 确保输入区可聚焦；若导航失败，后续 preparedText 校验会阻止误发。
        ClickRelative(window, 0.675, 0.866);
        await Task.Delay(120);

        using var current = CaptureWindow(window);
        _lastFileTransferTitleHash = ComputeTitleHash(current);
        _lastFileTransferWindow = window.Handle;
        _hasFileTransferTitleHash = true;
        return "search";
    }

    private sealed record SendConfirmation(bool Confirmed, bool InputCleared, double VisualDifferenceRatio, string RemainingInput);

    private static async Task<SendConfirmation> ConfirmSentAsync(WeChatWindow window, Bitmap before, string expectedText)
    {
        double difference = 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var after = CaptureWindow(window);
            difference = Math.Max(difference, ComputeChatDifference(before, after));
            if (difference >= 0.002) break;
            await Task.Delay(450);
        }

        FocusWindow(window.Handle);
        ClickRelative(window, 0.675, 0.866);
        await Task.Delay(100);
        var remaining = ReadFocusedTextPreservingClipboard();
        var inputCleared = string.IsNullOrEmpty(NormalizeText(remaining));
        return new SendConfirmation(inputCleared && difference >= 0.002, inputCleared, difference, remaining);
    }

    private static WeChatWindow? FindBestWindow()
    {
        var processIds = Process.GetProcessesByName("Weixin").Select(process => process.Id).ToHashSet();
        if (processIds.Count == 0) return null;

        var candidates = new List<WeChatWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!processIds.Contains((int)pid)) return true;
            if (!GetWindowRect(hwnd, out var rect) || rect.Width < 180 || rect.Height < 180) return true;
            candidates.Add(new WeChatWindow(hwnd, (int)pid, rect));
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

    private static void ClickRelative(WeChatWindow window, double xRatio, double yRatio)
    {
        if (!GetWindowRect(window.Handle, out var rect))
            throw new InvalidOperationException("无法读取微信窗口位置。");
        var x = rect.Left + (int)Math.Round(rect.Width * xRatio);
        var y = rect.Top + (int)Math.Round(rect.Height * yRatio);
        ClickAbsolute(x, y);
    }

    private static void ClickAbsolute(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(80);
        mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(45);
        mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
    }

    private static Bitmap CaptureWindow(WeChatWindow window)
    {
        if (!GetWindowRect(window.Handle, out var rect) || rect.Width < 1 || rect.Height < 1)
            throw new InvalidOperationException("微信窗口尺寸不可用。");

        FocusWindow(window.Handle);
        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static Point? FindPrimaryGreenButton(Bitmap bitmap)
    {
        var startY = bitmap.Height / 2;
        var bestY = -1;
        var bestCount = 0;
        var bestMinX = 0;
        var bestMaxX = 0;

        for (var y = startY; y < bitmap.Height - 8; y += 2)
        {
            var count = 0;
            var minX = bitmap.Width;
            var maxX = -1;
            for (var x = 8; x < bitmap.Width - 8; x += 2)
            {
                var color = bitmap.GetPixel(x, y);
                if (!IsWeChatGreen(color)) continue;
                count++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestY = y;
                bestMinX = minX;
                bestMaxX = maxX;
            }
        }

        if (bestY < 0 || bestCount < bitmap.Width * 0.12 || bestMaxX <= bestMinX)
            return null;

        return new Point((bestMinX + bestMaxX) / 2, bestY);
    }

    private static bool IsWeChatGreen(Color color)
        => color.G >= 115 && color.G >= color.R + 28 && color.G >= color.B + 18;

    private static double ComputeChatDifference(Bitmap before, Bitmap after)
    {
        if (before.Width != after.Width || before.Height != after.Height) return 1;

        var left = (int)(before.Width * 0.48);
        var right = (int)(before.Width * 0.98);
        var top = (int)(before.Height * 0.14);
        var bottom = (int)(before.Height * 0.76);
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

    private static ulong ComputeTitleHash(Bitmap bitmap)
    {
        var left = (int)(bitmap.Width * 0.48);
        var top = (int)(bitmap.Height * 0.02);
        var width = Math.Max(8, (int)(bitmap.Width * 0.34));
        var height = Math.Max(8, (int)(bitmap.Height * 0.10));
        var values = new byte[64];
        var sum = 0;

        for (var gy = 0; gy < 8; gy++)
        {
            for (var gx = 0; gx < 8; gx++)
            {
                var x = Math.Min(bitmap.Width - 1, left + gx * width / 8 + width / 16);
                var y = Math.Min(bitmap.Height - 1, top + gy * height / 8 + height / 16);
                var color = bitmap.GetPixel(x, y);
                var value = (byte)((color.R * 30 + color.G * 59 + color.B * 11) / 100);
                values[gy * 8 + gx] = value;
                sum += value;
            }
        }

        var average = sum / 64;
        ulong hash = 0;
        for (var index = 0; index < values.Length; index++)
            if (values[index] >= average) hash |= 1UL << index;
        return hash;
    }

    private static int HammingDistance(ulong a, ulong b)
        => System.Numerics.BitOperations.PopCount(a ^ b);

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
                Thread.Sleep(120);
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
            var sentinel = "__YANZI_WECHAT_EMPTY_" + Guid.NewGuid().ToString("N") + "__";
            try
            {
                SetClipboardTextWithRetry(sentinel);
                Forms.SendKeys.SendWait("^a");
                Thread.Sleep(50);
                Forms.SendKeys.SendWait("^c");
                Thread.Sleep(120);
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
                catch
                {
                }
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
        if (failure != null) throw new InvalidOperationException("微信 UI 操作失败。", failure);
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

    private static string CaptureVerificationWindow(WeChatWindow window)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            HostRuntimeProfile.DataDirectoryName,
            "WeChatCaptures");
        Directory.CreateDirectory(directory);

        foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                    File.Delete(file);
            }
            catch
            {
            }
        }

        var path = Path.Combine(directory, $"wechat-login-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var bitmap = CaptureWindow(window);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
