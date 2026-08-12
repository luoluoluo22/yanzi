using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace OpenQuickHost;

public static class YanyuTriggerService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkDelete = 0x2E;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkHome = 0x24;
    private const int VkEnd = 0x23;
    private const int VkPrior = 0x21;
    private const int VkNext = 0x22;
    private const int VkInsert = 0x2D;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkV = 0x56;
    private const int VkOem1 = 0xBA;
    private const int VkOemPlus = 0xBB;
    private const int VkOemComma = 0xBC;
    private const int VkOemMinus = 0xBD;
    private const int VkOemPeriod = 0xBE;
    private const int VkOem2 = 0xBF;
    private const int VkOem3 = 0xC0;
    private const int VkOem4 = 0xDB;
    private const int VkOem5 = 0xDC;
    private const int VkOem6 = 0xDD;
    private const int VkOem7 = 0xDE;
    private const uint LlkhfInjected = 0x00000010;
    private static readonly IntPtr SyntheticInputMarker = new(0x59414E5A);
    private static readonly LowLevelKeyboardProc Proc = HookCallback;
    private static readonly StringBuilder Buffer = new();
    private static readonly Lock SyncRoot = new();
    private static IntPtr _hookId = IntPtr.Zero;
    private static Action<YanyuTriggerEvent>? _onRuleTriggered;
    private static List<YanyuRuleSettings> _rules = [];
    private static readonly HashSet<int> SuppressedKeys = [];
    private static bool _leftShiftDown;
    private static bool _rightShiftDown;
    private static bool _leftCtrlDown;
    private static bool _rightCtrlDown;
    private static bool _leftAltDown;
    private static bool _rightAltDown;
    private static bool _leftWinDown;
    private static bool _rightWinDown;

    public static bool IsRunning => _hookId != IntPtr.Zero;

    public static void Start(Action<YanyuTriggerEvent> onRuleTriggered)
    {
        if (IsRunning)
        {
            UpdateRules(AppSettingsStore.Load().YanyuRules);
            return;
        }

        _onRuleTriggered = onRuleTriggered;
        UpdateRules(AppSettingsStore.Load().YanyuRules);
        ResetState();
        _hookId = SetHook(Proc);
        if (_hookId == IntPtr.Zero)
        {
            HostAssets.AppendLog($"Yanyu trigger: failed to install hook, lastError={Marshal.GetLastWin32Error()}.");
            return;
        }

        HostAssets.AppendLog($"Yanyu trigger: started. hook=0x{_hookId.ToInt64():X}, rules={_rules.Count}.");
    }

    public static void UpdateRules(IEnumerable<YanyuRuleSettings>? rules)
    {
        lock (SyncRoot)
        {
            _rules = (rules ?? [])
                .Where(static rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.TriggerText))
                .Select(CloneRule)
                .ToList();
        }
    }

    public static void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        var unhooked = UnhookWindowsHookEx(_hookId);
        HostAssets.AppendLog($"Yanyu trigger: stopped. unhooked={unhooked}.");
        _hookId = IntPtr.Zero;
        _onRuleTriggered = null;
        lock (SyncRoot)
        {
            _rules = [];
        }

        ResetState();
    }

    public static void PasteText(string text)
    {
        ClipboardService.SetText(text ?? string.Empty);
        SendChord(VkLControl, VkV);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        var moduleHandle = GetModuleHandle(currentModule.ModuleName);
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
        if (hook != IntPtr.Zero)
        {
            return hook;
        }

        return SetWindowsHookEx(WhKeyboardLl, proc, IntPtr.Zero, 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((info.flags & LlkhfInjected) != 0 || info.dwExtraInfo == SyntheticInputMarker)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        var vkCode = (int)info.vkCode;
        var suppress = message switch
        {
            WmKeyDown or WmSysKeyDown => HandleKeyDown(vkCode),
            WmKeyUp or WmSysKeyUp => HandleKeyUp(vkCode),
            _ => false
        };

        return suppress ? 1 : CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool HandleKeyDown(int vkCode)
    {
        switch (vkCode)
        {
            case VkLShift:
                _leftShiftDown = true;
                return false;
            case VkRShift:
                _rightShiftDown = true;
                return false;
            case VkLControl:
                _leftCtrlDown = true;
                ClearBuffer();
                return false;
            case VkRControl:
                _rightCtrlDown = true;
                ClearBuffer();
                return false;
            case VkLMenu:
                _leftAltDown = true;
                ClearBuffer();
                return false;
            case VkRMenu:
                _rightAltDown = true;
                ClearBuffer();
                return false;
            case VkLWin:
                _leftWinDown = true;
                ClearBuffer();
                return false;
            case VkRWin:
                _rightWinDown = true;
                ClearBuffer();
                return false;
        }

        if (SuppressedKeys.Contains(vkCode))
        {
            return true;
        }

        if (IsCurrentForegroundWindowOwnedByYanzi())
        {
            ClearBuffer();
            return false;
        }

        if (HasBlockingModifiers())
        {
            ClearBuffer();
            return false;
        }

        if (vkCode == VkBack)
        {
            if (Buffer.Length > 0)
            {
                Buffer.Length--;
            }

            return false;
        }

        if (IsBufferResetKey(vkCode))
        {
            ClearBuffer();
            return false;
        }

        var shiftPressed = _leftShiftDown || _rightShiftDown;
        if (TryGetSuffixToken(vkCode, shiftPressed, out var suffixToken))
        {
            if (TryMatchRule(Buffer.ToString(), suffixToken, out var triggerEvent))
            {
                SuppressedKeys.Add(vkCode);
                SendBackspaces(triggerEvent.MatchedText.Length);
                ClearBuffer();
                HostAssets.AppendLog($"Yanyu trigger matched: trigger={triggerEvent.Rule.TriggerText}, suffix={triggerEvent.Rule.TriggerSuffix}, action={triggerEvent.Rule.ActionType}, process={triggerEvent.ForegroundProcessName}.");
                QueueRuleTrigger(triggerEvent);
                return true;
            }

            ClearBuffer();
            return false;
        }

        if (TryGetTriggerCharacter(vkCode, shiftPressed, out var triggerCharacter))
        {
            Buffer.Append(char.ToLowerInvariant(triggerCharacter));
            TrimBuffer();
            return false;
        }

        ClearBuffer();
        return false;
    }

    private static bool HandleKeyUp(int vkCode)
    {
        switch (vkCode)
        {
            case VkLShift:
                _leftShiftDown = false;
                return false;
            case VkRShift:
                _rightShiftDown = false;
                return false;
            case VkLControl:
                _leftCtrlDown = false;
                return false;
            case VkRControl:
                _rightCtrlDown = false;
                return false;
            case VkLMenu:
                _leftAltDown = false;
                return false;
            case VkRMenu:
                _rightAltDown = false;
                return false;
            case VkLWin:
                _leftWinDown = false;
                return false;
            case VkRWin:
                _rightWinDown = false;
                return false;
        }

        return SuppressedKeys.Remove(vkCode);
    }

    private static void QueueRuleTrigger(YanyuTriggerEvent triggerEvent)
    {
        var callback = _onRuleTriggered;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (callback == null || dispatcher == null)
        {
            return;
        }

        var clonedEvent = triggerEvent.Clone();
        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, () => callback(clonedEvent));
    }

    private static void SendBackspaces(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new INPUT[count * 2];
        for (var index = 0; index < count; index++)
        {
            inputs[index * 2] = CreateKeyInput(VkBack, keyUp: false);
            inputs[index * 2 + 1] = CreateKeyInput(VkBack, keyUp: true);
        }

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendChord(int modifierVk, int keyVk)
    {
        var inputs = new[]
        {
            CreateKeyInput(modifierVk, keyUp: false),
            CreateKeyInput(keyVk, keyUp: false),
            CreateKeyInput(keyVk, keyUp: true),
            CreateKeyInput(modifierVk, keyUp: true)
        };

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateKeyInput(int vkCode, bool keyUp)
    {
        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vkCode,
                    dwFlags = keyUp ? 0x0002u : 0u,
                    dwExtraInfo = SyntheticInputMarker
                }
            }
        };
    }

    private static bool TryMatchRule(string buffer, string suffixToken, out YanyuTriggerEvent triggerEvent)
    {
        var normalizedBuffer = buffer.Trim().ToLowerInvariant();
        var normalizedSuffix = YanyuTriggerSuffix.Normalize(suffixToken);
        var foregroundProcessName = GetForegroundProcessName();
        lock (SyncRoot)
        {
            foreach (var rule in _rules)
            {
                if (!string.Equals(YanyuTriggerSuffix.Normalize(rule.TriggerSuffix), normalizedSuffix, StringComparison.OrdinalIgnoreCase) ||
                    !IsProcessAllowed(rule.BoundProcessName, foregroundProcessName))
                {
                    continue;
                }

                if (rule.UseRegex)
                {
                    if (TryMatchRegexRule(rule, buffer, foregroundProcessName, out triggerEvent))
                    {
                        return true;
                    }

                    continue;
                }

                if (string.Equals(rule.TriggerText, normalizedBuffer, StringComparison.OrdinalIgnoreCase))
                {
                    triggerEvent = new YanyuTriggerEvent(CloneRule(rule), buffer.Trim(), foregroundProcessName, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    return true;
                }
            }
        }

        triggerEvent = YanyuTriggerEvent.Empty;
        return false;
    }

    private static bool TryMatchRegexRule(YanyuRuleSettings rule, string buffer, string foregroundProcessName, out YanyuTriggerEvent triggerEvent)
    {
        triggerEvent = YanyuTriggerEvent.Empty;
        try
        {
            var regex = new Regex(rule.TriggerText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80));
            var matches = regex.Matches(buffer);
            var match = matches.Cast<Match>().LastOrDefault(item => item.Success && item.Index + item.Length == buffer.Length);
            if (match == null)
            {
                return false;
            }

            var groups = match.Groups.Cast<Group>().Skip(1).Select(static group => group.Value).ToList();
            var namedGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in regex.GetGroupNames())
            {
                if (int.TryParse(name, out _))
                {
                    continue;
                }

                namedGroups[name] = match.Groups[name].Value;
            }

            triggerEvent = new YanyuTriggerEvent(CloneRule(rule), match.Value, foregroundProcessName, groups, namedGroups);
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Yanyu regex match failed: trigger={rule.TriggerText}, error={ex.Message}");
            return false;
        }
    }

    private static bool IsProcessAllowed(string? boundProcessName, string foregroundProcessName)
    {
        var configured = (boundProcessName ?? string.Empty).Trim();
        if (configured.Length == 0)
        {
            return true;
        }

        return configured
            .Split([',', ';', '|', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item =>
            {
                var normalized = item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? item[..^4] : item;
                return string.Equals(normalized, foregroundProcessName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static bool TryGetTriggerCharacter(int vkCode, bool shiftPressed, out char character)
    {
        character = '\0';
        if (vkCode is >= 0x41 and <= 0x5A)
        {
            character = (char)vkCode;
            return true;
        }

        if (vkCode is >= 0x30 and <= 0x39)
        {
            if (shiftPressed)
            {
                return false;
            }

            character = (char)vkCode;
            return true;
        }

        if (vkCode is >= 0x60 and <= 0x69)
        {
            character = (char)('0' + (vkCode - 0x60));
            return true;
        }

        if (vkCode == VkOemMinus)
        {
            character = shiftPressed ? '_' : '-';
            return true;
        }

        return false;
    }

    private static bool TryGetSuffixToken(int vkCode, bool shiftPressed, out string suffixToken)
    {
        suffixToken = vkCode switch
        {
            VkSpace => YanyuTriggerSuffix.Space,
            VkTab => YanyuTriggerSuffix.Tab,
            VkReturn => YanyuTriggerSuffix.Enter,
            _ => string.Empty
        };

        if (suffixToken.Length > 0)
        {
            return true;
        }

        if (!TryGetPunctuationCharacter(vkCode, shiftPressed, out var punctuation))
        {
            return false;
        }

        suffixToken = punctuation.ToString();
        return true;
    }

    private static bool TryGetPunctuationCharacter(int vkCode, bool shiftPressed, out char character)
    {
        character = vkCode switch
        {
            VkOem1 => shiftPressed ? ':' : ';',
            VkOemPlus => shiftPressed ? '+' : '=',
            VkOemComma => shiftPressed ? '<' : ',',
            VkOemPeriod => shiftPressed ? '>' : '.',
            VkOem2 => shiftPressed ? '?' : '/',
            VkOem3 => shiftPressed ? '~' : '`',
            VkOem4 => shiftPressed ? '{' : '[',
            VkOem5 => shiftPressed ? '|' : '\\',
            VkOem6 => shiftPressed ? '}' : ']',
            VkOem7 => shiftPressed ? '"' : '\'',
            _ => '\0'
        };

        return character != '\0';
    }

    private static bool HasBlockingModifiers()
    {
        return _leftCtrlDown || _rightCtrlDown || _leftAltDown || _rightAltDown || _leftWinDown || _rightWinDown;
    }

    private static bool IsCurrentForegroundWindowOwnedByYanzi()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return processId == Environment.ProcessId;
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            var className = new System.Text.StringBuilder(256);
            if (NativeMethods.GetClassName(handle, className, className.Capacity) > 0)
            {
                var classStr = className.ToString();
                if (string.Equals(classStr, "Progman", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return "desktop";
                }
            }

            _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            return ProcessHelper.GetProcessNameByPid(processId);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsBufferResetKey(int vkCode)
    {
        return vkCode is VkEscape or VkDelete or VkLeft or VkRight or VkUp or VkDown or VkHome or VkEnd or VkPrior or VkNext or VkInsert;
    }

    private static void TrimBuffer()
    {
        const int maxBufferLength = 48;
        if (Buffer.Length <= maxBufferLength)
        {
            return;
        }

        Buffer.Remove(0, Buffer.Length - maxBufferLength);
    }

    private static void ClearBuffer()
    {
        if (Buffer.Length > 0)
        {
            Buffer.Clear();
        }
    }

    private static void ResetState()
    {
        ClearBuffer();
        SuppressedKeys.Clear();
        _leftShiftDown = false;
        _rightShiftDown = false;
        _leftCtrlDown = false;
        _rightCtrlDown = false;
        _leftAltDown = false;
        _rightAltDown = false;
        _leftWinDown = false;
        _rightWinDown = false;
    }

    private static YanyuRuleSettings CloneRule(YanyuRuleSettings rule)
    {
        return new YanyuRuleSettings
        {
            Id = rule.Id,
            Enabled = rule.Enabled,
            TriggerText = rule.TriggerText,
            TriggerSuffix = rule.TriggerSuffix,
            UseRegex = rule.UseRegex,
            BoundProcessName = rule.BoundProcessName,
            Description = rule.Description,
            ActionType = rule.ActionType,
            TextContent = rule.TextContent,
            ExtensionId = rule.ExtensionId
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
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

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

public sealed class YanyuTriggerEvent
{
    public static YanyuTriggerEvent Empty { get; } = new(new YanyuRuleSettings(), string.Empty, string.Empty, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public YanyuTriggerEvent(
        YanyuRuleSettings rule,
        string matchedText,
        string foregroundProcessName,
        IReadOnlyList<string> groups,
        IReadOnlyDictionary<string, string> namedGroups)
    {
        Rule = rule;
        MatchedText = matchedText;
        ForegroundProcessName = foregroundProcessName;
        Groups = groups.ToList();
        NamedGroups = new Dictionary<string, string>(namedGroups, StringComparer.OrdinalIgnoreCase);
    }

    public YanyuRuleSettings Rule { get; }

    public string MatchedText { get; }

    public string ForegroundProcessName { get; }

    public IReadOnlyList<string> Groups { get; }

    public IReadOnlyDictionary<string, string> NamedGroups { get; }

    public YanyuTriggerEvent Clone() => new(
        new YanyuRuleSettings
        {
            Id = Rule.Id,
            Enabled = Rule.Enabled,
            TriggerText = Rule.TriggerText,
            TriggerSuffix = Rule.TriggerSuffix,
            UseRegex = Rule.UseRegex,
            BoundProcessName = Rule.BoundProcessName,
            Description = Rule.Description,
            ActionType = Rule.ActionType,
            TextContent = Rule.TextContent,
            ExtensionId = Rule.ExtensionId
        },
        MatchedText,
        ForegroundProcessName,
        Groups,
        NamedGroups);
}
