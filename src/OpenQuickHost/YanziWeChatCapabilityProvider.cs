using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly SemaphoreSlim LayoutGate = new(1, 1);
    private static readonly string[] MuteIconTemplate =
    [
        "....##.....",
        "##.####....",
        ".###..##...",
        "..##...#...",
        ".####..##..",
        ".##.##.##..",
        ".##..####..",
        ".##...###..",
        ".#.....##..",
        "##......##.",
        ".#######.##"
    ];
    private static LayoutAnalysisCache? _layoutAnalysisCache;
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
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

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
            Name = "wechat.layout.inspect",
            Description = "后台截取新版微信主窗口，调用燕子 OCR，并将文字按导航、搜索、会话列表、会话标题、消息区和输入区分组；供微信标注与监听校准使用。",
            Permissions = ["application.read", "screen.capture", "file.read"],
            Category = "wechat",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"includeLines":{"type":"boolean"},"forceAnalysis":{"type":"boolean"}},"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = InspectLayoutAsync
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
            window = new { left = window.Rect.Left, top = window.Rect.Top, width = window.Rect.Width, height = window.Rect.Height, dpi = GetWindowDpi(window.Handle) }
        });
    }


    private static async Task<object?> InspectLayoutAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var includeLines = !input.TryGetProperty("includeLines", out var include)
            || include.ValueKind != JsonValueKind.False;
        var forceAnalysis = input.TryGetProperty("forceAnalysis", out var force)
            && force.ValueKind == JsonValueKind.True;

        await LayoutGate.WaitAsync();
        try
        {
            var totalWatch = Stopwatch.StartNew();
            var window = FindBestWindow() ?? throw new InvalidOperationException("未找到可见的新版微信窗口。");
            if (!window.LooksLikeMain)
                throw new InvalidOperationException($"微信尚未进入主界面：{window.Rect.Width}x{window.Rect.Height}。");

            if (!GetWindowRect(window.Handle, out var currentRect))
                throw new InvalidOperationException("无法读取微信主窗口位置。");
            window = window with { Rect = currentRect };

            var dpi = GetWindowDpi(window.Handle);
            var scale = dpi / 96d;
            var regions = BuildLayoutRegions(window.Rect.Width, window.Rect.Height, scale);
            var monitoredRegions = regions
                .Where(region => region.Id is "navigation" or "conversationList" or "chatHeader" or "messageList")
                .ToArray();

            var captureWatch = Stopwatch.StartNew();
            using var capturedBitmap = CaptureWindowBackground(window);
            captureWatch.Stop();

            var compareWatch = Stopwatch.StartNew();
            var fingerprints = BuildVisualFingerprints(capturedBitmap, monitoredRegions, scale);
            var change = CompareVisualFingerprints(
                _layoutAnalysisCache,
                window.Handle,
                window.Rect.Width,
                window.Rect.Height,
                dpi,
                fingerprints);
            compareWatch.Stop();

            var canReuse = !forceAnalysis
                && _layoutAnalysisCache != null
                && change.Compatible
                && !change.HasMeaningfulChange;

            if (canReuse)
            {
                var cached = _layoutAnalysisCache!;
                totalWatch.Stop();
                return new
                {
                    state = "main",
                    capturedAtUtc = DateTimeOffset.UtcNow,
                    analyzedAtUtc = cached.AnalyzedAtUtc,
                    processId = window.ProcessId,
                    window = new
                    {
                        handle = window.Handle.ToInt64(),
                        left = window.Rect.Left,
                        top = window.Rect.Top,
                        width = window.Rect.Width,
                        height = window.Rect.Height,
                        dpi,
                        scale = Math.Round(scale, 4)
                    },
                    capturePath = cached.CapturePath,
                    layoutVersion = "weixin-win-v7",
                    regions = cached.Regions.Clone(),
                    uiObjects = cached.UiObjects.Clone(),
                    navigation = cached.Navigation.Clone(),
                    conversations = cached.Conversations.Clone(),
                    messageObjects = cached.MessageObjects.Clone(),
                    text = cached.Text,
                    detectedCount = cached.DetectedCount,
                    elapsedMs = cached.OcrElapsedMs,
                    lines = includeLines ? cached.Lines.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>()),
                    changeDetection = BuildChangeDetectionDto(change, reused: true, ocrSkipped: true, forceAnalysis),
                    timings = new
                    {
                        captureMs = Math.Round(captureWatch.Elapsed.TotalMilliseconds, 2),
                        compareMs = Math.Round(compareWatch.Elapsed.TotalMilliseconds, 2),
                        ocrMs = 0d,
                        analysisMs = 0d,
                        totalMs = Math.Round(totalWatch.Elapsed.TotalMilliseconds, 2)
                    }
                };
            }

            var capturePath = GetLayoutCapturePath();
            capturedBitmap.Save(capturePath, ImageFormat.Png);
            using var visualBitmap = new Bitmap(capturedBitmap);

            var ocrWatch = Stopwatch.StartNew();
            var ocrRaw = await YanziCapabilityRegistry.InvokeAsync(
                "ocr.recognize",
                new { imagePath = capturePath, includeLines = true },
                YanziCapabilityCaller.LocalAgent);
            ocrWatch.Stop();

            var ocr = ocrRaw is JsonElement element
                ? element.Clone()
                : JsonSerializer.SerializeToElement(ocrRaw);

            var analysisWatch = Stopwatch.StartNew();
            var uiObjects = BuildFixedUiObjects(window.Rect.Width, window.Rect.Height, scale, regions);
            var navigation = AnalyzeNavigationState(visualBitmap, uiObjects, scale);
            var effectiveUiObjects = navigation.IsChatView
                ? uiObjects
                : uiObjects.Where(item => item.Kind is "windowControl" or "profileAvatar" or "navigationItem" or "navigationUtility").ToArray();
            var mappedLines = new List<object>();
            var grouped = regions.ToDictionary(region => region.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

            if (ocr.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (!TryReadOcrBox(line, out var x, out var y, out var width, out var height))
                        continue;

                    var centerX = x + width / 2d;
                    var centerY = y + height / 2d;
                    var region = regions.FirstOrDefault(item => item.Contains(centerX, centerY))
                        ?? new LayoutRegion("unknown", "未归类", 0, 0, window.Rect.Width, window.Rect.Height);
                    var text = ReadJsonString(line, "text") ?? string.Empty;
                    var role = ClassifyOcrRole(region, effectiveUiObjects, text, x, y, width, height, scale, navigation.IsChatView);
                    if (!string.IsNullOrWhiteSpace(text)
                        && ShouldIncludeInRegionText(role)
                        && grouped.TryGetValue(region.Id, out var texts))
                    {
                        texts.Add(text);
                    }

                    mappedLines.Add(new
                    {
                        text,
                        role,
                        regionId = region.Id,
                        regionLabel = region.Label,
                        box = new { x, y, width, height },
                        polygon = line.TryGetProperty("polygon", out var polygon) ? polygon.Clone() : default(JsonElement),
                        recognitionScore = ReadJsonDouble(line, "recognitionScore"),
                        recognitionScoreScale = ReadJsonString(line, "recognitionScoreScale"),
                        classificationConfidence = ReadJsonDouble(line, "classificationConfidence"),
                        rotationDegrees = ReadJsonInt(line, "rotationDegrees")
                    });
                }
            }

            var regionDtos = regions.Select(region => new
            {
                id = region.Id,
                label = GetRegionLabel(region, navigation),
                x = region.X,
                y = region.Y,
                width = region.Width,
                height = region.Height,
                text = grouped.TryGetValue(region.Id, out var texts) ? string.Join(Environment.NewLine, texts) : string.Empty,
                lineCount = grouped.TryGetValue(region.Id, out var items) ? items.Count : 0
            }).ToArray();

            var conversationRegion = regions.First(region => string.Equals(region.Id, "conversationList", StringComparison.Ordinal));
            var messageRegion = regions.First(region => string.Equals(region.Id, "messageList", StringComparison.Ordinal));
            var ocrInfos = ExtractOcrLines(ocr);
            var conversations = navigation.IsChatView
                ? BuildConversationRows(visualBitmap, conversationRegion, ocrInfos, scale)
                : Array.Empty<object>();
            var messageObjects = navigation.IsChatView
                ? BuildMessageObjects(visualBitmap, messageRegion, ocrInfos, scale)
                : Array.Empty<object>();

            var fullText = ocr.TryGetProperty("text", out var fullTextProperty)
                ? fullTextProperty.GetString() ?? string.Empty
                : string.Empty;
            var detectedCount = ocr.TryGetProperty("detectedCount", out var count)
                && count.TryGetInt32(out var detected)
                    ? detected
                    : mappedLines.Count;
            var ocrElapsedMs = ocr.TryGetProperty("elapsedMs", out var elapsed)
                && elapsed.TryGetDouble(out var ms)
                    ? ms
                    : (double?)null;

            analysisWatch.Stop();
            var analyzedAtUtc = DateTimeOffset.UtcNow;
            var regionElement = JsonSerializer.SerializeToElement(regionDtos).Clone();
            var uiObjectElement = JsonSerializer.SerializeToElement(effectiveUiObjects.Select(ToUiObjectDto).ToArray()).Clone();
            var navigationElement = JsonSerializer.SerializeToElement(ToNavigationDto(navigation)).Clone();
            var conversationElement = JsonSerializer.SerializeToElement(conversations).Clone();
            var messageObjectElement = JsonSerializer.SerializeToElement(messageObjects).Clone();
            var lineElement = JsonSerializer.SerializeToElement(mappedLines.ToArray()).Clone();

            _layoutAnalysisCache = new LayoutAnalysisCache(
                window.Handle,
                window.Rect.Width,
                window.Rect.Height,
                dpi,
                analyzedAtUtc,
                capturePath,
                fingerprints,
                regionElement,
                uiObjectElement,
                navigationElement,
                conversationElement,
                messageObjectElement,
                lineElement,
                fullText,
                detectedCount,
                ocrElapsedMs);

            totalWatch.Stop();
            return new
            {
                state = "main",
                capturedAtUtc = DateTimeOffset.UtcNow,
                analyzedAtUtc,
                processId = window.ProcessId,
                window = new
                {
                    handle = window.Handle.ToInt64(),
                    left = window.Rect.Left,
                    top = window.Rect.Top,
                    width = window.Rect.Width,
                    height = window.Rect.Height,
                    dpi,
                    scale = Math.Round(scale, 4)
                },
                capturePath,
                layoutVersion = "weixin-win-v7",
                regions = regionDtos,
                uiObjects = effectiveUiObjects.Select(ToUiObjectDto).ToArray(),
                navigation = ToNavigationDto(navigation),
                conversations,
                messageObjects,
                text = fullText,
                detectedCount,
                elapsedMs = ocrElapsedMs,
                lines = includeLines ? mappedLines.ToArray() : Array.Empty<object>(),
                changeDetection = BuildChangeDetectionDto(change, reused: false, ocrSkipped: false, forceAnalysis),
                timings = new
                {
                    captureMs = Math.Round(captureWatch.Elapsed.TotalMilliseconds, 2),
                    compareMs = Math.Round(compareWatch.Elapsed.TotalMilliseconds, 2),
                    ocrMs = Math.Round(ocrWatch.Elapsed.TotalMilliseconds, 2),
                    analysisMs = Math.Round(analysisWatch.Elapsed.TotalMilliseconds, 2),
                    totalMs = Math.Round(totalWatch.Elapsed.TotalMilliseconds, 2)
                }
            };
        }
        finally
        {
            LayoutGate.Release();
        }
    }
    private static Dictionary<string, VisualFingerprint> BuildVisualFingerprints(
        Bitmap bitmap,
        IReadOnlyList<LayoutRegion> regions,
        double scale)
    {
        var result = new Dictionary<string, VisualFingerprint>(StringComparer.OrdinalIgnoreCase);
        var step = Math.Max(4, (int)Math.Round(6 * scale));

        foreach (var region in regions)
        {
            var rect = ClampRect(new Rectangle(region.X, region.Y, region.Width, region.Height), bitmap.Size);
            var samples = new List<byte>(Math.Max(16, rect.Width * rect.Height / Math.Max(1, step * step) * 3));

            for (var y = rect.Top + step / 2; y < rect.Bottom; y += step)
            {
                for (var x = rect.Left + step / 2; x < rect.Right; x += step)
                {
                    var color = bitmap.GetPixel(x, y);
                    samples.Add((byte)(color.R & 0xF8));
                    samples.Add((byte)(color.G & 0xF8));
                    samples.Add((byte)(color.B & 0xF8));
                }
            }

            result[region.Id] = new VisualFingerprint(
                region.Id,
                rect.Width,
                rect.Height,
                step,
                samples.ToArray());
        }

        return result;
    }

    private static LayoutChangeSummary CompareVisualFingerprints(
        LayoutAnalysisCache? cache,
        IntPtr handle,
        int width,
        int height,
        uint dpi,
        IReadOnlyDictionary<string, VisualFingerprint> current)
    {
        if (cache == null)
        {
            return new LayoutChangeSummary(
                Compatible: false,
                HasMeaningfulChange: true,
                Reason: "cold_start",
                Regions: current.Values
                    .Select(item => new LayoutRegionChange(item.RegionId, 1d, ChangeThreshold(item.RegionId), true))
                    .ToArray());
        }

        if (cache.Handle != handle
            || cache.Width != width
            || cache.Height != height
            || cache.Dpi != dpi)
        {
            return new LayoutChangeSummary(
                Compatible: false,
                HasMeaningfulChange: true,
                Reason: "window_context_changed",
                Regions: current.Values
                    .Select(item => new LayoutRegionChange(item.RegionId, 1d, ChangeThreshold(item.RegionId), true))
                    .ToArray());
        }

        var changes = new List<LayoutRegionChange>();
        foreach (var pair in current)
        {
            var threshold = ChangeThreshold(pair.Key);
            if (!cache.Fingerprints.TryGetValue(pair.Key, out var previous)
                || previous.Width != pair.Value.Width
                || previous.Height != pair.Value.Height
                || previous.Step != pair.Value.Step
                || previous.Samples.Length != pair.Value.Samples.Length)
            {
                changes.Add(new LayoutRegionChange(pair.Key, 1d, threshold, true));
                continue;
            }

            var sampleCount = pair.Value.Samples.Length / 3;
            if (sampleCount == 0)
            {
                changes.Add(new LayoutRegionChange(pair.Key, 0d, threshold, false));
                continue;
            }

            var changed = 0;
            for (var i = 0; i + 2 < pair.Value.Samples.Length; i += 3)
            {
                var delta = Math.Abs(pair.Value.Samples[i] - previous.Samples[i])
                    + Math.Abs(pair.Value.Samples[i + 1] - previous.Samples[i + 1])
                    + Math.Abs(pair.Value.Samples[i + 2] - previous.Samples[i + 2]);
                if (delta >= 48)
                    changed++;
            }

            var ratio = changed / (double)sampleCount;
            changes.Add(new LayoutRegionChange(pair.Key, ratio, threshold, ratio >= threshold));
        }

        var meaningful = changes.Any(item => item.Changed);
        return new LayoutChangeSummary(
            Compatible: true,
            HasMeaningfulChange: meaningful,
            Reason: meaningful ? "visual_change" : "stable",
            Regions: changes.ToArray());
    }

    private static double ChangeThreshold(string regionId)
        => regionId switch
        {
            "navigation" => 0.003,
            "messageList" => 0.003,
            "conversationList" => 0.004,
            "chatHeader" => 0.004,
            _ => 0.005
        };

    private static object BuildChangeDetectionDto(
        LayoutChangeSummary change,
        bool reused,
        bool ocrSkipped,
        bool forceAnalysis)
        => new
        {
            reused,
            ocrSkipped,
            forced = forceAnalysis,
            compatible = change.Compatible,
            meaningfulChange = change.HasMeaningfulChange,
            reason = forceAnalysis ? "forced" : change.Reason,
            regions = change.Regions.Select(item => new
            {
                id = item.RegionId,
                ratio = Math.Round(item.Ratio, 6),
                threshold = item.Threshold,
                changed = item.Changed
            }).ToArray()
        };
    private static uint GetWindowDpi(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi is >= 72 and <= 768 ? dpi : 96;
        }
        catch
        {
            return 96;
        }
    }

    private static LayoutRegion[] BuildLayoutRegions(int width, int height, double scale)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var chrome = Math.Clamp(Dip(32), 1, Math.Max(1, height - 1));
        var nav = Math.Clamp(Dip(68), 1, Math.Max(1, width - 1));
        var conversations = Math.Clamp(Dip(239), 1, Math.Max(1, width - nav));
        var splitX = Math.Clamp(nav + conversations, nav + 1, Math.Max(nav + 1, width - 1));
        var strip = Math.Clamp(Dip(50), 1, Math.Max(1, height - chrome));
        var contentTop = Math.Clamp(chrome + strip, chrome + 1, Math.Max(chrome + 1, height - 1));
        var composerHeight = Math.Clamp(Dip(152), 1, Math.Max(1, height - contentTop));
        var composerTop = Math.Clamp(height - composerHeight, contentTop, height);

        return
        [
            new("chrome", "窗口栏", 0, 0, width, chrome),
            new("navigation", "导航栏", 0, chrome, nav, Math.Max(0, height - chrome)),
            new("search", "搜索区", nav, chrome, Math.Max(0, splitX - nav), Math.Max(0, contentTop - chrome)),
            new("conversationList", "会话列表", nav, contentTop, Math.Max(0, splitX - nav), Math.Max(0, height - contentTop)),
            new("chatHeader", "会话标题", splitX, chrome, Math.Max(0, width - splitX), Math.Max(0, contentTop - chrome)),
            new("messageList", "消息区", splitX, contentTop, Math.Max(0, width - splitX), Math.Max(0, composerTop - contentTop)),
            new("composer", "输入区", splitX, composerTop, Math.Max(0, width - splitX), Math.Max(0, height - composerTop))
        ];
    }

    private static string GetLayoutCapturePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            HostRuntimeProfile.DataDirectoryName,
            "WeChatCaptures");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "wechat-layout-current.png");
    }

    private static Bitmap CaptureWindowBackground(WeChatWindow window)
    {
        if (!GetWindowRect(window.Handle, out var rect) || rect.Width < 1 || rect.Height < 1)
            throw new InvalidOperationException("微信窗口尺寸不可用。");

        var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        bool captured;
        try
        {
            captured = PrintWindow(window.Handle, hdc, 2) || PrintWindow(window.Handle, hdc, 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        if (!captured)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("微信后台窗口截图失败。");
        }

        return bitmap;
    }

    private static VisualObject[] BuildFixedUiObjects(
        int width,
        int height,
        double scale,
        IReadOnlyList<LayoutRegion> regions)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var chrome = regions.First(region => string.Equals(region.Id, "chrome", StringComparison.Ordinal));
        var navigation = regions.First(region => string.Equals(region.Id, "navigation", StringComparison.Ordinal));
        var chatHeader = regions.First(region => string.Equals(region.Id, "chatHeader", StringComparison.Ordinal));
        var composer = regions.First(region => string.Equals(region.Id, "composer", StringComparison.Ordinal));
        var items = new List<VisualObject>();

        void Add(string id, string kind, string label, string regionId, Rectangle rect)
            => items.Add(new VisualObject(id, kind, label, regionId, ClampRect(rect, new Size(width, height))));

        // 左侧微信导航栏。图标本身不是文字，位置固定；当前激活项由绿色视觉状态判断。
        Add("nav.profile", "profileAvatar", "我的头像", navigation.Id,
            new Rectangle(navigation.X + Dip(18), navigation.Y + Dip(11), Dip(40), Dip(40)));
        Add("nav.chat", "navigationItem", "聊天", navigation.Id,
            new Rectangle(navigation.X + Dip(17), navigation.Y + Dip(61), Dip(42), Dip(42)));
        Add("nav.contacts", "navigationItem", "联系人", navigation.Id,
            new Rectangle(navigation.X + Dip(17), navigation.Y + Dip(110), Dip(42), Dip(42)));
        Add("nav.favorites", "navigationItem", "收藏", navigation.Id,
            new Rectangle(navigation.X + Dip(17), navigation.Y + Dip(159), Dip(42), Dip(42)));
        Add("nav.moments", "navigationItem", "朋友圈", navigation.Id,
            new Rectangle(navigation.X + Dip(17), navigation.Y + Dip(208), Dip(42), Dip(42)));
        Add("nav.phone", "navigationUtility", "手机", navigation.Id,
            new Rectangle(navigation.X + Dip(17), Math.Max(navigation.Y, height - Dip(110)), Dip(42), Dip(42)));
        Add("nav.settings", "navigationUtility", "设置", navigation.Id,
            new Rectangle(navigation.X + Dip(17), Math.Max(navigation.Y, height - Dip(61)), Dip(42), Dip(42)));
        // 窗口右上角固定控件。它们是图标，不应该交给 OCR 当成 Y/X/1 等字符。
        Add("window.pin", "windowControl", "置顶", chrome.Id,
            new Rectangle(width - Dip(174), chrome.Y, Dip(32), chrome.Height));
        Add("window.minimize", "windowControl", "最小化", chrome.Id,
            new Rectangle(width - Dip(134), chrome.Y, Dip(36), chrome.Height));
        Add("window.maximize", "windowControl", "最大化/还原", chrome.Id,
            new Rectangle(width - Dip(92), chrome.Y, Dip(38), chrome.Height));
        Add("window.close", "windowControl", "关闭", chrome.Id,
            new Rectangle(width - Dip(48), chrome.Y, Dip(46), chrome.Height));

        // 会话标题右侧操作按钮。
        Add("chat.actions", "headerControl", "聊天菜单", chatHeader.Id,
            new Rectangle(chatHeader.X + chatHeader.Width - Dip(144), chatHeader.Y + Dip(7), Dip(45), chatHeader.Height - Dip(10)));
        Add("chat.call", "headerControl", "通话", chatHeader.Id,
            new Rectangle(chatHeader.X + chatHeader.Width - Dip(94), chatHeader.Y + Dip(7), Dip(42), chatHeader.Height - Dip(10)));
        Add("chat.more", "headerControl", "更多", chatHeader.Id,
            new Rectangle(chatHeader.X + chatHeader.Width - Dip(50), chatHeader.Y + Dip(7), Dip(48), chatHeader.Height - Dip(10)));

        // 输入区底部工具栏。按当前微信 Windows UI 的 DIP 尺寸固定锚定。
        var toolY = composer.Y + composer.Height - Dip(49);
        Add("composer.emoji", "composerControl", "表情", composer.Id,
            new Rectangle(composer.X + Dip(15), toolY, Dip(34), Dip(40)));
        Add("composer.favorite", "composerControl", "收藏", composer.Id,
            new Rectangle(composer.X + Dip(50), toolY, Dip(38), Dip(40)));
        Add("composer.file", "composerControl", "文件", composer.Id,
            new Rectangle(composer.X + Dip(86), toolY, Dip(38), Dip(40)));
        Add("composer.screenshot", "composerControl", "截图/剪贴", composer.Id,
            new Rectangle(composer.X + Dip(124), toolY, Dip(48), Dip(40)));
        Add("composer.speechInput", "composerControl", "语音识别", composer.Id,
            new Rectangle(composer.X + Dip(172), toolY, Dip(40), Dip(40)));
        Add("composer.voiceMode", "composerControl", "语音模式", composer.Id,
            new Rectangle(composer.X + composer.Width - Dip(116), toolY, Dip(42), Dip(40)));
        Add("composer.send", "composerControl", "发送", composer.Id,
            new Rectangle(composer.X + composer.Width - Dip(74), toolY, Dip(70), Dip(40)));

        return items.ToArray();
    }

    private static object ToUiObjectDto(VisualObject item)
        => new
        {
            id = item.Id,
            kind = item.Kind,
            label = item.Label,
            regionId = item.RegionId,
            bounds = new
            {
                x = item.Bounds.X,
                y = item.Bounds.Y,
                width = item.Bounds.Width,
                height = item.Bounds.Height
            }
        };

    private static NavigationAnalysis AnalyzeNavigationState(
        Bitmap bitmap,
        IReadOnlyList<VisualObject> uiObjects,
        double scale)
    {
        var profile = uiObjects.First(item => string.Equals(item.Id, "nav.profile", StringComparison.Ordinal));
        var itemObjects = uiObjects
            .Where(item => item.Kind is "navigationItem" or "navigationUtility")
            .ToArray();

        var items = new List<NavigationItemAnalysis>();
        foreach (var item in itemObjects)
        {
            var greenRatio = CountWechatGreenPixels(bitmap, item.Bounds)
                / (double)Math.Max(1, item.Bounds.Width * item.Bounds.Height);
            var redRatio = CountWechatRedPixels(bitmap, item.Bounds)
                / (double)Math.Max(1, item.Bounds.Width * item.Bounds.Height);

            items.Add(new NavigationItemAnalysis(
                Id: item.Id["nav.".Length..],
                Label: item.Label,
                Kind: item.Kind,
                Bounds: item.Bounds,
                GreenRatio: greenRatio,
                RedRatio: redRatio,
                Active: false,
                Confidence: 0));
        }

        var mainItems = items
            .Where(item => string.Equals(item.Kind, "navigationItem", StringComparison.Ordinal))
            .OrderByDescending(item => item.GreenRatio)
            .ToArray();

        string active = "unknown";
        string activeLabel = "未知";
        var stateConfidence = 0d;

        if (mainItems.Length > 0)
        {
            var first = mainItems[0];
            var secondRatio = mainItems.Length > 1 ? mainItems[1].GreenRatio : 0d;

            // 新版微信当前主导航使用绿色图标表示选中。实测聊天态约 0.23，
            // 非选中项接近 0；这里留出较大版本/缩放余量。
            if (first.GreenRatio >= 0.025 && first.GreenRatio - secondRatio >= 0.012)
            {
                active = first.Id;
                activeLabel = first.Label;
                stateConfidence = Math.Clamp(
                    0.72 + Math.Min(0.18, first.GreenRatio * 0.8)
                         + Math.Min(0.10, (first.GreenRatio - secondRatio) * 1.5),
                    0.72,
                    0.99);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var isActive = string.Equals(item.Id, active, StringComparison.Ordinal);
            var confidence = isActive
                ? stateConfidence
                : Math.Clamp(0.75 + Math.Min(0.2, Math.Max(0, 0.025 - item.GreenRatio) * 5), 0.75, 0.95);
            items[i] = item with { Active = isActive, Confidence = confidence };
        }

        var profileEdgeCount = CountVisualEdges(bitmap, profile.Bounds);
        var profileEdgeRatio = profileEdgeCount
            / (double)Math.Max(1, profile.Bounds.Width * profile.Bounds.Height);
        var profileConfidence = Math.Clamp(0.72 + profileEdgeRatio * 1.6, 0.72, 0.99);

        return new NavigationAnalysis(
            Active: active,
            ActiveLabel: activeLabel,
            IsChatView: string.Equals(active, "chat", StringComparison.Ordinal),
            Confidence: stateConfidence,
            ProfileBounds: profile.Bounds,
            ProfileConfidence: profileConfidence,
            Items: items.ToArray());
    }

    private static object ToNavigationDto(NavigationAnalysis navigation)
        => new
        {
            active = navigation.Active,
            activeLabel = navigation.ActiveLabel,
            isChatView = navigation.IsChatView,
            confidence = Math.Round(navigation.Confidence, 3),
            profileAvatar = new
            {
                label = "我的头像",
                bounds = new
                {
                    x = navigation.ProfileBounds.X,
                    y = navigation.ProfileBounds.Y,
                    width = navigation.ProfileBounds.Width,
                    height = navigation.ProfileBounds.Height
                },
                confidence = Math.Round(navigation.ProfileConfidence, 3),
                method = "fixed_slot_visual_presence"
            },
            items = navigation.Items.Select(item => new
            {
                id = item.Id,
                label = item.Label,
                kind = item.Kind,
                active = item.Active,
                confidence = Math.Round(item.Confidence, 3),
                bounds = new
                {
                    x = item.Bounds.X,
                    y = item.Bounds.Y,
                    width = item.Bounds.Width,
                    height = item.Bounds.Height
                },
                evidence = new
                {
                    greenRatio = Math.Round(item.GreenRatio, 4),
                    redRatio = Math.Round(item.RedRatio, 4),
                    method = item.Kind == "navigationItem"
                        ? "wechat_green_active_state"
                        : "fixed_navigation_utility"
                }
            }).ToArray()
        };

    private static int CountWechatGreenPixels(Bitmap bitmap, Rectangle rect)
    {
        var count = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.G >= 105
                    && color.G - color.R >= 30
                    && color.G - color.B >= 18)
                {
                    count++;
                }
            }
        }
        return count;
    }
    private static string GetRegionLabel(LayoutRegion region, NavigationAnalysis navigation)
    {
        if (navigation.IsChatView)
            return region.Label;

        if (region.Id is "chrome" or "navigation" or "search")
            return region.Label;

        return navigation.Active switch
        {
            "contacts" => region.Id switch
            {
                "conversationList" => "联系人列表",
                "chatHeader" => "联系人标题区",
                "messageList" => "联系人内容区",
                "composer" => "联系人内容区",
                _ => region.Label
            },
            "favorites" => region.Id switch
            {
                "conversationList" => "收藏分类",
                "chatHeader" => "收藏标题区",
                "messageList" => "收藏内容区",
                "composer" => "收藏内容区",
                _ => region.Label
            },
            "moments" => region.Id switch
            {
                "conversationList" => "发现列表",
                "chatHeader" => "发现标题区",
                "messageList" => "发现内容区",
                "composer" => "发现内容区",
                _ => region.Label
            },
            _ => region.Id switch
            {
                "conversationList" => "主列表区",
                "chatHeader" => "内容标题区",
                "messageList" => "内容区",
                "composer" => "内容区",
                _ => region.Label
            }
        };
    }
    private static string ClassifyOcrRole(
        LayoutRegion region,
        IReadOnlyList<VisualObject> uiObjects,
        string text,
        double x,
        double y,
        double width,
        double height,
        double scale,
        bool isChatView)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var centerX = x + width / 2d;
        var centerY = y + height / 2d;

        if (uiObjects.Any(item => item.Contains(centerX, centerY)))
            return "controlIcon";

        if (!isChatView
            && region.Id is "conversationList" or "chatHeader" or "messageList" or "composer")
        {
            return "contentText";
        }

        if (string.Equals(region.Id, "conversationList", StringComparison.Ordinal))
        {
            var rowHeight = Dip(64);
            var rowOffset = ((int)Math.Round(centerY) - region.Y) % rowHeight;
            if (rowOffset < 0)
                rowOffset += rowHeight;

            if (centerX >= region.X + region.Width - Dip(45)
                && rowOffset >= Dip(27)
                && !LooksLikeConversationTime(text))
            {
                return "iconCandidate";
            }

            if (centerX < region.X + Dip(52))
                return "avatarVisual";

            if (rowOffset < Dip(31))
                return LooksLikeConversationTime(text) || centerX >= region.X + region.Width - Dip(78)
                    ? "time"
                    : "title";

            return "preview";
        }

        if (string.Equals(region.Id, "messageList", StringComparison.Ordinal))
        {
            if (LooksLikeVoiceDuration(text))
                return "voiceDuration";

            if (LooksLikeMessageSystemTime(text, centerX, region, scale))
                return "systemTime";

            if (centerX < region.X + Dip(64))
                return "avatarVisual";

            return "messageText";
        }

        return "text";
    }

    private static bool LooksLikeVoiceDuration(string text)
        => Regex.IsMatch(
            text.Trim(),
            @"^\d{1,3}\s*(?:[″""”']|秒)$",
            RegexOptions.CultureInvariant);

    private static bool LooksLikeMessageSystemTime(string text, double centerX, LayoutRegion region, double scale)
    {
        var nearCenter = Math.Abs(centerX - (region.X + region.Width / 2d)) <= 115d * scale;
        if (!nearCenter)
            return false;

        return Regex.IsMatch(
            text.Trim(),
            @"^(?:(?:\d{1,2}月\d{1,2}日\s*)?(?:星期[一二三四五六日天]\s*)?\d{1,2}:\d{2}|\d{1,2}月\d{1,2}日|昨天|今天|星期[一二三四五六日天])$",
            RegexOptions.CultureInvariant);
    }

    private static object[] BuildMessageObjects(
        Bitmap bitmap,
        LayoutRegion region,
        IReadOnlyList<OcrLineInfo> lines,
        double scale)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var result = new List<object>();
        var avatarBounds = DetectIncomingAvatarBounds(bitmap, region, scale);

        var avatarIndex = 0;
        foreach (var bounds in avatarBounds)
        {
            result.Add(new
            {
                id = $"message.avatar.incoming.{avatarIndex++}",
                kind = "avatar.incoming",
                label = "聊天对象头像",
                bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height },
                confidence = 0.82
            });
        }

        var voiceIndex = 0;
        foreach (var line in lines.Where(line =>
                     line.X + line.Width / 2d >= region.X
                     && line.X + line.Width / 2d < region.X + region.Width
                     && line.Y + line.Height / 2d >= region.Y
                     && line.Y + line.Height / 2d < region.Y + region.Height
                     && LooksLikeVoiceDuration(line.Text)))
        {
            var incoming = line.X + line.Width / 2d < region.X + region.Width * 0.62;
            var y = Math.Max(region.Y, (int)Math.Round(line.Y - Dip(12)));
            var h = Dip(42);
            Rectangle bounds;
            if (incoming)
            {
                bounds = ClampRect(
                    new Rectangle(
                        region.X + Dip(62),
                        y,
                        Math.Min(Dip(168), region.Width - Dip(68)),
                        h),
                    bitmap.Size);
            }
            else
            {
                bounds = ClampRect(
                    new Rectangle(
                        Math.Max(region.X, (int)Math.Round(line.X - Dip(135))),
                        y,
                        Dip(165),
                        h),
                    bitmap.Size);
            }

            var seconds = Regex.Match(line.Text, @"\d{1,3}").Value;
            result.Add(new
            {
                id = $"message.voice.{voiceIndex++}",
                kind = "message.voice",
                label = "语音消息",
                direction = incoming ? "incoming" : "outgoing",
                durationSeconds = int.TryParse(seconds, out var parsed) ? parsed : (int?)null,
                bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height },
                sourceText = line.Text,
                confidence = 0.9
            });
        }

        var systemTimeIndex = 0;
        foreach (var line in lines.Where(line =>
                     line.X + line.Width / 2d >= region.X
                     && line.X + line.Width / 2d < region.X + region.Width
                     && line.Y + line.Height / 2d >= region.Y
                     && line.Y + line.Height / 2d < region.Y + region.Height
                     && LooksLikeMessageSystemTime(line.Text, line.X + line.Width / 2d, region, scale)))
        {
            result.Add(new
            {
                id = $"message.systemTime.{systemTimeIndex++}",
                kind = "message.systemTime",
                label = "系统时间",
                text = line.Text,
                bounds = new
                {
                    x = (int)Math.Round(line.X),
                    y = (int)Math.Round(line.Y),
                    width = (int)Math.Round(line.Width),
                    height = (int)Math.Round(line.Height)
                },
                confidence = 0.95
            });
        }

        return result.ToArray();
    }

    private static Rectangle[] DetectIncomingAvatarBounds(Bitmap bitmap, LayoutRegion region, double scale)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var strip = ClampRect(
            new Rectangle(region.X + Dip(12), region.Y + Dip(2), Dip(48), Math.Max(1, region.Height - Dip(4))),
            bitmap.Size);
        var backgroundProbe = ClampRect(
            new Rectangle(region.X + Dip(2), region.Y + Dip(6), Dip(8), Math.Max(1, region.Height - Dip(12))),
            bitmap.Size);
        var background = SampleAverageColor(bitmap, backgroundProbe);

        var spans = new List<(int Start, int End)>();
        var start = -1;
        var lastActive = -1;
        var gap = 0;
        var maxGap = Dip(3);

        for (var y = strip.Top; y < strip.Bottom; y++)
        {
            var changed = 0;
            for (var x = strip.Left; x < strip.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var delta = Math.Abs(color.R - background.R)
                    + Math.Abs(color.G - background.G)
                    + Math.Abs(color.B - background.B);
                if (delta >= 58)
                    changed++;
            }

            var active = changed >= Math.Max(4, (int)Math.Round(strip.Width * 0.2));
            if (active)
            {
                if (start < 0)
                    start = y;
                lastActive = y;
                gap = 0;
            }
            else if (start >= 0)
            {
                gap++;
                if (gap > maxGap)
                {
                    spans.Add((start, lastActive));
                    start = -1;
                    lastActive = -1;
                    gap = 0;
                }
            }
        }

        if (start >= 0 && lastActive >= start)
            spans.Add((start, lastActive));

        return spans
            .Select(span => new Rectangle(
                strip.Left,
                span.Start,
                strip.Width,
                span.End - span.Start + 1))
            .Where(rect => rect.Height >= Dip(24) && rect.Height <= Dip(52))
            .ToArray();
    }

    private static bool ShouldIncludeInRegionText(string role)
        => role is "text" or "title" or "time" or "preview" or "messageText" or "systemTime";

    private static OcrLineInfo[] ExtractOcrLines(JsonElement ocr)
    {
        if (!ocr.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<OcrLineInfo>();
        foreach (var line in lines.EnumerateArray())
        {
            if (!TryReadOcrBox(line, out var x, out var y, out var width, out var height))
                continue;

            result.Add(new OcrLineInfo(
                ReadJsonString(line, "text") ?? string.Empty,
                x,
                y,
                width,
                height));
        }

        return result.ToArray();
    }

    private static object[] BuildConversationRows(
        Bitmap bitmap,
        LayoutRegion region,
        IReadOnlyList<OcrLineInfo> allLines,
        double scale)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var textLeft = region.X + Dip(55);
        var statusLeft = region.X + region.Width - Dip(29);
        var statusRight = region.X + region.Width - Dip(7);
        var rows = new List<ConversationRowAnalysis>();
        var anchors = DetectConversationRowAnchors(bitmap, region, scale);

        for (var index = 0; index < anchors.Length; index++)
        {
            var anchor = anchors[index];
            var top = anchor.RowBounds.Top;
            var bottom = anchor.RowBounds.Bottom;
            var height = anchor.RowBounds.Height;

            var rowLines = allLines
                .Where(line =>
                {
                    var centerX = line.X + line.Width / 2d;
                    var centerY = line.Y + line.Height / 2d;
                    return centerX >= region.X
                        && centerX < region.X + region.Width
                        && centerY >= top
                        && centerY < bottom;
                })
                .OrderBy(line => line.Y)
                .ThenBy(line => line.X)
                .ToArray();

            var upperSplit = top + height * 0.49;
            var lowerStart = top + Math.Min(height * 0.38, Dip(25));
            var upper = rowLines
                .Where(line => line.X >= textLeft - Dip(3) && line.Y + line.Height / 2d < upperSplit)
                .ToArray();
            var lower = rowLines
                .Where(line =>
                    line.X >= textLeft - Dip(3)
                    && line.Y + line.Height / 2d >= lowerStart
                    && line.X < statusLeft + Dip(2))
                .ToArray();

            var time = string.Empty;
            var titleParts = new List<string>();
            foreach (var line in upper)
            {
                var text = line.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var split = SplitTrailingConversationTime(text);
                if (!string.IsNullOrWhiteSpace(split.Time))
                {
                    if (string.IsNullOrWhiteSpace(time))
                        time = split.Time;
                    if (!string.IsNullOrWhiteSpace(split.Remaining))
                        titleParts.Add(split.Remaining);
                    continue;
                }

                if (LooksLikeConversationTime(text) || line.X >= region.X + region.Width - Dip(78))
                {
                    if (string.IsNullOrWhiteSpace(time))
                        time = text;
                    continue;
                }

                titleParts.Add(text);
            }

            var title = string.Join(" ", titleParts).Trim();
            var previewParts = lower
                .Select(line => line.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Where(text => !LooksLikeIconOcrNoise(text))
                .ToArray();
            var preview = string.Join(" ", previewParts).Trim();

            // 头像是会话行的视觉锚点；即使某一帧 OCR 没读到文字，也保留该行，
            // 避免因为 OCR 波动造成会话列表结构抖动。
            var backgroundRect = ClampRect(
                new Rectangle(region.X + Dip(1), top + Dip(6), Dip(8), Math.Max(1, height - Dip(12))),
                bitmap.Size);
            var background = SampleAverageColor(bitmap, backgroundRect);

            var selected = background.G - background.R >= 48
                && background.G - background.B >= 28
                && background.G >= 90;
            var selectedConfidence = selected
                ? Math.Clamp((background.G - Math.Max(background.R, background.B)) / 95d, 0.55, 1)
                : Math.Clamp((30d - Math.Max(0, background.G - Math.Max(background.R, background.B))) / 30d, 0.25, 0.85);

            var unreadBox = ClampRect(
                new Rectangle(
                    anchor.AvatarBounds.Right - Dip(10),
                    anchor.AvatarBounds.Top - Dip(8),
                    Dip(20),
                    Dip(20)),
                bitmap.Size);
            var unreadRedRatio = CountWechatRedPixels(bitmap, unreadBox) / (double)Math.Max(1, unreadBox.Width * unreadBox.Height);
            var unread = unreadRedRatio >= 0.035;
            var unreadCount = ParseUnreadCount(preview);
            var unreadConfidence = unread
                ? Math.Clamp((unreadRedRatio - 0.02) / 0.09, 0.55, 1)
                : Math.Clamp((0.035 - unreadRedRatio) / 0.035, 0.35, 0.9);

            var muteBox = ClampRect(
                new Rectangle(
                    statusLeft,
                    top + Math.Max(Dip(24), height - Dip(38)),
                    Math.Max(1, statusRight - statusLeft),
                    Dip(32)),
                bitmap.Size);
            var muteShape = AnalyzeMuteIconShape(bitmap, muteBox, scale);
            var muted = muteShape.IsMatch;
            var mutedConfidence = muteShape.Confidence;

            rows.Add(new ConversationRowAnalysis
            {
                Index = index,
                X = anchor.RowBounds.X,
                Y = anchor.RowBounds.Y,
                Width = anchor.RowBounds.Width,
                Height = anchor.RowBounds.Height,
                AvatarBox = anchor.AvatarBounds,
                AvatarConfidence = anchor.Confidence,
                RowAnchorConfidence = anchor.Confidence,
                Title = title,
                Preview = preview,
                Time = time,
                Unread = unread,
                UnreadCount = unreadCount,
                UnreadConfidence = unreadConfidence,
                UnreadRedRatio = unreadRedRatio,
                Muted = muted,
                MutedConfidence = mutedConfidence,
                MuteShapeScore = muteShape.Score,
                MuteLargestComponent = muteShape.LargestPixels,
                MuteComponentWidth = muteShape.Width,
                MuteComponentHeight = muteShape.Height,
                MuteComponentCount = muteShape.Components,
                Selected = selected,
                SelectedConfidence = selectedConfidence,
                BackgroundR = background.R,
                BackgroundG = background.G,
                BackgroundB = background.B,
                UnreadBox = unreadBox,
                MuteBox = muteBox
            });
        }

        if (rows.Count == 0)
            return [];

        var baseline = MedianColor(rows
            .Where(row => !row.Selected)
            .Select(row => (row.BackgroundR, row.BackgroundG, row.BackgroundB))
            .ToArray());

        var pinPrefixActive = true;
        foreach (var row in rows)
        {
            var distance = Math.Abs(row.BackgroundR - baseline.R)
                + Math.Abs(row.BackgroundG - baseline.G)
                + Math.Abs(row.BackgroundB - baseline.B);
            row.BackgroundDistance = distance;

            var neutralBackground = Math.Max(row.BackgroundR, Math.Max(row.BackgroundG, row.BackgroundB))
                - Math.Min(row.BackgroundR, Math.Min(row.BackgroundG, row.BackgroundB)) <= 28;
            var pinStyle = !row.Selected && neutralBackground && distance >= 24;

            if (!pinPrefixActive || !pinStyle)
            {
                row.Pinned = false;
                row.PinnedConfidence = pinStyle ? 0.35 : 0.75;
                pinPrefixActive = false;
            }
            else
            {
                row.Pinned = true;
                row.PinnedConfidence = Math.Clamp((distance - 16) / 42d, 0.5, 0.95);
            }
        }

        return rows.Select(row => (object)new
        {
            index = row.Index,
            bounds = new { x = row.X, y = row.Y, width = row.Width, height = row.Height },
            avatar = new
            {
                bounds = new
                {
                    x = row.AvatarBox.X,
                    y = row.AvatarBox.Y,
                    width = row.AvatarBox.Width,
                    height = row.AvatarBox.Height
                },
                confidence = Math.Round(row.AvatarConfidence, 3),
                method = "visual_avatar_anchor"
            },
            title = row.Title,
            preview = row.Preview,
            time = row.Time,
            unread = row.Unread,
            unreadCount = row.UnreadCount,
            muted = row.Muted,
            pinned = row.Pinned,
            selected = row.Selected,
            confidence = new
            {
                rowAnchor = Math.Round(row.RowAnchorConfidence, 3),
                unread = Math.Round(row.UnreadConfidence, 3),
                muted = Math.Round(row.MutedConfidence, 3),
                pinned = Math.Round(row.PinnedConfidence, 3),
                selected = Math.Round(row.SelectedConfidence, 3)
            },
            evidence = new
            {
                unreadRedRatio = Math.Round(row.UnreadRedRatio, 4),
                muteShapeScore = Math.Round(row.MuteShapeScore, 4),
                muteLargestComponent = row.MuteLargestComponent,
                muteComponentWidth = row.MuteComponentWidth,
                muteComponentHeight = row.MuteComponentHeight,
                muteComponentCount = row.MuteComponentCount,
                background = new { r = row.BackgroundR, g = row.BackgroundG, b = row.BackgroundB },
                backgroundDistance = row.BackgroundDistance,
                methods = new
                {
                    row = "conversation_avatar_visual_anchor",
                    avatar = "conversation_avatar_visual_anchor",
                    unread = "avatar_badge_red_pixels",
                    muted = "mute_icon_connected_component_shape",
                    pinned = "top_prefix_background_style",
                    selected = "row_background_green_state"
                }
            },
            slots = new
            {
                unread = new { x = row.UnreadBox.X, y = row.UnreadBox.Y, width = row.UnreadBox.Width, height = row.UnreadBox.Height },
                muted = new { x = row.MuteBox.X, y = row.MuteBox.Y, width = row.MuteBox.Width, height = row.MuteBox.Height }
            }
        }).ToArray();
    }

    private static ConversationRowAnchor[] DetectConversationRowAnchors(
        Bitmap bitmap,
        LayoutRegion region,
        double scale)
    {
        int Dip(double value) => Math.Max(1, (int)Math.Round(value * scale));
        var regionBottom = Math.Min(bitmap.Height, region.Y + region.Height);
        var backgroundLeft = region.X + Dip(1);
        var backgroundRight = Math.Min(region.X + region.Width, region.X + Dip(7));
        var avatarScanLeft = region.X + Dip(10);
        var avatarScanRight = Math.Min(region.X + region.Width, region.X + Dip(54));
        var expectedAvatarBodyHeight = Dip(36);
        var avatarSize = Dip(40);
        var nominalRowHeight = Dip(65);
        var maxGap = Dip(2);

        var spans = new List<(int Start, int End, double Strength)>();
        int? spanStart = null;
        var lastActive = -1;
        var gap = 0;
        double strengthTotal = 0;
        var activeLines = 0;

        for (var y = region.Y; y < regionBottom; y++)
        {
            long br = 0, bg = 0, bb = 0;
            var backgroundCount = 0;
            for (var x = backgroundLeft; x < backgroundRight; x++)
            {
                var color = bitmap.GetPixel(x, y);
                br += color.R;
                bg += color.G;
                bb += color.B;
                backgroundCount++;
            }

            if (backgroundCount == 0)
                continue;

            var baseR = br / (double)backgroundCount;
            var baseG = bg / (double)backgroundCount;
            var baseB = bb / (double)backgroundCount;
            var changed = 0;
            var scanWidth = Math.Max(1, avatarScanRight - avatarScanLeft);

            for (var x = avatarScanLeft; x < avatarScanRight; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var delta = Math.Abs(color.R - baseR)
                    + Math.Abs(color.G - baseG)
                    + Math.Abs(color.B - baseB);
                if (delta >= 60)
                    changed++;
            }

            var active = changed >= Math.Max(Dip(7), (int)Math.Round(scanWidth * 0.20));
            if (active)
            {
                if (!spanStart.HasValue)
                    spanStart = y;
                lastActive = y;
                gap = 0;
                strengthTotal += changed / (double)scanWidth;
                activeLines++;
            }
            else if (spanStart.HasValue)
            {
                gap++;
                if (gap > maxGap)
                {
                    spans.Add((
                        spanStart.Value,
                        lastActive,
                        activeLines == 0 ? 0 : strengthTotal / activeLines));
                    spanStart = null;
                    lastActive = -1;
                    gap = 0;
                    strengthTotal = 0;
                    activeLines = 0;
                }
            }
        }

        if (spanStart.HasValue && lastActive >= spanStart.Value)
        {
            spans.Add((
                spanStart.Value,
                lastActive,
                activeLines == 0 ? 0 : strengthTotal / activeLines));
        }

        var candidates = spans
            .Select(span =>
            {
                var visibleHeight = span.End - span.Start + 1;
                var centerY = (span.Start + span.End) / 2d;
                return new
                {
                    span.Start,
                    span.End,
                    VisibleHeight = visibleHeight,
                    CenterY = centerY,
                    span.Strength
                };
            })
            // 顶部/底部被滚动裁掉的头像通常只剩几像素到二十多像素；
            // 只把接近完整头像主体高度的候选作为会话锚点。
            .Where(item => item.VisibleHeight >= Dip(28) && item.VisibleHeight <= Dip(48))
            .OrderBy(item => item.CenterY)
            .ToArray();

        if (candidates.Length == 0)
            return [];

        var anchors = new List<ConversationRowAnchor>();
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            var top = i == 0
                ? (int)Math.Round(candidate.CenterY - nominalRowHeight / 2d)
                : (int)Math.Round((candidates[i - 1].CenterY + candidate.CenterY) / 2d);
            var bottom = i == candidates.Length - 1
                ? (int)Math.Round(candidate.CenterY + nominalRowHeight / 2d)
                : (int)Math.Round((candidate.CenterY + candidates[i + 1].CenterY) / 2d);

            // 只保留完整出现在可视会话区里的行。
            if (top < region.Y || bottom > regionBottom)
                continue;

            var rowBounds = new Rectangle(
                region.X,
                top,
                region.Width,
                Math.Max(Dip(50), bottom - top));

            var avatarBounds = ClampRect(
                new Rectangle(
                    region.X + Dip(12),
                    (int)Math.Round(candidate.CenterY - avatarSize / 2d),
                    avatarSize,
                    avatarSize),
                bitmap.Size);

            var heightScore = Math.Clamp(
                1d - Math.Abs(candidate.VisibleHeight - expectedAvatarBodyHeight) / (double)Math.Max(1, Dip(18)),
                0,
                1);
            var strengthScore = Math.Clamp((candidate.Strength - 0.18) / 0.48, 0, 1);
            var confidence = Math.Clamp(0.68 + heightScore * 0.20 + strengthScore * 0.12, 0.68, 0.99);

            anchors.Add(new ConversationRowAnchor(
                rowBounds,
                avatarBounds,
                candidate.CenterY,
                confidence));
        }

        return anchors.ToArray();
    }
    private static (string Remaining, string Time) SplitTrailingConversationTime(string text)
    {
        var match = Regex.Match(
            text,
            @"^(?<body>.*?)(?<time>(?:昨天\s*)?\d{1,2}:\d{2}|星期[一二三四五六日天]|周[一二三四五六日天]|\d{1,2}/\d{1,2})$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return (text, string.Empty);

        return (match.Groups["body"].Value.Trim(), match.Groups["time"].Value.Trim());
    }

    private static bool LooksLikeConversationTime(string text)
        => Regex.IsMatch(
            text.Trim(),
            @"^(?:(?:昨天\s*)?\d{1,2}:\d{2}|星期[一二三四五六日天]|周[一二三四五六日天]|\d{1,2}/\d{1,2})$",
            RegexOptions.CultureInvariant);

    private static bool LooksLikeIconOcrNoise(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 4)
            return false;
        if (LooksLikeConversationTime(trimmed))
            return false;
        if (Regex.IsMatch(trimmed, @"^[\p{IsCJKUnifiedIdeographs}A-Za-z0-9]+$", RegexOptions.CultureInvariant))
            return false;
        return true;
    }

    private static int? ParseUnreadCount(string preview)
    {
        var match = Regex.Match(preview, @"^\[(?<count>\d+)条\]", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["count"].Value, out var count) ? count : null;
    }

    private static Rectangle ClampRect(Rectangle rect, Size bounds)
    {
        var left = Math.Clamp(rect.Left, 0, Math.Max(0, bounds.Width - 1));
        var top = Math.Clamp(rect.Top, 0, Math.Max(0, bounds.Height - 1));
        var right = Math.Clamp(rect.Right, left + 1, bounds.Width);
        var bottom = Math.Clamp(rect.Bottom, top + 1, bounds.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static (int R, int G, int B) SampleAverageColor(Bitmap bitmap, Rectangle rect)
    {
        long r = 0, g = 0, b = 0, count = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                r += color.R;
                g += color.G;
                b += color.B;
                count++;
            }
        }

        if (count == 0)
            return (0, 0, 0);
        return ((int)(r / count), (int)(g / count), (int)(b / count));
    }

    private static int CountWechatRedPixels(Bitmap bitmap, Rectangle rect)
    {
        var count = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.R >= 175
                    && color.G <= 115
                    && color.B <= 115
                    && color.R - color.G >= 75
                    && color.R - color.B >= 75)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static MuteShapeAnalysis AnalyzeMuteIconShape(Bitmap bitmap, Rectangle rect, double scale)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return new MuteShapeAnalysis(false, 0, 0, 0, 0, 0, 0.92);

        var normalizedWidth = Math.Max(1, (int)Math.Round(rect.Width / Math.Max(0.5, scale)));
        var normalizedHeight = Math.Max(1, (int)Math.Round(rect.Height / Math.Max(0.5, scale)));

        var reds = new List<int>(rect.Width * rect.Height);
        var greens = new List<int>(rect.Width * rect.Height);
        var blues = new List<int>(rect.Width * rect.Height);
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                reds.Add(color.R);
                greens.Add(color.G);
                blues.Add(color.B);
            }
        }

        static int Median(List<int> values)
        {
            values.Sort();
            return values.Count == 0 ? 0 : values[values.Count / 2];
        }

        var backgroundR = Median(reds);
        var backgroundG = Median(greens);
        var backgroundB = Median(blues);
        var mask = new bool[normalizedHeight, normalizedWidth];

        for (var ny = 0; ny < normalizedHeight; ny++)
        {
            var sourceY = rect.Top + Math.Clamp(
                (int)Math.Round((ny + 0.5) * rect.Height / normalizedHeight - 0.5),
                0,
                rect.Height - 1);

            for (var nx = 0; nx < normalizedWidth; nx++)
            {
                var sourceX = rect.Left + Math.Clamp(
                    (int)Math.Round((nx + 0.5) * rect.Width / normalizedWidth - 0.5),
                    0,
                    rect.Width - 1);

                var color = bitmap.GetPixel(sourceX, sourceY);
                var delta = Math.Abs(color.R - backgroundR)
                    + Math.Abs(color.G - backgroundG)
                    + Math.Abs(color.B - backgroundB);
                mask[ny, nx] = delta >= 45;
            }
        }

        var seen = new bool[normalizedHeight, normalizedWidth];
        var components = 0;
        var largestPixels = 0;
        var largestWidth = 0;
        var largestHeight = 0;

        for (var sy = 0; sy < normalizedHeight; sy++)
        {
            for (var sx = 0; sx < normalizedWidth; sx++)
            {
                if (!mask[sy, sx] || seen[sy, sx])
                    continue;

                components++;
                var queue = new Queue<Point>();
                queue.Enqueue(new Point(sx, sy));
                seen[sy, sx] = true;

                var pixels = 0;
                var minX = sx;
                var maxX = sx;
                var minY = sy;
                var maxY = sy;

                while (queue.Count > 0)
                {
                    var point = queue.Dequeue();
                    pixels++;
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;

                            var nx = point.X + dx;
                            var ny = point.Y + dy;
                            if (nx < 0 || nx >= normalizedWidth || ny < 0 || ny >= normalizedHeight)
                                continue;
                            if (!mask[ny, nx] || seen[ny, nx])
                                continue;

                            seen[ny, nx] = true;
                            queue.Enqueue(new Point(nx, ny));
                        }
                    }
                }

                if (pixels > largestPixels)
                {
                    largestPixels = pixels;
                    largestWidth = maxX - minX + 1;
                    largestHeight = maxY - minY + 1;
                }
            }
        }

        var isMatch = largestPixels >= 28
            && largestWidth is >= 9 and <= 15
            && largestHeight is >= 5 and <= 15;

        var pixelScore = Math.Clamp((largestPixels - 20) / 45d, 0, 1);
        var widthScore = Math.Clamp(1d - Math.Abs(largestWidth - 11) / 8d, 0, 1);
        var heightScore = Math.Clamp(1d - Math.Abs(largestHeight - 9) / 10d, 0, 1);
        var score = 0.45 * pixelScore + 0.35 * widthScore + 0.20 * heightScore;
        if (!isMatch)
            score = Math.Min(score, 0.59);

        var confidence = isMatch
            ? Math.Clamp(0.72 + score * 0.28, 0.72, 1)
            : Math.Clamp(0.55 + (1 - score) * 0.35, 0.55, 0.92);

        return new MuteShapeAnalysis(
            isMatch,
            score,
            largestPixels,
            largestWidth,
            largestHeight,
            components,
            confidence);
    }

    private static double ComputeMuteTemplateScore(Bitmap bitmap, Rectangle rect, double scale)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return 0;

        var normalizedWidth = Math.Max(MuteIconTemplate[0].Length, (int)Math.Round(rect.Width / Math.Max(0.5, scale)));
        var normalizedHeight = Math.Max(MuteIconTemplate.Length, (int)Math.Round(rect.Height / Math.Max(0.5, scale)));

        var reds = new List<int>(rect.Width * rect.Height);
        var greens = new List<int>(rect.Width * rect.Height);
        var blues = new List<int>(rect.Width * rect.Height);
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var color = bitmap.GetPixel(x, y);
                reds.Add(color.R);
                greens.Add(color.G);
                blues.Add(color.B);
            }
        }

        static int Median(List<int> values)
        {
            values.Sort();
            return values.Count == 0 ? 0 : values[values.Count / 2];
        }

        var backgroundR = Median(reds);
        var backgroundG = Median(greens);
        var backgroundB = Median(blues);
        var mask = new bool[normalizedHeight, normalizedWidth];

        for (var ny = 0; ny < normalizedHeight; ny++)
        {
            var sourceY = rect.Top + Math.Clamp(
                (int)Math.Round((ny + 0.5) * rect.Height / normalizedHeight - 0.5),
                0,
                rect.Height - 1);

            for (var nx = 0; nx < normalizedWidth; nx++)
            {
                var sourceX = rect.Left + Math.Clamp(
                    (int)Math.Round((nx + 0.5) * rect.Width / normalizedWidth - 0.5),
                    0,
                    rect.Width - 1);

                var color = bitmap.GetPixel(sourceX, sourceY);
                var delta = Math.Abs(color.R - backgroundR)
                    + Math.Abs(color.G - backgroundG)
                    + Math.Abs(color.B - backgroundB);
                mask[ny, nx] = delta >= 45;
            }
        }

        var templateHeight = MuteIconTemplate.Length;
        var templateWidth = MuteIconTemplate[0].Length;
        var templatePixels = MuteIconTemplate.Sum(row => row.Count(ch => ch == '#'));
        var best = 0d;

        for (var offsetY = 0; offsetY <= normalizedHeight - templateHeight; offsetY++)
        {
            for (var offsetX = 0; offsetX <= normalizedWidth - templateWidth; offsetX++)
            {
                var intersection = 0;
                var candidatePixels = 0;

                for (var y = 0; y < templateHeight; y++)
                {
                    for (var x = 0; x < templateWidth; x++)
                    {
                        var candidate = mask[offsetY + y, offsetX + x];
                        if (candidate)
                            candidatePixels++;

                        if (candidate && MuteIconTemplate[y][x] == '#')
                            intersection++;
                    }
                }

                if (candidatePixels == 0)
                    continue;

                var score = 2d * intersection / (templatePixels + candidatePixels);
                if (score > best)
                    best = score;
            }
        }

        return best;
    }

    private static int CountVisualEdges(Bitmap bitmap, Rectangle rect)
    {
        var count = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right - 1; x++)
            {
                var first = bitmap.GetPixel(x, y);
                var second = bitmap.GetPixel(x + 1, y);
                var delta = Math.Abs(first.R - second.R)
                    + Math.Abs(first.G - second.G)
                    + Math.Abs(first.B - second.B);
                if (delta >= 45)
                    count++;
            }
        }
        return count;
    }

    private static (int R, int G, int B) MedianColor((int R, int G, int B)[] colors)
    {
        if (colors.Length == 0)
            return (0, 0, 0);

        static int Median(int[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        return (
            Median(colors.Select(color => color.R).ToArray()),
            Median(colors.Select(color => color.G).ToArray()),
            Median(colors.Select(color => color.B).ToArray()));
    }

    private static bool TryReadOcrBox(JsonElement line, out double x, out double y, out double width, out double height)
    {
        x = y = width = height = 0;
        if (!line.TryGetProperty("box", out var box) || box.ValueKind != JsonValueKind.Object)
            return false;
        return TryReadJsonDouble(box, "x", out x)
            && TryReadJsonDouble(box, "y", out y)
            && TryReadJsonDouble(box, "width", out width)
            && TryReadJsonDouble(box, "height", out height);
    }

    private static bool TryReadJsonDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value);
    }

    private static double? ReadJsonDouble(JsonElement element, string name)
        => TryReadJsonDouble(element, name, out var value) ? value : null;

    private static int? ReadJsonInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;

    private static string? ReadJsonString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record VisualFingerprint(
        string RegionId,
        int Width,
        int Height,
        int Step,
        byte[] Samples);

    private sealed record LayoutRegionChange(
        string RegionId,
        double Ratio,
        double Threshold,
        bool Changed);

    private sealed record LayoutChangeSummary(
        bool Compatible,
        bool HasMeaningfulChange,
        string Reason,
        LayoutRegionChange[] Regions);

    private sealed record LayoutAnalysisCache(
        IntPtr Handle,
        int Width,
        int Height,
        uint Dpi,
        DateTimeOffset AnalyzedAtUtc,
        string CapturePath,
        Dictionary<string, VisualFingerprint> Fingerprints,
        JsonElement Regions,
        JsonElement UiObjects,
        JsonElement Navigation,
        JsonElement Conversations,
        JsonElement MessageObjects,
        JsonElement Lines,
        string Text,
        int DetectedCount,
        double? OcrElapsedMs);
    private sealed record MuteShapeAnalysis(
        bool IsMatch,
        double Score,
        int LargestPixels,
        int Width,
        int Height,
        int Components,
        double Confidence);

    private sealed record NavigationItemAnalysis(
        string Id,
        string Label,
        string Kind,
        Rectangle Bounds,
        double GreenRatio,
        double RedRatio,
        bool Active,
        double Confidence);

    private sealed record NavigationAnalysis(
        string Active,
        string ActiveLabel,
        bool IsChatView,
        double Confidence,
        Rectangle ProfileBounds,
        double ProfileConfidence,
        NavigationItemAnalysis[] Items);
    private sealed record VisualObject(
        string Id,
        string Kind,
        string Label,
        string RegionId,
        Rectangle Bounds)
    {
        public bool Contains(double x, double y)
            => x >= Bounds.Left && x < Bounds.Right && y >= Bounds.Top && y < Bounds.Bottom;
    }

    private sealed record OcrLineInfo(string Text, double X, double Y, double Width, double Height);

    private sealed record ConversationRowAnchor(
        Rectangle RowBounds,
        Rectangle AvatarBounds,
        double CenterY,
        double Confidence);

    private sealed class ConversationRowAnalysis
    {
        public int Index { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public Rectangle AvatarBox { get; init; }
        public double AvatarConfidence { get; init; }
        public double RowAnchorConfidence { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Preview { get; init; } = string.Empty;
        public string Time { get; init; } = string.Empty;
        public bool Unread { get; init; }
        public int? UnreadCount { get; init; }
        public double UnreadConfidence { get; init; }
        public double UnreadRedRatio { get; init; }
        public bool Muted { get; init; }
        public double MutedConfidence { get; init; }
        public double MuteShapeScore { get; init; }
        public int MuteLargestComponent { get; init; }
        public int MuteComponentWidth { get; init; }
        public int MuteComponentHeight { get; init; }
        public int MuteComponentCount { get; init; }
        public bool Selected { get; init; }
        public double SelectedConfidence { get; init; }
        public bool Pinned { get; set; }
        public double PinnedConfidence { get; set; }
        public int BackgroundR { get; init; }
        public int BackgroundG { get; init; }
        public int BackgroundB { get; init; }
        public int BackgroundDistance { get; set; }
        public Rectangle UnreadBox { get; init; }
        public Rectangle MuteBox { get; init; }
    }

    private sealed record LayoutRegion(string Id, string Label, int X, int Y, int Width, int Height)
    {
        public bool Contains(double x, double y)
            => x >= X && x < X + Width && y >= Y && y < Y + Height;
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
