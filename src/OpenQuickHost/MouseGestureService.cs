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
    private const uint LlInjected = 0x00000001;
    private const uint InputMouse = 0;
    private const uint MouseeventfRightDown = 0x0008;
    private const uint MouseeventfRightUp = 0x0010;
    private const uint MouseeventfMiddleDown = 0x0020;
    private const uint MouseeventfMiddleUp = 0x0040;

    private static readonly LowLevelMouseProc MouseProc = HookCallback;
    private static IntPtr _hookId;
    private static bool _isRunning;

    // 当前拖动状态
    private static bool _rightDown;
    private static bool _middleDown;
    private static Point _downPoint;
    private static readonly List<Point> _path = new(capacity: 256);
    private static bool _suppressNextRightUp;
    private static bool _suppressNextMiddleUp;
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

    public static bool HasRightDragRegistrations =>
        _registry.TryGetValue("right-drag", out var registry) && registry.Count > 0;

    public static bool HasMiddleDragRegistrations =>
        _registry.TryGetValue("middle-drag", out var registry) && registry.Count > 0;

    /// <summary>
    /// 启动全局 hook。<paramref name="logger"/> 用于把 (level, message) 输出到调用方日志（一般是 HostAssets）。
    /// </summary>
    public static void Start(Action<string, string>? logger = null)
    {
        if (_isRunning) return;
        _onLog = logger;
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
    /// 重新构建注册表。<paramref name="gestures"/> 来自所有扩展 manifest 的 MouseGesture 字段。
    /// </summary>
    public static void ReloadRegistrations(IEnumerable<RegisteredGesture> gestures)
    {
        _registry.Clear();
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
        var registrations = new List<RegisteredGesture>();
        foreach (var entry in entries)
        {
            var m = entry.Manifest;
            var g = m.MouseGesture;
            if (g == null || (string.IsNullOrWhiteSpace(g.Sequence) && !MouseGestureTemplateRecognizer.HasTemplateData(g.Data))) continue;
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
                Execute: onExecute));
        }
        ReloadRegistrations(registrations);
        return registrations.Count;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var msg = wParam.ToInt32();
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if ((data.flags & LlInjected) != 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        switch (msg)
        {
            case WmLButtonDown:
                // 按了左键，跟其他服务（YarnSelect）共享时本服务不应在左键按下时尝试手势
                ResetState();
                break;

            case WmRButtonDown:
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
                        ReleaseMouseButtonAfterHookReturns(MouseeventfRightUp, "right");
                        InputHookService.ResetMouseState("mouse gesture");
                        ExecuteAfterInputSettles(matchedGesture);
                        _ = CallNextHookEx(_hookId, nCode, wParam, lParam);
                        return (IntPtr)1;
                    }

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
                    return (IntPtr)1;
                }
                break;

            case WmMButtonDown:
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
                        ReleaseMouseButtonAfterHookReturns(MouseeventfMiddleUp, "middle");
                        InputHookService.ResetMouseState("mouse gesture");
                        ExecuteAfterInputSettles(matchedGesture);
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
                    return (IntPtr)1;
                }
                break;

            case WmMouseMove:
                if (_rightDown || _middleDown)
                {
                    if (InputHookService.HasActiveMouseTrigger)
                    {
                        CancelStrokeForInputTrigger(_rightDown ? "right-drag" : "middle-drag");
                        break;
                    }

                    var pt = new Point(data.pt.x, data.pt.y);
                    AppendPathPoint(pt);
                }
                break;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void BeginStroke(Point point, bool isRightButton)
    {
        _rightDown = isRightButton;
        _middleDown = !isRightButton;
        _downPoint = point;
        _path.Clear();
        _path.Add(_downPoint);
        _traceActive = false;
        _gesturePreviewMatched = false;
        _lastPreviewInfo = null;
        _suppressNextRightUp = false;
        _suppressNextMiddleUp = false;
    }

    private static void CancelStrokeForInputTrigger(string trigger)
    {
        Log("info", $"ignored {trigger} gesture because another mouse trigger is active.");
        ResetState();
    }

    private static void AppendPathPoint(Point pt)
    {
        if (_path.Count > 0 && (pt - _path[^1]).Length < 2)
        {
            return;
        }

        _path.Add(pt);
        if (!_traceActive && (pt - _downPoint).Length >= TraceStartDistance)
        {
            _traceActive = true;
            StartTrace(_downPoint);
        }

        if (!_traceActive)
        {
            return;
        }

        AddTracePoint(pt);
        UpdatePreview(_rightDown ? "right-drag" : "middle-drag", pt);
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
        if (_path.Count < 2) return false;
        var totalDist = (_path[^1] - _path[0]).Length;
        if (totalDist < MinDragDistance) return false;

        var sequence = SimplifyPath(_path);
        if (string.IsNullOrEmpty(sequence)) return false;

        if (!_registry.TryGetValue(trigger, out var triggerRegistry)) return false;

        var templateMatch = MouseGestureTemplateRecognizer.FindBestMatch(_path, triggerRegistry.Templates);
        if (templateMatch != null)
        {
            Log("info", $"matched template: trigger={trigger}, sign={templateMatch.Gesture.Sign}, distance={templateMatch.Distance:0.0}, ext={templateMatch.Gesture.ExtensionId}.");
            matchedGesture = templateMatch.Gesture;
            previewInfo = BuildPreviewInfo(templateMatch.Gesture, sequence);
            return true;
        }

        if (!triggerRegistry.SequenceMap.TryGetValue(sequence, out var owners) || owners.Count == 0) return false;

        // 多个扩展共用同一手势：暂取首个；后续可改为弹气泡选择
        var winner = owners[0];
        Log("info", $"matched: trigger={trigger}, sequence={sequence}, ext={winner.ExtensionId}, candidates={owners.Count}.");
        matchedGesture = winner;
        previewInfo = BuildPreviewInfo(winner, sequence);
        return true;
    }

    private static void ExecuteAfterInputSettles(RegisteredGesture? winner)
    {
        if (winner == null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(35).ConfigureAwait(false);
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
            previewInfo = BuildPreviewInfo(owners[0], sequence);
            return true;
        }

        var templateMatch = MouseGestureTemplateRecognizer.FindBestMatch(_path, triggerRegistry.Templates);
        if (templateMatch == null)
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
        if (pts.Count < 2) return string.Empty;
        var sb = new StringBuilder();
        var lastDir = -1;
        var anchor = pts[0];
        for (var i = 1; i < pts.Count; i++)
        {
            var dx = pts[i].X - anchor.X;
            var dy = pts[i].Y - anchor.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < MinSegmentDistance) continue;
            var angle = Math.Atan2(dy, dx);
            var normalized = (angle + TwoPi) % TwoPi;
            var idx = (int)Math.Round(normalized / EightthPi) % 8;
            if (idx != lastDir)
            {
                sb.Append(Arrows[idx]);
                lastDir = idx;
            }
            anchor = pts[i];
        }
        return sb.ToString();
    }

    private static string NormalizeTrigger(string? raw)
    {
        return raw == "middle-drag" ? "middle-drag" : "right-drag";
    }

    private static void ResetState()
    {
        _rightDown = false;
        _middleDown = false;
        _path.Clear();
        _traceActive = false;
        _gesturePreviewMatched = false;
        _lastPreviewInfo = null;
        _suppressNextRightUp = false;
        _suppressNextMiddleUp = false;
        CancelTrace();
    }

    private static void StartTrace(Point screenPoint)
    {
        DispatchTrace(window => window.Start(screenPoint));
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
                    dwFlags = flags
                }
            }
        };
    }

    private static void CancelTrace()
    {
        if (_traceWindow == null)
        {
            return;
        }

        DispatchTrace(window => window.Cancel());
    }

    private static void DispatchTrace(Action<MouseGestureTraceWindow> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        void Invoke()
        {
            _traceWindow ??= new MouseGestureTraceWindow();
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

    private static void Log(string level, string message)
    {
        try
        {
            _onLog?.Invoke(level, $"MouseGesture: {message}");
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
    Action<RegisteredGesture>? Execute);

public sealed record MouseGesturePreviewInfo(
    string ExtensionName,
    string? IconReference,
    string? ExtensionDirectoryPath,
    string DisplayGlyph,
    string Sign,
    string Sequence);

internal sealed class GestureTriggerRegistry
{
    public Dictionary<string, List<RegisteredGesture>> SequenceMap { get; } = new(StringComparer.Ordinal);

    public List<RegisteredGesture> Templates { get; } = [];

    public int Count => SequenceMap.Values.Sum(static list => list.Count) + Templates.Count;
}
