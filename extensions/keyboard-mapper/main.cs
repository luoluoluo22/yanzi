using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class MappingRule
{
    public string Id { get; set; } = string.Empty;
    public int FromVk { get; set; }
    public string FromName { get; set; } = string.Empty;
    public int ToVk { get; set; }
    public string ToName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsPermanent { get; set; } = false; // 是否是系统级永久映射
}

public static class KeyMapperManager
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
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
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static IntPtr _hookId = IntPtr.Zero;
    private static LowLevelKeyboardProc _proc = HookCallback;
    private static readonly object RulesLock = new object();
    
    public static Dictionary<int, int> EnabledRules = new Dictionary<int, int>();
    public static List<MappingRule> AllRules = new List<MappingRule>();
    private static string _dataDirectory = string.Empty;

    public static bool IsRunning => _hookId != IntPtr.Zero;

    // The host's global double-tap hook ignores injected events. When a physical
    // modifier is remapped, forward the logical modifier to its existing detector.
    // This keeps Ctrl/Alt remaps while preserving DoubleCtrl/DoubleAlt launchers.
    private static bool _hostTapMethodsResolved;
    private static System.Reflection.MethodInfo? _hostMappedKeyDown;
    private static System.Reflection.MethodInfo? _hostMappedKeyUp;

    private static void ForwardMappedModifierToLauncher(int targetVk, bool isKeyDown)
    {
        if (targetVk != 0xA2 && targetVk != 0xA3 &&
            targetVk != 0xA4 && targetVk != 0xA5)
        {
            return;
        }

        try
        {
            if (!_hostTapMethodsResolved)
            {
                _hostTapMethodsResolved = true;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var hostType = assembly.GetType("OpenQuickHost.KeyboardDoubleTapService", false);
                    if (hostType == null) continue;

                    var flags = System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Static;
                    _hostMappedKeyDown = hostType.GetMethod("HandleKeyDown", flags);
                    _hostMappedKeyUp = hostType.GetMethod("HandleKeyUp", flags);
                    break;
                }
            }

            var handler = isKeyDown ? _hostMappedKeyDown : _hostMappedKeyUp;
            handler?.Invoke(null, new object[] { targetVk });
        }
        catch
        {
            // The remapping hook must never fail because a host API changed.
        }
    }

    public static void Start(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        LoadRules();

        if (IsRunning) return;
        
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
        }
    }

    public static void Stop()
    {
        if (!IsRunning) return;

        // 释放所有已启用映射的目标键，防止卡住
        lock (RulesLock)
        {
            foreach (var targetVk in EnabledRules.Values)
            {
                keybd_event((byte)targetVk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    public static void LoadRules()
    {
        lock (RulesLock)
        {
            EnabledRules.Clear();
            AllRules.Clear();
            
            if (string.IsNullOrEmpty(_dataDirectory)) return;
            
            string rulesPath = Path.Combine(_dataDirectory, "rules.json");
            if (File.Exists(rulesPath))
            {
                try
                {
                    string json = File.ReadAllText(rulesPath);
                    var rules = JsonSerializer.Deserialize<List<MappingRule>>(json);
                    if (rules != null)
                    {
                        AllRules = rules;
                        foreach (var rule in rules)
                        {
                            if (rule.Enabled)
                            {
                                EnabledRules[rule.FromVk] = rule.ToVk;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore load errors
                }
            }
        }
    }

    public static void SaveRules()
    {
        if (string.IsNullOrEmpty(_dataDirectory)) return;
        
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            string rulesPath = Path.Combine(_dataDirectory, "rules.json");
            string json = JsonSerializer.Serialize(AllRules, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(rulesPath, json);
            
            lock (RulesLock)
            {
                EnabledRules.Clear();
                foreach (var rule in AllRules)
                {
                    // 仅当规则启用，且不是系统级永久映射时，才加入动态键盘钩子的生效规则字典
                    if (rule.Enabled && !rule.IsPermanent)
                    {
                        EnabledRules[rule.FromVk] = rule.ToVk;
                    }
                }
            }
        }
        catch
        {
            // Ignore save errors
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private static int VkToScanCode(int vk)
    {
        switch (vk)
        {
            case 0xA3: return 0xE01D; // 右 Ctrl
            case 0xA5: return 0xE038; // 右 Alt
            case 0x5B: return 0xE05B; // 左 Win
            case 0x5C: return 0xE05C; // 右 Win
            case 0x5D: return 0xE05D; // Menu / Apps
            case 0x2D: return 0xE052; // Insert
            case 0x2E: return 0xE053; // Delete
            case 0x24: return 0xE047; // Home
            case 0x23: return 0xE04F; // End
            case 0x21: return 0xE049; // Page Up
            case 0x22: return 0xE051; // Page Down
            case 0x25: return 0xE04B; // 左
            case 0x27: return 0xE04D; // 右
            case 0x26: return 0xE048; // 上
            case 0x28: return 0xE050; // 下
            default:
                return (int)MapVirtualKey((uint)vk, 0);
        }
    }

    public static bool UpdateRegistryMappings(out string errorMessage)
    {
        errorMessage = string.Empty;
        var permRules = AllRules.FindAll(r => r.Enabled && r.IsPermanent);

        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layout", true))
            {
                if (key == null)
                {
                    errorMessage = "未找到 Keyboard Layout 注册表项。";
                    return false;
                }

                if (permRules.Count == 0)
                {
                    key.DeleteValue("Scancode Map", false);
                    return true;
                }

                // 计算字节大小: 8字节 Header + 4字节 Count + N*4字节 映射项 + 4字节 Null终止符
                int totalBytes = 8 + 4 + permRules.Count * 4 + 4;
                byte[] bytes = new byte[totalBytes];

                // Header 默认 0
                // Count = permRules.Count + 1
                byte[] countBytes = BitConverter.GetBytes(permRules.Count + 1);
                Array.Copy(countBytes, 0, bytes, 8, 4);

                for (int i = 0; i < permRules.Count; i++)
                {
                    var rule = permRules[i];
                    int fromScan = VkToScanCode(rule.FromVk);
                    int toScan = VkToScanCode(rule.ToVk);

                    byte[] toBytes = BitConverter.GetBytes((ushort)toScan);
                    byte[] fromBytes = BitConverter.GetBytes((ushort)fromScan);

                    int offset = 12 + i * 4;
                    Array.Copy(toBytes, 0, bytes, offset, 2);
                    Array.Copy(fromBytes, 0, bytes, offset + 2, 2);
                }

                key.SetValue("Scancode Map", bytes, Microsoft.Win32.RegistryValueKind.Binary);
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = "需要管理员权限。请尝试以管理员身份运行主程序后再重试。";
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        return false;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            // 过滤注入按键以防无限递归
            bool isInjected = (kbStruct.flags & 0x10) != 0;
            
            if (!isInjected)
            {
                int vk = kbStruct.vkCode;
                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                if (isKeyDown || isKeyUp)
                {
                    int targetVk = 0;
                    bool hasRule = false;
                    
                    lock (RulesLock)
                    {
                        hasRule = EnabledRules.TryGetValue(vk, out targetVk);
                    }

                    if (hasRule)
                    {
                        uint flags = isKeyUp ? KEYEVENTF_KEYUP : 0;
                        // The host ignores injected keys, so update its double-tap state
                        // with the remapped logical modifier before injecting the OS key.
                        ForwardMappedModifierToLauncher(targetVk, isKeyDown);
                        keybd_event((byte)targetVk, 0, flags, UIntPtr.Zero);
                        return new IntPtr(1); // 吞掉物理按键事件
                    }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public class RegistryMappingRule
    {
        public int FromScanCode { get; set; }
        public string FromName { get; set; } = string.Empty;
        public int ToScanCode { get; set; }
        public string ToName { get; set; } = string.Empty;
    }

    private static readonly Dictionary<int, string> ScanCodeNames = new Dictionary<int, string>
    {
        { 0x001D, "左 Ctrl" },
        { 0x0038, "左 Alt" },
        { 0x003A, "Caps Lock (大写锁定)" },
        { 0x0001, "Esc" },
        { 0x000E, "退格 (Backspace)" },
        { 0x000F, "Tab" },
        { 0x001C, "回车 (Enter)" },
        { 0x002A, "左 Shift" },
        { 0x0036, "右 Shift" },
        { 0xE01D, "右 Ctrl" },
        { 0xE038, "右 Alt" },
        { 0xE05B, "左 Win" },
        { 0xE05C, "右 Win" },
        { 0xE05D, "Menu (菜单键)" },
        { 0xE050, "下箭头" },
        { 0xE04B, "左箭头" },
        { 0xE04D, "右箭头" },
        { 0xE048, "上箭头" },
        { 0xE053, "Delete" },
        { 0xE052, "Insert" },
        { 0xE04F, "End" },
        { 0xE047, "Home" },
        { 0xE049, "Page Up" },
        { 0xE051, "Page Down" },
        { 0x0039, "空格 (Space)" },
        { 0x0002, "1" }, { 0x0003, "2" }, { 0x0004, "3" }, { 0x0005, "4" }, { 0x0006, "5" }, { 0x0007, "6" }, { 0x0008, "7" }, { 0x0009, "8" }, { 0x000A, "9" }, { 0x000B, "0" },
        { 0x001E, "A" }, { 0x0030, "B" }, { 0x002E, "C" }, { 0x0020, "D" }, { 0x0012, "E" }, { 0x0021, "F" }, { 0x0022, "G" }, { 0x0023, "H" }, { 0x0017, "I" }, { 0x0024, "J" },
        { 0x0025, "K" }, { 0x0026, "L" }, { 0x0032, "M" }, { 0x0031, "N" }, { 0x0018, "O" }, { 0x0019, "P" }, { 0x0010, "Q" }, { 0x0013, "R" }, { 0x001F, "S" }, { 0x0014, "T" },
        { 0x0016, "U" }, { 0x002F, "V" }, { 0x0011, "W" }, { 0x002D, "X" }, { 0x0015, "Y" }, { 0x002C, "Z" }
    };

    private static string GetScanCodeFriendlyName(int scanCode)
    {
        if (ScanCodeNames.TryGetValue(scanCode, out var name))
        {
            return name;
        }
        return $"扫描码 0x{scanCode:X4}";
    }

    public static List<RegistryMappingRule> GetRegistryMappings()
    {
        var list = new List<RegistryMappingRule>();
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layout"))
            {
                if (key == null) return list;
                var bytes = key.GetValue("Scancode Map") as byte[];
                if (bytes == null || bytes.Length < 16) return list;

                int count = BitConverter.ToInt32(bytes, 8);
                for (int i = 0; i < count - 1; i++)
                {
                    int offset = 12 + i * 4;
                    if (offset + 4 > bytes.Length) break;

                    int toScan = BitConverter.ToUInt16(bytes, offset);
                    int fromScan = BitConverter.ToUInt16(bytes, offset + 2);

                    if (fromScan == 0) continue;

                    string fromName = GetScanCodeFriendlyName(fromScan);
                    string toName = toScan == 0 ? "禁用" : GetScanCodeFriendlyName(toScan);

                    list.Add(new RegistryMappingRule
                    {
                        FromScanCode = fromScan,
                        FromName = fromName,
                        ToScanCode = toScan,
                        ToName = toName
                    });
                }
            }
        }
        catch
        {
            // Ignore
        }
        return list;
    }

    public static bool ClearRegistryMappings(out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layout", true))
            {
                if (key != null)
                {
                    key.DeleteValue("Scancode Map", false);
                    return true;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = "需要管理员权限。请尝试以管理员身份运行主程序后再重试。";
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        return false;
    }
}

public class MapperWindow : Window
{
    private enum RecordingState
    {
        None,
        FromKey,
        ToKey
    }

    private RecordingState _recordingState = RecordingState.None;
    private int _pendingFromVk = 0;
    private string _pendingFromName = string.Empty;
    private int _pendingToVk = 0;
    private string _pendingToName = string.Empty;

    private readonly string _dataDir;
    private bool _forceClose = false;
    
    // UI Elements
    private TextBlock StatusText;
    private System.Windows.Shapes.Ellipse StatusDot;
    private Button ToggleServiceBtn;
    private TextBox SearchTextBox;
    private StackPanel RuleListPanel;
    private Button FromKeyBtn;
    private Button ToKeyBtn;
    private Button AddRuleBtn;

    // Active Maps UI Elements
    private Border ActiveMapsBorder;
    private WrapPanel ActiveMapsPanel;
    private Button ResetAllBtn;

    // System Registry Maps UI Elements (NEW)
    private Border SystemMapsBorder;
    private WrapPanel SystemMapsPanel;
    private Button ClearSystemMapsBtn;

    private static readonly Dictionary<int, string> KeyNames = new Dictionary<int, string>
    {
        { 0x08, "退格 (Backspace)" },
        { 0x09, "Tab" },
        { 0x0D, "回车 (Enter)" },
        { 0x14, "大写锁定 (Caps Lock)" },
        { 0x1B, "Esc" },
        { 0x20, "空格 (Space)" },
        { 0x21, "Page Up" },
        { 0x22, "Page Down" },
        { 0x23, "End" },
        { 0x24, "Home" },
        { 0x25, "左箭头 (Left)" },
        { 0x26, "上箭头 (Up)" },
        { 0x27, "右箭头 (Right)" },
        { 0x28, "下箭头 (Down)" },
        { 0x2C, "Print Screen" },
        { 0x2D, "Insert" },
        { 0x2E, "Delete" },
        { 0x5B, "左 Win" },
        { 0x5C, "右 Win" },
        { 0xA0, "左 Shift" },
        { 0xA1, "右 Shift" },
        { 0xA2, "左 Ctrl" },
        { 0xA3, "右 Ctrl" },
        { 0xA4, "左 Alt" },
        { 0xA5, "右 Alt" }
    };

    public MapperWindow(string dataDir)
    {
        _dataDir = dataDir;
        Closing += OnClosing;
        Closed += (s, e) =>
        {
            KeyMapperManager.Stop();
        };

        Title = "按键修改器";
        Width = 600;
        Height = 550;
        MinWidth = 550;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)); // 轻质雅灰

        // 绑定键盘录制逻辑到窗口
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        InitUi();
        UpdateStatusUi();
        RefreshActiveMapsUi();
        RefreshSystemMapsUi();
        RefreshList();
    }

    private void InitUi()
    {
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header (0)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Active Maps Badge Area (1)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // System Registry Maps Area (2)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // New rule card (3)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search bar (4)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // List Header (5) (NEW)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scroll list (6)

        // --- Row 0: Header Area ---
        var headerPanel = new DockPanel { Margin = new Thickness(20, 15, 20, 10) };
        
        var titleTxt = new TextBlock
        {
            Text = "按键修改器",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(33, 37, 41)),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(titleTxt, Dock.Left);
        headerPanel.Children.Add(titleTxt);

        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(statusPanel, Dock.Right);

        StatusDot = new System.Windows.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.Gray,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPanel.Children.Add(StatusDot);

        StatusText = new TextBlock
        {
            Text = "正在获取状态...",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0)
        };
        statusPanel.Children.Add(StatusText);

        ToggleServiceBtn = new Button
        {
            Content = "暂停服务",
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromRgb(13, 110, 253)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Medium,
            Cursor = Cursors.Hand
        };
        ToggleServiceBtn.Click += ToggleServiceBtn_Click;
        statusPanel.Children.Add(ToggleServiceBtn);

        headerPanel.Children.Add(statusPanel);
        Grid.SetRow(headerPanel, 0);
        mainGrid.Children.Add(headerPanel);

        // --- Row 1: Active Maps Badge Area (NEW) ---
        ActiveMapsBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(241, 243, 245)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(20, 5, 20, 5),
            Padding = new Thickness(15, 10, 15, 10),
            Visibility = Visibility.Collapsed
        };

        var activeMapsGrid = new Grid();
        activeMapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        activeMapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        ActiveMapsPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(ActiveMapsPanel, 0);
        activeMapsGrid.Children.Add(ActiveMapsPanel);

        ResetAllBtn = new Button
        {
            Content = "一键还原",
            Height = 26,
            Width = 70,
            Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Medium,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0)
        };
        ResetAllBtn.Click += ResetAllBtn_Click;
        Grid.SetColumn(ResetAllBtn, 1);
        activeMapsGrid.Children.Add(ResetAllBtn);

        ActiveMapsBorder.Child = activeMapsGrid;
        Grid.SetRow(ActiveMapsBorder, 1);
        mainGrid.Children.Add(ActiveMapsBorder);

        // --- Row 2: System Registry Maps Area (NEW) ---
        SystemMapsBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 243, 205)), // 暖黄底色警告提示
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 238, 186)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(20, 5, 20, 5),
            Padding = new Thickness(15, 10, 15, 10),
            Visibility = Visibility.Collapsed
        };

        var systemMapsGrid = new Grid();
        systemMapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        systemMapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        SystemMapsPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(SystemMapsPanel, 0);
        systemMapsGrid.Children.Add(SystemMapsPanel);

        ClearSystemMapsBtn = new Button
        {
            Content = "清除系统映射",
            Height = 26,
            Width = 90,
            Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Medium,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0)
        };
        ClearSystemMapsBtn.Click += ClearSystemMapsBtn_Click;
        Grid.SetColumn(ClearSystemMapsBtn, 1);
        systemMapsGrid.Children.Add(ClearSystemMapsBtn);

        SystemMapsBorder.Child = systemMapsGrid;
        Grid.SetRow(SystemMapsBorder, 2);
        mainGrid.Children.Add(SystemMapsBorder);

        // --- Row 3: New Rule Card ---
        var newRuleBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(20, 5, 20, 10),
            Padding = new Thickness(20, 15, 20, 15)
        };

        var newRuleGrid = new Grid();
        newRuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // From key record
        newRuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Arrow
        newRuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // To key record
        newRuleGrid.ColumnDefinitions.Add(new GridLength(1, GridUnitType.Star) == null ? new ColumnDefinition() : new ColumnDefinition { Width = GridLength.Auto }); // Add btn

        // Record From
        var fromPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        fromPanel.Children.Add(new TextBlock { Text = "按下物理键", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)), Margin = new Thickness(0,0,0,5) });
        FromKeyBtn = new Button { Content = "点击录制按键", Height = 36, Background = new SolidColorBrush(Color.FromRgb(241, 243, 245)), BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218)), Cursor = Cursors.Hand };
        FromKeyBtn.Click += (s, e) => StartRecord(RecordingState.FromKey);
        fromPanel.Children.Add(FromKeyBtn);
        Grid.SetColumn(fromPanel, 0);
        newRuleGrid.Children.Add(fromPanel);

        // Arrow
        var arrowTxt = new TextBlock
        {
            Text = "➜",
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromRgb(173, 181, 189)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 15, 10, 0)
        };
        Grid.SetColumn(arrowTxt, 1);
        newRuleGrid.Children.Add(arrowTxt);

        // Record To
        var toPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
        toPanel.Children.Add(new TextBlock { Text = "替换为目标键", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)), Margin = new Thickness(0,0,0,5) });
        ToKeyBtn = new Button { Content = "点击录制按键", Height = 36, Background = new SolidColorBrush(Color.FromRgb(241, 243, 245)), BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218)), Cursor = Cursors.Hand };
        ToKeyBtn.Click += (s, e) => StartRecord(RecordingState.ToKey);
        toPanel.Children.Add(ToKeyBtn);
        Grid.SetColumn(toPanel, 2);
        newRuleGrid.Children.Add(toPanel);

        // Add Button
        AddRuleBtn = new Button
        {
            Content = "添加映射",
            Height = 36,
            Width = 90,
            Background = new SolidColorBrush(Color.FromRgb(25, 135, 84)), // 绿色
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.Hand
        };
        AddRuleBtn.Click += AddRuleBtn_Click;
        Grid.SetColumn(AddRuleBtn, 3);
        newRuleGrid.Children.Add(AddRuleBtn);

        newRuleBorder.Child = newRuleGrid;
        Grid.SetRow(newRuleBorder, 3);
        mainGrid.Children.Add(newRuleBorder);

        // --- Row 4: Search Bar ---
        var searchPanel = new Grid { Margin = new Thickness(20, 5, 20, 10) };
        SearchTextBox = new TextBox
        {
            Height = 36,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218))
        };
        SearchTextBox.TextChanged += (s, e) => RefreshList();

        // 搜索栏水印文本
        var searchWatermark = new TextBlock
        {
            Text = "搜索规则...",
            FontSize = 13,
            Foreground = Brushes.Gray,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 0, 0)
        };
        
        SearchTextBox.GotFocus += (s, e) => searchWatermark.Visibility = Visibility.Collapsed;
        SearchTextBox.LostFocus += (s, e) => searchWatermark.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        searchPanel.Children.Add(SearchTextBox);
        searchPanel.Children.Add(searchWatermark);
        Grid.SetRow(searchPanel, 4);
        mainGrid.Children.Add(searchPanel);

        // --- Row 5: Table Header (NEW) ---
        var listHeader = CreateListHeader();
        Grid.SetRow(listHeader, 5);
        mainGrid.Children.Add(listHeader);

        // --- Row 6: Rule ScrollList ---
        RuleListPanel = new StackPanel { Margin = new Thickness(20, 5, 20, 20) };
        var scrollViewer = new ScrollViewer
        {
            Content = RuleListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 0)
        };
        Grid.SetRow(scrollViewer, 6);
        mainGrid.Children.Add(scrollViewer);

        Content = mainGrid;
    }

    private UIElement CreateListHeader()
    {
        var border = new Border
        {
            Padding = new Thickness(15, 5, 15, 5),
            Margin = new Thickness(20, 5, 20, 0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });      // 启用 CheckBox (0)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // From key name (1)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });      // Arrow (2)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // To key name (3)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });      // 永久 CheckBox (4)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });      // Del btn (5)

        // Column 0: 启用
        var hEnable = new TextBlock
        {
            Text = "启用",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(hEnable, 0);
        grid.Children.Add(hEnable);

        // Column 1: 源按键
        var hFrom = new TextBlock
        {
            Text = "物理按键",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(hFrom, 1);
        grid.Children.Add(hFrom);

        // Column 2: 占位
        var hArrow = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(hArrow, 2);
        grid.Children.Add(hArrow);

        // Column 3: 目标按键
        var hTo = new TextBlock
        {
            Text = "目标按键",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(hTo, 3);
        grid.Children.Add(hTo);

        // Column 4: 永久(底层)
        var hPerm = new TextBlock
        {
            Text = "永久(系统)",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(hPerm, 4);
        grid.Children.Add(hPerm);

        // Column 5: 操作
        var hAction = new TextBlock
        {
            Text = "操作",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(hAction, 5);
        grid.Children.Add(hAction);

        border.Child = grid;
        return border;
    }

    private void RefreshSystemMapsUi()
    {
        SystemMapsPanel.Children.Clear();
        var sysRules = KeyMapperManager.GetRegistryMappings();
        if (sysRules.Count == 0)
        {
            SystemMapsBorder.Visibility = Visibility.Collapsed;
            return;
        }

        SystemMapsBorder.Visibility = Visibility.Visible;

        SystemMapsPanel.Children.Add(new TextBlock
        {
            Text = "⚠️ 检测到系统底层(注册表)已有映射: ",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(133, 100, 4)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        foreach (var rule in sysRules)
        {
            var badgeBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 238, 186)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 222, 142)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            var txt = new TextBlock
            {
                Text = $"{rule.FromName} ➜ {rule.ToName}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(133, 100, 4)),
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = txt;
            SystemMapsPanel.Children.Add(badgeBorder);
        }
    }

    private void ClearSystemMapsBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "清除系统底层映射需要修改注册表，且必须重启电脑才能生效。\n确定要清除所有系统层面的按键映射吗？",
            "清除系统按键映射",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.Yes)
        {
            if (KeyMapperManager.ClearRegistryMappings(out string error))
            {
                MessageBox.Show(
                    "清除系统底层按键映射成功！\n请重启电脑以使更改生效。",
                    "清除成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                RefreshSystemMapsUi();
            }
            else
            {
                MessageBox.Show(
                    $"清除失败: {error}\n请尝试以管理员身份运行主程序后重试。",
                    "清除失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }

    private void ResetAllBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定要还原（禁用）所有当前的按键映射吗？", "一键还原", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            // 在修改前把目前启用的按键全部释放，防止卡住
            lock (KeyMapperManager.EnabledRules)
            {
                foreach (var targetVk in KeyMapperManager.EnabledRules.Values)
                {
                    KeyMapperManager.keybd_event((byte)targetVk, 0, KeyMapperManager.KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }

            foreach (var rule in KeyMapperManager.AllRules)
            {
                rule.Enabled = false;
            }
            KeyMapperManager.UpdateRegistryMappings(out _);
            KeyMapperManager.SaveRules();

            RefreshActiveMapsUi();
            RefreshSystemMapsUi();
            RefreshList();
        }
    }

    private void RefreshActiveMapsUi()
    {
        ActiveMapsPanel.Children.Clear();
        
        var activeRules = KeyMapperManager.AllRules.FindAll(r => r.Enabled);
        if (activeRules.Count == 0)
        {
            ActiveMapsBorder.Visibility = Visibility.Collapsed;
            return;
        }
        
        ActiveMapsBorder.Visibility = Visibility.Visible;
        
        ActiveMapsPanel.Children.Add(new TextBlock
        {
            Text = "当前生效映射: ",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        
        foreach (var rule in activeRules)
        {
            var badgeBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(224, 238, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(188, 216, 253)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var badgeGrid = new Grid();
            badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            badgeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var txt = new TextBlock
            {
                Text = $"{rule.FromName} ➜ {rule.ToName}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(13, 110, 253)),
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txt, 0);
            badgeGrid.Children.Add(txt);
            
            var closeBtn = new TextBlock
            {
                Text = "×",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
                Margin = new Thickness(6, -1, 0, 1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            closeBtn.MouseEnter += (s, e) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            closeBtn.MouseLeave += (s, e) => closeBtn.Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125));
            
            var targetRule = rule;
            closeBtn.MouseDown += (s, e) =>
            {
                targetRule.Enabled = false;
                
                // 释放目标按键逻辑状态
                KeyMapperManager.keybd_event((byte)targetRule.ToVk, 0, KeyMapperManager.KEYEVENTF_KEYUP, UIntPtr.Zero);
                
                KeyMapperManager.SaveRules();
                
                RefreshActiveMapsUi();
                RefreshList();
            };
            
            Grid.SetColumn(closeBtn, 2);
            badgeGrid.Children.Add(closeBtn);
            
            badgeBorder.Child = badgeGrid;
            ActiveMapsPanel.Children.Add(badgeBorder);
        }
    }

    private void StartRecord(RecordingState state)
    {
        _recordingState = state;
        if (state == RecordingState.FromKey)
        {
            FromKeyBtn.Content = "正在录制... 按下任意键";
            FromKeyBtn.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205)); // 暖黄
            FromKeyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
        }
        else if (state == RecordingState.ToKey)
        {
            ToKeyBtn.Content = "正在录制... 按下任意键";
            ToKeyBtn.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205));
            ToKeyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void Quit()
    {
        _forceClose = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            KeyMapperManager.Stop();
            Close();
        }));
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingState == RecordingState.None) return;

        e.Handled = true;

        Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) return;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        string keyName = GetKeyFriendlyName(vk, key);

        if (_recordingState == RecordingState.FromKey)
        {
            _pendingFromVk = vk;
            _pendingFromName = keyName;
            FromKeyBtn.Content = keyName;
            FromKeyBtn.Background = new SolidColorBrush(Color.FromRgb(241, 243, 245));
            FromKeyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218));
        }
        else if (_recordingState == RecordingState.ToKey)
        {
            _pendingToVk = vk;
            _pendingToName = keyName;
            ToKeyBtn.Content = keyName;
            ToKeyBtn.Background = new SolidColorBrush(Color.FromRgb(241, 243, 245));
            ToKeyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218));
        }

        _recordingState = RecordingState.None;
    }

    private void AddRuleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingFromVk == 0 || _pendingToVk == 0)
        {
            MessageBox.Show("请先录制源按键和目标按键。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 避免重复的 From 键映射
        if (KeyMapperManager.AllRules.Exists(r => r.FromVk == _pendingFromVk))
        {
            MessageBox.Show($"已经存在源按键为 '{_pendingFromName}' 的映射规则。", "添加失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newRule = new MappingRule
        {
            Id = "rule_" + Guid.NewGuid().ToString("N")[..8],
            FromVk = _pendingFromVk,
            FromName = _pendingFromName,
            ToVk = _pendingToVk,
            ToName = _pendingToName,
            Enabled = true
        };

        KeyMapperManager.AllRules.Add(newRule);
        KeyMapperManager.SaveRules();
        
        // 重置录制框
        _pendingFromVk = 0;
        _pendingFromName = string.Empty;
        _pendingToVk = 0;
        _pendingToName = string.Empty;

        FromKeyBtn.Content = "点击录制按键";
        ToKeyBtn.Content = "点击录制按键";

        RefreshActiveMapsUi();
        RefreshList();
    }

    private void ToggleServiceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (KeyMapperManager.IsRunning)
        {
            KeyMapperManager.Stop();
        }
        else
        {
            KeyMapperManager.Start(_dataDir);
        }
        UpdateStatusUi();
    }

    private void UpdateStatusUi()
    {
        if (KeyMapperManager.IsRunning)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // 绿
            StatusText.Text = "映射服务已启用";
            ToggleServiceBtn.Content = "暂停服务";
            ToggleServiceBtn.Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // 红
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // 红
            StatusText.Text = "映射服务已暂停";
            ToggleServiceBtn.Content = "启动服务";
            ToggleServiceBtn.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // 绿
        }
    }

    private void RefreshList()
    {
        RuleListPanel.Children.Clear();
        string filter = SearchTextBox.Text.Trim().ToLower();

        foreach (var rule in KeyMapperManager.AllRules)
        {
            if (!string.IsNullOrEmpty(filter))
            {
                bool match = rule.FromName.ToLower().Contains(filter) || rule.ToName.ToLower().Contains(filter);
                if (!match) continue;
            }

            RuleListPanel.Children.Add(CreateRuleCard(rule));
        }
    }

    private UIElement CreateRuleCard(MappingRule rule)
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(15, 10, 15, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });      // 启用 CheckBox (0)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // From key name (1)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });      // Arrow (2)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // To key name (3)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });      // 永久 CheckBox (4)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });      // Del btn (5)

        // Column 0: 启用 CheckBox
        var cb = new CheckBox
        {
            IsChecked = rule.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand
        };
        cb.Checked += (s, e) =>
        {
            rule.Enabled = true;
            bool success = true;
            if (rule.IsPermanent)
            {
                if (!KeyMapperManager.UpdateRegistryMappings(out string error))
                {
                    rule.Enabled = false;
                    cb.IsChecked = false;
                    MessageBox.Show($"更新系统底层映射失败: {error}\n请尝试以管理员身份运行主程序后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    success = false;
                }
            }
            if (success)
            {
                KeyMapperManager.SaveRules();
                RefreshActiveMapsUi();
                RefreshSystemMapsUi();
            }
        };
        cb.Unchecked += (s, e) =>
        {
            rule.Enabled = false;
            bool success = true;
            if (rule.IsPermanent)
            {
                if (!KeyMapperManager.UpdateRegistryMappings(out string error))
                {
                    rule.Enabled = true;
                    cb.IsChecked = true;
                    MessageBox.Show($"更新系统底层映射失败: {error}\n请尝试以管理员身份运行主程序后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    success = false;
                }
            }
            if (success)
            {
                KeyMapperManager.SaveRules();
                RefreshActiveMapsUi();
                RefreshSystemMapsUi();
            }
        };
        Grid.SetColumn(cb, 0);
        grid.Children.Add(cb);

        // Column 1: 源按键名称
        var fromTxt = new TextBlock
        {
            Text = rule.FromName,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(33, 37, 41)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(fromTxt, 1);
        grid.Children.Add(fromTxt);

        // Column 2: 箭头
        var arrow = new TextBlock
        {
            Text = "➜",
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(173, 181, 189)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        // Column 3: 目标按键名称
        var toTxt = new TextBlock
        {
            Text = rule.ToName,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(13, 110, 253)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(toTxt, 3);
        grid.Children.Add(toTxt);

        // Column 4: 永久 CheckBox
        var permCb = new CheckBox
        {
            IsChecked = rule.IsPermanent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "将此映射写入系统注册表底层，即使关闭修改器也一直有效（需重启电脑）"
        };
        permCb.Checked += (s, e) =>
        {
            rule.IsPermanent = true;
            if (KeyMapperManager.UpdateRegistryMappings(out string error))
            {
                KeyMapperManager.SaveRules();
                RefreshActiveMapsUi();
                RefreshSystemMapsUi();
                if (rule.Enabled)
                {
                    MessageBox.Show("已成功写入系统底层映射，请重启电脑使新键位生效！", "系统映射已修改", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                rule.IsPermanent = false;
                permCb.IsChecked = false;
                MessageBox.Show($"写入系统映射失败: {error}\n请尝试以管理员身份运行主程序重试。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        permCb.Unchecked += (s, e) =>
        {
            rule.IsPermanent = false;
            if (KeyMapperManager.UpdateRegistryMappings(out string error))
            {
                KeyMapperManager.SaveRules();
                RefreshActiveMapsUi();
                RefreshSystemMapsUi();
                MessageBox.Show("已从系统底层撤销该映射，请重启电脑以恢复默认键位！", "系统映射已撤销", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                rule.IsPermanent = true;
                permCb.IsChecked = true;
                MessageBox.Show($"撤销系统映射失败: {error}\n请尝试以管理员身份运行主程序重试。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        Grid.SetColumn(permCb, 4);
        grid.Children.Add(permCb);

        // Column 5: 删除按钮
        var delBtn = new Button
        {
            Content = "删除",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Padding = new Thickness(3)
        };
        delBtn.Click += (s, e) =>
        {
            KeyMapperManager.AllRules.Remove(rule);
            if (rule.IsPermanent && rule.Enabled)
            {
                KeyMapperManager.UpdateRegistryMappings(out _);
            }
            KeyMapperManager.SaveRules();
            RefreshActiveMapsUi();
            RefreshSystemMapsUi();
            RefreshList();
        };
        Grid.SetColumn(delBtn, 5);
        grid.Children.Add(delBtn);

        border.Child = grid;

        border.MouseEnter += (s, e) => border.BorderBrush = new SolidColorBrush(Color.FromRgb(13, 110, 253));
        border.MouseLeave += (s, e) => border.BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230));

        return border;
    }

    private static string GetKeyFriendlyName(int vk, Key wpfKey)
    {
        if (KeyNames.TryGetValue(vk, out var name))
        {
            return name;
        }
        try
        {
            return wpfKey.ToString();
        }
        catch
        {
            return "未知按键(" + vk + ")";
        }
    }
}

public class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var tcs = new TaskCompletionSource<string>();
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                // 确保服务已在运行
                KeyMapperManager.Start(context.ExtensionDataDirectory);

                // 尝试获取已经注册的窗口
                var existing = context.GetRegisteredObject?.Invoke("keyboard-mapper-window") as Window;
                if (existing != null)
                {
                    if (context.LaunchSource != "app-startup")
                    {
                        // 从启动器/面板呼出：恢复并前置窗口
                        existing.ShowInTaskbar = true;
                        existing.WindowState = WindowState.Normal;
                        existing.Show();
                        existing.Activate();
                    }
                    tcs.TrySetResult("Already running, shown/activated");
                    return;
                }

                // 第一次运行，创建窗口并注册
                var win = new MapperWindow(context.ExtensionDataDirectory);
                context.RegisterObject?.Invoke("keyboard-mapper-window", win);

                // 仅在窗口彻底关闭时完成常驻任务并清空注册引用
                win.Closed += (s, e) => {
                    context.RegisterObject?.Invoke("keyboard-mapper-window", null!);
                    tcs.TrySetResult("Window Closed");
                };

                if (context.LaunchSource == "app-startup")
                {
                    // 自启动只加载键盘钩子并保留一个隐藏窗口作为常驻实例。
                    // 先以最小化状态创建 HWND，随即 Hide，确保无任务栏图标也不闪现。
                    win.ShowActivated = false;
                    win.ShowInTaskbar = false;
                    win.WindowState = WindowState.Minimized;
                    win.Show();
                    win.Hide();
                    win.WindowState = WindowState.Normal;
                }
                else
                {
                    win.Show();
                    win.Activate();
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult("Error: " + ex.Message);
            }
        });

        return await tcs.Task;
    }
}
