using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OpenQuickHost.Sync;
using Point = System.Windows.Point;

namespace OpenQuickHost;

/// <summary>
/// 全局鼠标手势服务。监听右键 / 中键的 down → drag → up，
/// 松开后优先匹配扩展 manifest 里的 mouseGesture.data 模板，旧配置回退到 mouseGesture.sequence。
///
/// 设计要点：
/// - 跟 YarnSelectService（按住左键 + 字母触发）正交：本服务在没按左键时才工作。
/// - 跟 QuickPanelMouseTriggers（长按右键打开面板）会同时收到右键事件；
///   本服务会拦截目标窗口的原始右键 down/up，普通短右击再用 SendInput 重放，
///   避免手势结束时额外弹出系统右键菜单。
/// - 序列匹配后用 Process.Start / 内联脚本运行扩展，复用 LocalExtensionCatalog.CreateCommand 出来的 CommandItem。
/// </summary>
public static class MouseGestureService
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseMove = 0x0200;
    private const int WmMouseWheel = 0x020A;
    private const uint LlInjected = 0x00000001;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkMenu = 0x12;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkTab = 0x09;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;
    private const ushort VkBrowserBack = 0xA6;
    private const ushort VkBrowserForward = 0xA7;
    private const uint MouseeventfLeftDown = 0x0002;
    private const uint MouseeventfLeftUp = 0x0004;
    private const uint MouseeventfRightDown = 0x0008;
    private const uint MouseeventfRightUp = 0x0010;
    private const uint MouseeventfMiddleDown = 0x0020;
    private const uint MouseeventfMiddleUp = 0x0040;

    private static readonly IntPtr SyntheticExtraInfo = (IntPtr)0x59414E5A; // "YANZ"

    /// <summary>手势服务合成事件标记，供其它钩子服务互认过滤。</summary>
    internal static IntPtr GestureSyntheticMarker => SyntheticExtraInfo;
    private static readonly LowLevelMouseProc MouseProc = HookCallback;
    private static IntPtr _hookId;
    private static bool _isRunning;

    // 当前拖动状态
    private static bool _leftDown;
    private static bool _rightDown;
    private static bool _middleDown;
    private static bool _ctrlLeftDown;
    private static Point _downPoint;
    private static readonly List<Point> _path = new(capacity: 256);
    private static bool _suppressNextRightUp;
    private static bool _suppressNextMiddleUp;
    private static bool _suppressNextLeftUp;
    private static bool _gestureActionTriggered;
    private static bool _traceActive;
    private static bool _gesturePreviewMatched;
    private static MouseGesturePreviewInfo? _lastPreviewInfo;
    private static MouseGestureTraceWindow? _traceWindow;

    // 注册表：trigger -> 模板 + sequence fallback
    private static readonly Dictionary<string, GestureTriggerRegistry> _registry = new(StringComparer.Ordinal);

    private const int MinDragDistance = 30;       // 触发手势识别的最小总位移
    private const int MinSegmentDistance = 30;    // 单段最短距离
    private const double TraceStartDistance = 6;
    private const double TwoPi = Math.PI * 2;
    private const double EightthPi = Math.PI / 4;
    private static readonly char[] Arrows = { '→','↘','↓','↙','←','↖','↑','↗' };

    private static Action<string, string>? _onLog;

    public static bool IsRunning => _isRunning;

    public static bool HasRightDragRegistrations
    {
        get
        {
            var activeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(AppSettingsStore.LoadCached().MouseGestureTriggerMode);
            return string.Equals(activeTrigger, "right-drag", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool HasMiddleDragRegistrations
    {
        get
        {
            var activeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(AppSettingsStore.LoadCached().MouseGestureTriggerMode);
            return string.Equals(activeTrigger, "middle-drag", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool HasCtrlLeftDragRegistrations
    {
        get
        {
            var activeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(AppSettingsStore.LoadCached().MouseGestureTriggerMode);
            return string.Equals(activeTrigger, "ctrl-left-drag", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 取消当前活跃的手势识别与画线轨迹（用于背包、燕环、燕幕长按或浮窗激活时的互斥抢占）
    /// </summary>
    public static void CancelActiveGesture()
    {
        ResetState();
    }

    /// <summary>
    /// 启动全局 hook。<paramref name="logger"/> 用于把 (level, message) 输出到调用方日志（一般是 HostAssets）。
    /// </summary>
    public static void Start(Action<string, string>? logger = null)
    {
        if (_isRunning) return;
        _onLog = logger;
        OverlayWindowManager.RegisterSuppressionHandler(CancelActiveGesture);
        _hookId = SetMouseHook(MouseProc);
        if (_hookId == IntPtr.Zero)
        {
            Log("warn", $"hook install failed: error={Marshal.GetLastWin32Error()}");
            return;
        }
        _isRunning = true;
        Log("info", "started.");
    }

    public static void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _isRunning = false;
        ResetState();
        Log("info", "stopped.");
    }

    /// <summary>
    /// 无感重装/自愈鼠标手势底层钩子（在系统休眠唤醒、锁屏解锁或看门狗触发时调用）
    /// </summary>
    public static void RestartHook(string reason = "watchdog")
    {
        try
        {
            if (!_isRunning) return;
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            ResetState();
            _hookId = SetMouseHook(MouseProc);
            Log("info", $"Mouse gesture hook auto-reinstalled successfully due to {reason}. hookId=0x{_hookId.ToInt64():X}");
        }
        catch (Exception ex)
        {
            Log("warn", $"failed to restart mouse gesture hook on {reason}: {ex.Message}");
        }
    }

    /// <summary>
    /// 立即取消当前正在进行的手势画线或捕获状态（Rocker 取消或 ESC 键触发）
    /// </summary>
    public static void CancelCurrentGesture(string reason = "escape")
    {
        if (_rightDown || _middleDown || _ctrlLeftDown || _traceActive)
        {
            _gestureActionTriggered = true;
            _suppressNextRightUp = true;
            _suppressNextMiddleUp = true;
            _suppressNextLeftUp = true;
            _rightDown = false;
            _middleDown = false;
            _ctrlLeftDown = false;
            _path.Clear();
            _traceActive = false;
            CancelTrace();
            Log("info", $"Gesture cancelled due to {reason}.");
        }
    }

    /// <summary>
    /// 重新构建注册表。<paramref name="gestures"/> 来自所有扩展 manifest 的 MouseGesture 字段。
    /// </summary>
    public static void ReloadRegistrations(IEnumerable<RegisteredGesture> gestures)
    {
        _registry.Clear();
        var activeRuntimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(AppSettingsStore.Load().MouseGestureTriggerMode);
        if (!string.IsNullOrWhiteSpace(activeRuntimeTrigger))
        {
            _registry[NormalizeTrigger(activeRuntimeTrigger)] = new GestureTriggerRegistry();
        }
        var count = 0;
        foreach (var g in gestures)
        {
            if (string.IsNullOrWhiteSpace(g.Sequence) && !MouseGestureTemplateRecognizer.HasTemplateData(g.Data)) continue;
            var trigger = NormalizeTrigger(g.Trigger);
            if (!_registry.TryGetValue(trigger, out var triggerRegistry))
            {
                triggerRegistry = new GestureTriggerRegistry();
                _registry[trigger] = triggerRegistry;
            }

            if (MouseGestureTemplateRecognizer.HasTemplateData(g.Data))
            {
                triggerRegistry.Templates.Add(g);
            }

            if (!string.IsNullOrWhiteSpace(g.Sequence))
            {
                if (!triggerRegistry.SequenceMap.TryGetValue(g.Sequence, out var owners))
                {
                    owners = new List<RegisteredGesture>();
                    triggerRegistry.SequenceMap[g.Sequence] = owners;
                }

                owners.Add(g);
            }
            count++;
        }
        Log("info", $"registry reloaded: {count} gesture(s) across {_registry.Count} trigger(s).");
    }

    public static void ClearRegistrations()
    {
        _registry.Clear();
    }

    /// <summary>从扩展目录扫描注册表。返回注册个数。</summary>
    public static int ReloadFromCatalog(Action<RegisteredGesture> onExecute)
    {
        var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(AppSettingsStore.Load().MouseGestureTriggerMode);
        if (string.IsNullOrWhiteSpace(runtimeTrigger))
        {
            ReloadRegistrations([]);
            return 0;
        }

        var entries = LocalExtensionCatalog.LoadEntries();
        var appBindings = AppSettingsStore.Load().MouseGestureAppBindings;

        // 按手势 sequence 组织白名单（限定应用）与黑名单（禁用应用）
        var whitelistBySeq = appBindings
            .Where(b => !b.IsBlacklist && !string.IsNullOrWhiteSpace(b.Sequence) && !string.IsNullOrWhiteSpace(b.AppPath))
            .GroupBy(b => MouseGestureNaming.NormalizeSequence(b.Sequence), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AppPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        var blacklistBySeq = appBindings
            .Where(b => b.IsBlacklist && !string.IsNullOrWhiteSpace(b.Sequence) && !string.IsNullOrWhiteSpace(b.AppPath))
            .GroupBy(b => MouseGestureNaming.NormalizeSequence(b.Sequence), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AppPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        var registrations = new List<RegisteredGesture>();
        foreach (var entry in entries)
        {
            var m = entry.Manifest;
            var g = m.MouseGesture;
            if (g == null || (string.IsNullOrWhiteSpace(g.Sequence) && !MouseGestureTemplateRecognizer.HasTemplateData(g.Data))) continue;

            var normSeq = MouseGestureNaming.NormalizeSequence(g.Sequence);
            IReadOnlyList<string>? targetApps = whitelistBySeq.TryGetValue(normSeq, out var w) && w.Count > 0 ? w : null;
            IReadOnlyList<string>? excludedApps = blacklistBySeq.TryGetValue(normSeq, out var b) && b.Count > 0 ? b : null;

            registrations.Add(new RegisteredGesture(
                ExtensionId: m.Id,
                ExtensionName: m.Name,
                Trigger: runtimeTrigger,
                Sequence: g.Sequence,
                Sign: string.IsNullOrWhiteSpace(g.Sign) ? g.Sequence : g.Sign,
                IconReference: m.Icon,
                ExtensionDirectoryPath: Path.GetDirectoryName(entry.ManifestPath),
                DisplayGlyph: BuildFallbackGlyph(m.Name),
                Data: g.Data,
                Tolerance: g.Tolerance,
                MinDistance: Math.Max(8, g.MinDistance ?? MinSegmentDistance),
                Execute: onExecute,
                TargetAppPaths: targetApps,
                ExcludedAppPaths: excludedApps));
        }

        // 针对未绑定小程序、仅绑定了应用的手势进行注册
        foreach (var group in whitelistBySeq)
        {
            if (registrations.Any(r => string.Equals(MouseGestureNaming.NormalizeSequence(r.Sequence), group.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var firstBind = appBindings.FirstOrDefault(b => !b.IsBlacklist && string.Equals(MouseGestureNaming.NormalizeSequence(b.Sequence), group.Key, StringComparison.OrdinalIgnoreCase));
            var appName = firstBind?.AppName ?? "App";
            IReadOnlyList<string>? excludedApps = blacklistBySeq.TryGetValue(group.Key, out var b) && b.Count > 0 ? b : null;

            registrations.Add(new RegisteredGesture(
                ExtensionId: "app:" + (firstBind?.AppPath ?? string.Empty),
                ExtensionName: appName,
                Trigger: runtimeTrigger,
                Sequence: group.Key,
                Sign: group.Key,
                IconReference: firstBind?.AppPath,
                ExtensionDirectoryPath: null,
                DisplayGlyph: BuildFallbackGlyph(appName),
                Data: null,
                Tolerance: null,
                MinDistance: MinSegmentDistance,
                Execute: onExecute,
                TargetAppPaths: group.Value,
                ExcludedAppPaths: excludedApps));
        }

        ReloadRegistrations(registrations);
        return registrations.Count;
    }

    // 钩子回调由 user32 反向调用：异常逃出回调边界会直接进程 fail-fast，外层兜底后放行事件。
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HookCallbackCore(nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            Log("error", $"HookCallback exception: {ex.Message}");
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    private static unsafe IntPtr HookCallbackCore(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var msg = (int)wParam;

        // 1. WM_MOUSEMOVE 极速短路：未在画手势时，1 纳秒内放行
        if (msg == WmMouseMove && !_rightDown && !_middleDown && !_ctrlLeftDown)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // 2. Unsafe 零分配指针读取
        var data = *(MSLLHOOKSTRUCT*)lParam;
        // 过滤燕子自身与 InputHookService 的合成重放事件（两个服务互认，避免把对方的
        // 重放当作用户输入再次触发状态机），允许 ToDesk、向日葵等远程控制软件注入的真实用户鼠标事件
        if (data.dwExtraInfo == SyntheticExtraInfo || data.dwExtraInfo == InputHookService.SyntheticInputMarker)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        switch (msg)
        {
            case WmLButtonDown:
                _leftDown = true;
                if (_rightDown)
                {
                    if (AppSettingsStore.LoadCached().MouseGestureEnableRockerActions && !ShouldBypassGesture(data.pt))
                    {
                        HandleRockerGesture("rocker-right-left", data.pt);
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }
                    else
                    {
                        // 摇摆取消（Rocker Escape）：按住右键时点击左键，立即安全撤销手势画线！
                        CancelCurrentGesture("rocker-left-click");
                        _suppressNextLeftUp = true;
                        _suppressNextRightUp = true;
                        return (IntPtr)1;
                    }
                }
                if (IsControlDown() && _registry.ContainsKey("ctrl-left-drag") && !ShouldBypassGesture(data.pt))
                {
                    BeginStroke(new Point(data.pt.x, data.pt.y), isRightButton: false, isCtrlLeft: true);
                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
                ResetState();
                break;

            case WmLButtonUp:
                _leftDown = false;
                if (_ctrlLeftDown)
                {
                    var inputTriggerWasActive = InputHookService.HasActiveMouseTrigger;
                    var wasDragIntent = IsDragIntent();
                    if (inputTriggerWasActive)
                    {
                        CancelStrokeForInputTrigger("ctrl-left-drag");
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    AppendPathPoint(new Point(data.pt.x, data.pt.y));
                    _ctrlLeftDown = false;
                    var matched = TryFinishStroke("ctrl-left-drag", out var matchedGesture, out var previewInfo);
                    FinishTrace(previewInfo, matched);
                    _path.Clear();
                    if (matched)
                    {
                        InputHookService.ResetMouseState("mouse gesture");
                        ExecuteAfterInputSettles(matchedGesture);
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    if (_gestureActionTriggered)
                    {
                        _gestureActionTriggered = false;
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    if (!wasDragIntent && !inputTriggerWasActive && !InputHookService.WasMouseTriggerReleasedRecently())
                    {
                        ReplayMouseClickAfterHookReturns(MouseeventfLeftDown, MouseeventfLeftUp, "left");
                    }

                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
                if (_suppressNextLeftUp)
                {
                    _suppressNextLeftUp = false;
                    return (IntPtr)1;
                }
                break;

            case WmRButtonDown:
                if (ShouldBypassGesture(data.pt))
                {
                    break;
                }

                if (_leftDown && AppSettingsStore.LoadCached().MouseGestureEnableRockerActions)
                {
                    HandleRockerGesture("rocker-left-right", data.pt);
                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }

                if (_registry.ContainsKey("right-drag"))
                {
                    BeginStroke(new Point(data.pt.x, data.pt.y), isRightButton: true);
                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
                break;

            case WmRButtonUp:
                if (_rightDown)
                {
                    var inputTriggerWasActive = InputHookService.HasActiveMouseTrigger;
                    var wasDragIntent = IsDragIntent();
                    if (inputTriggerWasActive)
                    {
                        CancelStrokeForInputTrigger("right-drag");
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    AppendPathPoint(new Point(data.pt.x, data.pt.y));
                    _rightDown = false;
                    var matched = TryFinishStroke("right-drag", out var matchedGesture, out var previewInfo);
                    FinishTrace(previewInfo, matched);
                    _path.Clear();
                    if (matched)
                    {
                        InputHookService.ResetMouseState("mouse gesture");
                        ExecuteAfterInputSettles(matchedGesture);
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    if (_gestureActionTriggered)
                    {
                        _gestureActionTriggered = false;
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    // 仅当用户纯粹在原地极短点击（未进入手势拖拽、位移小于起点距离阈值）时才重放短右击
                    if (!wasDragIntent && !inputTriggerWasActive && !InputHookService.WasMouseTriggerReleasedRecently())
                    {
                        ReplayMouseClickAfterHookReturns(MouseeventfRightDown, MouseeventfRightUp, "right");
                    }

                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
                if (_suppressNextRightUp)
                {
                    _suppressNextRightUp = false;
                    _gestureActionTriggered = false;
                    return (IntPtr)1;
                }
                break;

            case WmMButtonDown:
                if (ShouldBypassGesture(data.pt))
                {
                    break;
                }

                if (_registry.ContainsKey("middle-drag"))
                {
                    BeginStroke(new Point(data.pt.x, data.pt.y), isRightButton: false);
                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
                break;

            case WmMButtonUp:
                if (_middleDown)
                {
                    var inputTriggerWasActive = InputHookService.HasActiveMouseTrigger;
                    var wasDragIntent = IsDragIntent();
                    if (inputTriggerWasActive)
                    {
                        CancelStrokeForInputTrigger("middle-drag");
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    AppendPathPoint(new Point(data.pt.x, data.pt.y));
                    _middleDown = false;
                    var matched = TryFinishStroke("middle-drag", out var matchedGesture, out var previewInfo);
                    FinishTrace(previewInfo, matched);
                    _path.Clear();
                    if (matched)
                    {
                        InputHookService.ResetMouseState("mouse gesture");
                        ExecuteAfterInputSettles(matchedGesture);
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    if (_gestureActionTriggered)
                    {
                        _gestureActionTriggered = false;
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

                    if (!wasDragIntent && !inputTriggerWasActive && !InputHookService.WasMouseTriggerReleasedRecently())
                    {
                        ReplayMouseClickAfterHookReturns(MouseeventfMiddleDown, MouseeventfMiddleUp, "middle");
                    }

                    _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                    return (IntPtr)1;
                }
                if (_suppressNextMiddleUp)
                {
                    _suppressNextMiddleUp = false;
                    _gestureActionTriggered = false;
                    return (IntPtr)1;
                }
                break;

            case WmMouseWheel:
                if ((_rightDown || _middleDown || _ctrlLeftDown) && AppSettingsStore.LoadCached().MouseGestureEnableWheelActions && !ShouldBypassGesture(data.pt))
                {
                    var delta = (short)((data.mouseData >> 16) & 0xFFFF);
                    var action = delta > 0 ? "wheel-up" : "wheel-down";
                    HandleWheelGesture(action, data.pt);
                    return (IntPtr)1;
                }
                break;

            case WmMouseMove:
                if (_rightDown || _middleDown || _ctrlLeftDown)
                {
                    if (InputHookService.HasActiveMouseTrigger)
                    {
                        CancelStrokeForInputTrigger(_rightDown ? "right-drag" : (_middleDown ? "middle-drag" : "ctrl-left-drag"));
                        break;
                    }

                    var pt = new Point(data.pt.x, data.pt.y);
                    AppendPathPoint(pt);
                }
                break;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void BeginStroke(Point point, bool isRightButton, bool isCtrlLeft = false)
    {
        if (InputHookService.HasActiveMouseTrigger)
        {
            Log("info", "BeginStroke skipped because an active overlay/mouse trigger (Backpack/Radial/Yanm) is present.");
            return;
        }

        _rightDown = isRightButton && !isCtrlLeft;
        _middleDown = !isRightButton && !isCtrlLeft;
        _ctrlLeftDown = isCtrlLeft;
        _downPoint = point;
        _path.Clear();
        _path.Add(_downPoint);
        _traceActive = false;
        _gesturePreviewMatched = false;
        _lastPreviewInfo = null;
        _suppressNextRightUp = false;
        _suppressNextMiddleUp = false;
        _suppressNextLeftUp = false;
        CancelTrace();

        var trigger = _rightDown ? "right-drag" : (_middleDown ? "middle-drag" : "ctrl-left-drag");
        var ruleCount = _registry.TryGetValue(trigger, out var reg) ? (reg.Templates.Count + reg.SequenceMap.Count) : 0;
        Log("info", $"BeginStroke: trigger={trigger}, physicalPoint=({point.X:F0}, {point.Y:F0}), activeRules={ruleCount}");
    }

    private static void CancelStrokeForInputTrigger(string trigger)
    {
        Log("info", $"ignored {trigger} gesture because another mouse trigger is active.");
        ResetState();
    }

    private static void AppendPathPoint(Point pt)
    {
        if (InputHookService.HasActiveMouseTrigger)
        {
            CancelStrokeForInputTrigger(_rightDown ? "right-drag" : (_middleDown ? "middle-drag" : "ctrl-left-drag"));
            return;
        }

        if (_path.Count > 0 && (pt - _path[^1]).Length < 2)
        {
            return;
        }

        _path.Add(pt);
        var trigger = _rightDown ? "right-drag" : (_middleDown ? "middle-drag" : "ctrl-left-drag");
        if (!_traceActive && (pt - _downPoint).Length >= TraceStartDistance)
        {
            _traceActive = true;
            StartTrace(_downPoint, trigger);
        }

        if (!_traceActive)
        {
            return;
        }

        AddTracePoint(pt);
        UpdatePreview(trigger, pt);
    }

    private static bool IsDragIntent()
    {
        if (_traceActive)
        {
            return true;
        }

        if (_path.Count < 2)
        {
            return false;
        }

        return _path.Any(pt => (pt - _downPoint).Length >= TraceStartDistance);
    }

    private static bool TryFinishStroke(string trigger, out RegisteredGesture? matchedGesture, out MouseGesturePreviewInfo? previewInfo)
    {
        matchedGesture = null;
        previewInfo = null;

        if (_traceWindow != null)
        {
            var originAction = _traceWindow.CurrentOriginAction;
            if (originAction == OriginActionState.Cancel)
            {
                Log("info", $"FinishStroke: cancelled by user returning to cancel zone at ({_path.LastOrDefault().X:F0}, {_path.LastOrDefault().Y:F0}).");
                return false;
            }
            if (originAction == OriginActionState.Edit)
            {
                Log("info", "FinishStroke: triggered origin action: Open Settings (mousegestures).");
                ExecuteOriginEditAction();
                return false;
            }
            if (originAction == OriginActionState.Pin)
            {
                Log("info", "FinishStroke: triggered origin action: Toggle Topmost.");
                ExecuteOriginToggleTopmostAction();
                return false;
            }
        }

        if (_path.Count < 2)
        {
            Log("info", $"FinishStroke: ignored because path point count is too small ({_path.Count}).");
            return false;
        }

        var totalDist = (_path[^1] - _path[0]).Length;
        if (totalDist < MinDragDistance)
        {
            Log("info", $"FinishStroke: ignored because total displacement ({totalDist:F1}px) < MinDragDistance ({MinDragDistance}px).");
            return false;
        }

        var sequence = SimplifyPath(_path);
        if (string.IsNullOrEmpty(sequence))
        {
            Log("info", $"FinishStroke: ignored because direction sequence could not be extracted from {_path.Count} points.");
            return false;
        }

        if (!_registry.TryGetValue(trigger, out var triggerRegistry))
        {
            Log("warn", $"FinishStroke: no registered rules found for trigger '{trigger}'.");
            return false;
        }

        var templateMatch = MouseGestureTemplateRecognizer.FindBestMatch(_path, triggerRegistry.Templates);
        if (templateMatch != null && IsGestureAllowedInForeground(templateMatch.Gesture))
        {
            Log("info", $"FinishStroke (MATCHED_TEMPLATE): trigger={trigger}, points={_path.Count}, sign={templateMatch.Gesture.Sign}, score={templateMatch.Score:P0}, name='{templateMatch.Gesture.ExtensionName}', extId={templateMatch.Gesture.ExtensionId}.");
            matchedGesture = templateMatch.Gesture;
            previewInfo = BuildPreviewInfo(templateMatch.Gesture, sequence);
            return true;
        }

        if (triggerRegistry.SequenceMap.TryGetValue(sequence, out var owners) && owners.Count > 0)
        {
            var winner = SelectBestMatchingGesture(owners);
            if (winner != null)
            {
                Log("info", $"FinishStroke (MATCHED_SEQUENCE): trigger={trigger}, points={_path.Count}, sequence='{sequence}', name='{winner.ExtensionName}', extId={winner.ExtensionId}, candidates={owners.Count}.");
                matchedGesture = winner;
                previewInfo = BuildPreviewInfo(winner, sequence);
                return true;
            }
        }

        Log("info", $"FinishStroke (UNMATCHED): trigger={trigger}, points={_path.Count}, totalDist={totalDist:F1}px, sequence='{sequence}', templatesChecked={triggerRegistry.Templates.Count}, sequencesChecked={triggerRegistry.SequenceMap.Count}.");
        return false;
    }

    private static bool IsGestureAllowedInForeground(RegisteredGesture gesture)
    {
        // 1. 黑名单检查：若当前前台应用在黑名单中，坚决禁用
        if (gesture.ExcludedAppPaths != null && gesture.ExcludedAppPaths.Count > 0)
        {
            if (WindowSensorHelper.IsForegroundProcessMatch(gesture.ExcludedAppPaths))
            {
                return false;
            }
        }

        // 2. 白名单检查：若设置了白名单，必须匹配其中之一
        if (gesture.TargetAppPaths != null && gesture.TargetAppPaths.Count > 0)
        {
            return WindowSensorHelper.IsForegroundProcessMatch(gesture.TargetAppPaths);
        }

        return true;
    }

    private static RegisteredGesture? SelectBestMatchingGesture(IEnumerable<RegisteredGesture> candidates)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return null;

        // 1. 过滤掉被黑名单禁用的手势
        var activeList = list.Where(g =>
        {
            if (g.ExcludedAppPaths != null && g.ExcludedAppPaths.Count > 0)
            {
                if (WindowSensorHelper.IsForegroundProcessMatch(g.ExcludedAppPaths))
                {
                    return false;
                }
            }
            return true;
        }).ToList();

        if (activeList.Count == 0) return null;

        // 2. 优先匹配限定了当前前台应用的手势（应用专属白名单手势）
        foreach (var g in activeList)
        {
            if (g.TargetAppPaths != null && g.TargetAppPaths.Count > 0)
            {
                if (WindowSensorHelper.IsForegroundProcessMatch(g.TargetAppPaths))
                {
                    return g;
                }
            }
        }

        // 3. 如果前台应用没有专属白名单手势，回退匹配全局未限定手势
        foreach (var g in activeList)
        {
            if (g.TargetAppPaths == null || g.TargetAppPaths.Count == 0)
            {
                return g;
            }
        }

        return null;
    }

    private static void ExecuteAfterInputSettles(RegisteredGesture? winner)
    {
        if (winner == null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                winner.Execute?.Invoke(winner);
            }
            catch (Exception ex)
            {
                Log("warn", $"execute failed: ext={winner.ExtensionId}, err={ex.Message}");
            }
        });
    }

    private static bool TryFindPreviewGesture(string trigger, out MouseGesturePreviewInfo? previewInfo)
    {
        previewInfo = null;
        if (_path.Count < 2 || (_path[^1] - _path[0]).Length < MinDragDistance)
        {
            return false;
        }

        var sequence = SimplifyPath(_path);
        if (string.IsNullOrEmpty(sequence) || !_registry.TryGetValue(trigger, out var triggerRegistry))
        {
            return false;
        }

        if (triggerRegistry.SequenceMap.TryGetValue(sequence, out var owners) && owners.Count > 0)
        {
            var best = SelectBestMatchingGesture(owners);
            if (best != null)
            {
                previewInfo = BuildPreviewInfo(best, sequence);
                return true;
            }
        }

        var templateMatch = MouseGestureTemplateRecognizer.FindBestMatch(_path, triggerRegistry.Templates);
        if (templateMatch == null || !IsGestureAllowedInForeground(templateMatch.Gesture))
        {
            return false;
        }

        previewInfo = BuildPreviewInfo(templateMatch.Gesture, sequence);
        return true;
    }

    private static MouseGesturePreviewInfo BuildPreviewInfo(RegisteredGesture gesture, string sequence)
    {
        var sign = string.IsNullOrWhiteSpace(gesture.Sign)
            ? MouseGestureNaming.GetDisplayName(sequence)
            : gesture.Sign;
        return new MouseGesturePreviewInfo(
            gesture.ExtensionName,
            gesture.IconReference,
            gesture.ExtensionDirectoryPath,
            gesture.DisplayGlyph,
            sign,
            sequence);
    }

    private static string BuildFallbackGlyph(string? name)
    {
        var first = (name ?? string.Empty).Trim().EnumerateRunes().FirstOrDefault();
        return first.Value == 0 ? "E" : first.ToString().ToUpperInvariant();
    }

    private static string SimplifyPath(IReadOnlyList<Point> pts)
    {
        return MouseGestureTemplateRecognizer.ExtractSequence(pts, minStepDistance: MinSegmentDistance);
    }

    private static string NormalizeTrigger(string? raw)
    {
        return raw switch
        {
            "middle-drag" => "middle-drag",
            "ctrl-left-drag" => "ctrl-left-drag",
            _ => "right-drag"
        };
    }

    private static void HandleWheelGesture(string action, POINT pt)
    {
        _gestureActionTriggered = true;
        _suppressNextRightUp = true;
        _suppressNextMiddleUp = true;
        CancelTrace();

        var isUp = action == "wheel-up";
        var title = "滚轮手势";
        var detail = isUp ? "向上 · 上一标签" : "向下 · 下一标签";
        var glyph = isUp ? "↑" : "↓";

        DispatchTrace(window => window.ShowInstantAction(title, detail, new Point(pt.x, pt.y), glyph));

        _ = Task.Run(() =>
        {
            try
            {
                if (_registry.TryGetValue(action, out var registry) && registry.Templates.Count > 0)
                {
                    registry.Templates[0].Execute?.Invoke(registry.Templates[0]);
                    return;
                }

                // 默认行为：Ctrl + Shift + Tab (Prev Tab) / Ctrl + Tab (Next Tab)
                if (isUp)
                {
                    SendKeyboardShortcut([VkControl, VkShift], VkTab);
                }
                else
                {
                    SendKeyboardShortcut([VkControl], VkTab);
                }
                Log("info", $"executed wheel gesture: {action}");
            }
            catch (Exception ex)
            {
                Log("warn", $"wheel gesture failed: {ex.Message}");
            }
        });
    }

    private static void HandleRockerGesture(string action, POINT pt)
    {
        _gestureActionTriggered = true;
        _suppressNextRightUp = true;
        if (action == "rocker-right-left")
        {
            _suppressNextLeftUp = true;
        }
        CancelTrace();

        var isBack = action == "rocker-right-left";
        var title = "摇杆手势";
        var detail = isBack ? "按右点左 · 后退" : "按左点右 · 前进";
        var glyph = isBack ? "←" : "→";

        DispatchTrace(window => window.ShowInstantAction(title, detail, new Point(pt.x, pt.y), glyph));

        _ = Task.Run(() =>
        {
            try
            {
                if (_registry.TryGetValue(action, out var registry) && registry.Templates.Count > 0)
                {
                    registry.Templates[0].Execute?.Invoke(registry.Templates[0]);
                    return;
                }

                // 默认行为：Alt + Left (后退) / Alt + Right (前进)
                if (isBack)
                {
                    SendKeyboardShortcut([VkMenu], VkLeft);
                }
                else
                {
                    SendKeyboardShortcut([VkMenu], VkRight);
                }
                Log("info", $"executed rocker gesture: {action}");
            }
            catch (Exception ex)
            {
                Log("warn", $"rocker gesture failed: {ex.Message}");
            }
        });
    }

    private static bool ShouldBypassGesture(POINT pt)
    {
        if (IsProcessBlacklisted(pt))
        {
            return true;
        }

        if (IsForegroundWindowFullScreenGame())
        {
            return true;
        }

        return false;
    }

    private static bool IsForegroundWindowFullScreenGame()
    {
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false;

            var sb = new StringBuilder(256);
            _ = GetClassName(hWnd, sb, sb.Capacity);
            var className = sb.ToString();

            // 排除桌面与系统任务栏
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            if (!GetWindowRect(hWnd, out var windowRect)) return false;

            var hMonitor = MonitorFromWindow(hWnd, MonitorDefaultToNearest);
            if (hMonitor == IntPtr.Zero) return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref mi)) return false;

            // 判断窗口是否占满当前显示器屏幕
            return windowRect.left <= mi.rcMonitor.left &&
                   windowRect.top <= mi.rcMonitor.top &&
                   windowRect.right >= mi.rcMonitor.right &&
                   windowRect.bottom >= mi.rcMonitor.bottom;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessBlacklisted(POINT pt)
    {
        var blacklisted = AppSettingsStore.LoadCached().MouseGestureBlacklistedProcesses;
        if (blacklisted == null || blacklisted.Count == 0)
        {
            return false;
        }

        var processName = GetProcessNameAtPoint(pt);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return blacklisted.Any(name =>
            string.Equals(name.Trim(), processName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name.Trim() + ".exe", processName + ".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetProcessNameAtPoint(POINT pt)
    {
        try
        {
            var hWnd = WindowFromPoint(pt);
            if (hWnd == IntPtr.Zero) return string.Empty;
            _ = GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0) return string.Empty;
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SendKeyboardShortcut(ushort[] modifiers, ushort key)
    {
        var inputs = new List<INPUT>();
        foreach (var mod in modifiers)
        {
            inputs.Add(KeyboardInput(mod, 0));
        }
        inputs.Add(KeyboardInput(key, 0));
        inputs.Add(KeyboardInput(key, KeyeventfKeyup));
        for (var i = modifiers.Length - 1; i >= 0; i--)
        {
            inputs.Add(KeyboardInput(modifiers[i], KeyeventfKeyup));
        }

        var array = inputs.ToArray();
        _ = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyboardInput(ushort vk, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    // 标记为合成输入：InputHookService 的键盘钩子据此放行，
                    // 不会把 Ctrl+Tab / Alt+← 等手势快捷键误判为用户按键
                    dwExtraInfo = SyntheticExtraInfo
                }
            }
        };
    }

    private static void ResetState()
    {
        _leftDown = false;
        _rightDown = false;
        _middleDown = false;
        _ctrlLeftDown = false;
        _path.Clear();
        _traceActive = false;
        _gesturePreviewMatched = false;
        _lastPreviewInfo = null;
        _suppressNextRightUp = false;
        _suppressNextMiddleUp = false;
        _suppressNextLeftUp = false;
        _gestureActionTriggered = false;
        CancelTrace();
    }

    private static void StartTrace(Point screenPoint, string trigger)
    {
        var cheatItems = GetAvailableGestures(trigger);
        DispatchTrace(window => window.Start(screenPoint, cheatItems), forceNew: true);
    }

    private static IReadOnlyList<MouseGestureCheatItem> GetAvailableGestures(string trigger)
    {
        var list = new List<MouseGestureCheatItem>();
        if (!_registry.TryGetValue(trigger, out var triggerRegistry))
        {
            return list;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddGesture(RegisteredGesture g)
        {
            var gestureSign = !string.IsNullOrWhiteSpace(g.Sign) ? g.Sign : g.Sequence;
            var key = !string.IsNullOrWhiteSpace(g.ExtensionId)
                ? $"{g.ExtensionId}:{g.ExtensionName}:{gestureSign}"
                : $"{g.ExtensionName}:{gestureSign}";

            if (seen.Add(key))
            {
                list.Add(new MouseGestureCheatItem(
                    DisplaySequence: g.Sequence,
                    Name: g.ExtensionName,
                    Sign: g.Sign,
                    DisplayGlyph: g.DisplayGlyph,
                    IconReference: g.IconReference,
                    ExtensionDirectoryPath: g.ExtensionDirectoryPath,
                    Data: g.Data));
            }
        }

        // 1. 序列映射手势
        foreach (var (_, gestures) in triggerRegistry.SequenceMap)
        {
            foreach (var g in gestures)
            {
                TryAddGesture(g);
            }
        }

        // 2. 模板手势
        foreach (var g in triggerRegistry.Templates)
        {
            TryAddGesture(g);
        }

        return list;
    }

    private static void AddTracePoint(Point screenPoint)
    {
        DispatchTrace(window => window.AddPoint(screenPoint));
    }

    private static void UpdatePreview(string trigger, Point screenPoint)
    {
        var matched = TryFindPreviewGesture(trigger, out var previewInfo);
        if (matched == _gesturePreviewMatched && Equals(previewInfo, _lastPreviewInfo))
        {
            return;
        }

        _gesturePreviewMatched = matched;
        _lastPreviewInfo = previewInfo;
        DispatchTrace(window => window.UpdatePreview(matched ? previewInfo : null, screenPoint));
    }

    private static void FinishTrace(MouseGesturePreviewInfo? previewInfo, bool matched)
    {
        if (!_traceActive)
        {
            return;
        }

        DispatchTrace(window => window.Finish(matched ? previewInfo : null, matched));
        _traceActive = false;
        _gesturePreviewMatched = false;
        _lastPreviewInfo = null;
    }

    private static void ReplayMouseClickAfterHookReturns(uint downFlag, uint upFlag, string buttonName)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            var inputs = new[]
            {
                MouseInput(downFlag),
                MouseInput(upFlag)
            };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            Log("info", $"replayed short {buttonName} click, SendInput sent={sent}/2.");
        });
    }

    private static void ReleaseMouseButtonAfterHookReturns(uint upFlag, string buttonName)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(10).ConfigureAwait(false);
            var inputs = new[]
            {
                MouseInput(upFlag)
            };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            Log("info", $"forced {buttonName} button up after matched gesture, SendInput sent={sent}/1.");
        });
    }

    private static INPUT MouseInput(uint flags)
    {
        return new INPUT
        {
            type = InputMouse,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                    dwExtraInfo = SyntheticExtraInfo
                }
            }
        };
    }

    public static void WarmUp()
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_traceWindow == null)
                {
                    _traceWindow = new MouseGestureTraceWindow();
                    _traceWindow.Closed += (_, _) => _traceWindow = null;
                }
            }
            catch { }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void CancelTrace()
    {
        if (_traceWindow == null)
        {
            return;
        }

        DispatchTrace(window =>
        {
            try { window.Cancel(); } catch { }
        });
    }

    private static void DispatchTrace(Action<MouseGestureTraceWindow> action, bool forceNew = false)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        void Invoke()
        {
            if (forceNew && _traceWindow != null)
            {
                try { _traceWindow.Close(); } catch { }
                _traceWindow = null;
            }

            if (_traceWindow == null)
            {
                _traceWindow = new MouseGestureTraceWindow();
                _traceWindow.Closed += (_, _) => _traceWindow = null;
            }

            action(_traceWindow);
        }

        try
        {
            if (dispatcher.CheckAccess())
            {
                Invoke();
            }
            else
            {
                _ = dispatcher.BeginInvoke(new Action(Invoke), System.Windows.Threading.DispatcherPriority.Input);
            }
        }
        catch (Exception ex)
        {
            Log("warn", $"trace update failed: {ex.Message}");
        }
    }

    private static void ExecuteOriginEditAction()
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            if (System.Windows.Application.Current is App app)
            {
                app.OpenSettingsWindow("mousegestures");
            }
        }));
    }

    private static void ExecuteOriginToggleTopmostAction()
    {
        if (_path.Count == 0) return;
        var lastPt = _path[^1];
        var pt = new POINT { x = (int)lastPt.X, y = (int)lastPt.Y };
        var hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return;
        var targetHwnd = GetAncestor(hwnd, GaRoot);
        if (targetHwnd == IntPtr.Zero) targetHwnd = hwnd;

        try
        {
            var exStyle = GetWindowLongPtr(targetHwnd, GwlExstyle).ToInt64();
            bool isTopmost = (exStyle & WsExTopmost) != 0;
            SetWindowPos(targetHwnd, isTopmost ? HwndNoTopmost : HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

            var sb = new System.Text.StringBuilder(256);
            _ = GetWindowText(targetHwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) title = "目标窗口";

            var badgeTitle = isTopmost ? "取消置顶" : "窗口置顶";
            DispatchTrace(window =>
            {
                window.ShowInstantAction(badgeTitle, title, lastPt, "📌");
            });

            Log("info", $"ToggleTopmost on hwnd={targetHwnd}: isTopmost={!isTopmost}, title={title}");
        }
        catch (Exception ex)
        {
            Log("warn", $"ToggleTopmost failed: {ex.Message}");
        }
    }

    private static void Log(string level, string message)
    {
        try
        {
            if (_onLog != null)
            {
                _onLog.Invoke(level, $"MouseGesture: {message}");
            }
            else
            {
                HostAssets.AppendLog($"[MouseGesture {level.ToUpperInvariant()}] {message}");
            }
        }
        catch { /* ignore */ }
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);
        var hook = SetWindowsHookEx(WhMouseLl, proc, moduleHandle, 0);
        return hook != IntPtr.Zero ? hook : SetWindowsHookEx(WhMouseLl, proc, IntPtr.Zero, 0);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
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
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int GwlExstyle = -20;
    private const long WsExTopmost = 0x00000008L;
    private static readonly IntPtr HwndTopmost = new IntPtr(-1);
    private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint GaRoot = 2;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsControlDown() => (GetAsyncKeyState(0x11) & 0x8000) != 0;
}

/// <summary>
/// 一条已注册的手势。Execute 会在匹配时被调用，由调用方决定如何启动扩展。
/// </summary>
public sealed record RegisteredGesture(
    string ExtensionId,
    string ExtensionName,
    string Trigger,
    string Sequence,
    string Sign,
    string? IconReference,
    string? ExtensionDirectoryPath,
    string DisplayGlyph,
    int[]? Data,
    int? Tolerance,
    int MinDistance,
    Action<RegisteredGesture>? Execute,
    IReadOnlyList<string>? TargetAppPaths = null,
    IReadOnlyList<string>? ExcludedAppPaths = null);

public sealed record MouseGesturePreviewInfo(
    string ExtensionName,
    string? IconReference,
    string? ExtensionDirectoryPath,
    string DisplayGlyph,
    string Sign,
    string Sequence);

public sealed record MouseGestureCheatItem(
    string DisplaySequence,
    string Name,
    string Sign,
    string DisplayGlyph,
    string? IconReference,
    string? ExtensionDirectoryPath,
    int[]? Data = null);

internal sealed class GestureTriggerRegistry
{
    public Dictionary<string, List<RegisteredGesture>> SequenceMap { get; } = new(StringComparer.Ordinal);

    public List<RegisteredGesture> Templates { get; } = [];

    public int Count => SequenceMap.Values.Sum(static list => list.Count) + Templates.Count;
}
