using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    private static KeyboardCleanerWindow? _currentWindow;
    private static readonly object _lock = new();

    public static async Task<string> RunAsync(YanziActionContext context)
    {
        lock (_lock)
        {
            if (_currentWindow != null && _currentWindow.IsLoaded)
            {
                _currentWindow.Dispatcher.Invoke(() =>
                {
                    if (_currentWindow.WindowState == WindowState.Minimized)
                        _currentWindow.WindowState = WindowState.Normal;
                    _currentWindow.Activate();
                });
                return "键盘清洁窗口已激活";
            }
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                var window = new KeyboardCleanerWindow(context);
                lock (_lock)
                {
                    _currentWindow = window;
                }

                window.Closed += (_, _) =>
                {
                    lock (_lock)
                    {
                        if (_currentWindow == window)
                        {
                            _currentWindow = null;
                        }
                    }
                    tcs.TrySetResult(window.SummaryResult);
                };

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                context.Log("键盘清洁窗口异常: " + ex);
                tcs.TrySetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Name = "YanziKeyboardCleaner";
        thread.Start();

        return await tcs.Task;
    }
}

public sealed class KeyboardCleanerWindow : Window
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // SVG 矢量路径定义
    private const string SvgLock = "M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z";
    private const string SvgClose = "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z";
    private const string SvgKeyboard = "M20 5H4c-1.1 0-1.99.9-1.99 2L2 17c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm-9 3h2v2h-2V8zm0 3h2v2h-2v-2zM8 8h2v2H8V8zm0 3h2v2H8v-2zm-1 2H5v-2h2v2zm0-3H5V8h2v2zm9 7H8v-2h8v2zm0-4h-2v-2h2v2zm0-3h-2V8h2v2zm3 3h-2v-2h2v2zm0-3h-2V8h2v2z";
    // 带斜杠的键盘图标 (Keyboard Off)
    private const string SvgKeyboardOff = "M1.27 1.27L0 2.55l2.42 2.42C2.16 5.37 2 5.86 2 6.4v11.2C2 18.92 3.08 20 4.4 20h14.77l2.28 2.27 1.28-1.27L1.27 1.27zM4.4 18.4c-.44 0-.8-.36-.8-.8V6.8l12.4 12.4H4.4zm15.2-1.97V6.4c0-.44-.36-.8-.8-.8H7.21l-1.6-1.6H18.8c1.32 0 2.4 1.08 2.4 2.4v10.03l-1.6-1.6zM8 8h1.17l2 2H8V8zm4 0h1.17l2 2H12V8zm4 0h2v2h-2V8zm0 4h2v2h-2v-2z";
    private const string SvgGrid = "M3 3v18h18V3H3zm16 16H5V5h14v14zM11 7H7v4h4V7zm6 0h-4v4h4V7zm-6 6H7v4h4v-4zm6 0h-4v4h4v-4z";

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static LowLevelKeyboardProc? _hookProc;
    private static IntPtr _hookId = IntPtr.Zero;
    private static KeyboardCleanerWindow? _activeInstance;

    private readonly YanziActionContext _context;
    private int _blockedCount = 0;
    private bool _isLocked = true;
    private bool _isMapExpanded = false;
    private string _currentLayoutMode = "104";

    // UI 组件
    private Button _toggleLockBtn = null!;
    private Button _toggleMapBtn = null!;
    private Border _mapPanel = null!;

    private readonly Dictionary<uint, List<Border>> _keyVisualMap = new();

    private StackPanel _navClusterCol = null!;
    private Grid _numpadGrid = null!;
    private StackPanel _fRowPanel = null!;

    public string SummaryResult { get; private set; } = "已完成键盘清理";

    private static SolidColorBrush ColorBrush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private static Viewbox CreateSvgIcon(string pathData, Brush fill, double size)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            Fill = fill,
            Stretch = Stretch.Uniform
        };
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = path,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public KeyboardCleanerWindow(YanziActionContext context)
    {
        _context = context;
        _activeInstance = this;

        Title = "键盘清洁锁 · 燕子";
        Width = 320;
        Height = 112;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

        // 彻底禁用窗口内部所有按键导航焦点，杜绝按键误触发按钮点击
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        Focusable = false;

        BuildUi();

        Loaded += (_, _) =>
        {
            StartKeyboardLock();
        };

        Closed += (_, _) =>
        {
            StopKeyboardLock();
            if (_activeInstance == this)
            {
                _activeInstance = null;
            }
        };
    }

    private void BuildUi()
    {
        // 纯正黑灰科技感外框（无蓝色）
        var outerCard = new Border
        {
            Background = ColorBrush("#F5111827"),
            BorderBrush = ColorBrush("#334155"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 12, 16, 14),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 5,
                Opacity = 0.6
            }
        };

        // 让全窗口任意非按钮区域均可随时鼠标按住自由拖动
        outerCard.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            {
                try { DragMove(); } catch { }
            }
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 顶部标题栏
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 纯粹按钮区
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 展开键位图区

        // 1. 顶部标题栏（锁图标 + 标题 + 右上角 SVG 叉号）
        var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        // 左上角锁图标：黑灰/银灰质感
        titleStack.Children.Add(CreateSvgIcon(SvgLock, ColorBrush("#94A3B8"), 15));
        titleStack.Children.Add(new TextBlock
        {
            Text = "键盘清洁锁",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColorBrush("#F1F5F9"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        });
        titleBar.Children.Add(titleStack);

        var closeBtn = new Button
        {
            Content = CreateSvgIcon(SvgClose, ColorBrush("#94A3B8"), 12),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(6),
            ToolTip = "关闭退出",
            Focusable = false // 禁用焦点，防止擦键盘时误触
        };
        closeBtn.MouseEnter += (_, _) =>
        {
            if (closeBtn.Content is Viewbox vb && vb.Child is System.Windows.Shapes.Path p)
                p.Fill = Brushes.White;
        };
        closeBtn.MouseLeave += (_, _) =>
        {
            if (closeBtn.Content is Viewbox vb && vb.Child is System.Windows.Shapes.Path p)
                p.Fill = ColorBrush("#94A3B8");
        };
        closeBtn.Click += (_, _) =>
        {
            StopKeyboardLock();
            SummaryResult = $"用户手动退出，本次共安全拦截 {_blockedCount} 次按键误触";
            Close();
        };
        Grid.SetColumn(closeBtn, 1);
        titleBar.Children.Add(closeBtn);
        rootGrid.Children.Add(titleBar);

        // 2. 主操作区：只放 [状态按钮] 和 [键位按钮]
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 状态按钮：显示当前状态名（键盘已禁用 / 键盘已启用）
        _toggleLockBtn = new Button
        {
            Width = 138,
            Height = 38,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0),
            Focusable = false // 禁用焦点，防止擦键盘时被空格/回车误触触发
        };
        ApplyLockButtonStyle(_toggleLockBtn, _isLocked);
        _toggleLockBtn.Click += (_, _) => ToggleLockState();
        btnRow.Children.Add(_toggleLockBtn);

        // 键位按钮（黑灰配色）
        _toggleMapBtn = new Button
        {
            Width = 125,
            Height = 38,
            Cursor = Cursors.Hand,
            Focusable = false // 禁用焦点
        };
        ApplyMapButtonStyle(_toggleMapBtn, _isMapExpanded);
        _toggleMapBtn.Click += (_, _) => ToggleKeyboardMap();
        btnRow.Children.Add(_toggleMapBtn);

        Grid.SetRow(btnRow, 1);
        rootGrid.Children.Add(btnRow);

        // 3. 键位图面板 (默认收起)
        _mapPanel = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = ColorBrush("#111827"),
            BorderBrush = ColorBrush("#2E384D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 12),
            Margin = new Thickness(0, 12, 0, 0)
        };

        var mapContainer = new StackPanel();

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        tabs.Children.Add(CreateLayoutTab("104", "104键", true));
        tabs.Children.Add(CreateLayoutTab("87", "87键", false));
        tabs.Children.Add(CreateLayoutTab("68", "68键", false));
        mapContainer.Children.Add(tabs);

        var keyboardBody = BuildVisualKeyboardGrid();
        mapContainer.Children.Add(keyboardBody);

        _mapPanel.Child = mapContainer;
        Grid.SetRow(_mapPanel, 2);
        rootGrid.Children.Add(_mapPanel);

        outerCard.Child = rootGrid;
        Content = outerCard;
    }

    private void ApplyLockButtonStyle(Button btn, bool isLocked)
    {
        // isLocked == true: 键盘已禁用（黑灰背景，带斜杠的键盘图标）
        // isLocked == false: 键盘已启用（绿色背景，正常键盘图标）
        string text = isLocked ? "键盘已禁用" : "键盘已启用";
        string svg = isLocked ? SvgKeyboardOff : SvgKeyboard;
        Brush bg = isLocked ? ColorBrush("#243044") : ColorBrush("#10B981");
        Brush bgHover = isLocked ? ColorBrush("#334155") : ColorBrush("#059669");
        Brush borderBrush = isLocked ? ColorBrush("#3B4A61") : ColorBrush("#34D399");
        Brush fg = isLocked ? ColorBrush("#E2E8F0") : Brushes.White;
        Brush iconBrush = isLocked ? ColorBrush("#94A3B8") : Brushes.White;

        btn.Template = BuildButtonTemplate(text, svg, bg, bgHover, borderBrush, fg, iconBrush);
    }

    private void ApplyMapButtonStyle(Button btn, bool isExpanded)
    {
        // 黑灰质感（无蓝色）
        string text = isExpanded ? "收起键位" : "键位图";
        Brush bg = isExpanded ? ColorBrush("#334155") : ColorBrush("#1E293B");
        Brush bgHover = isExpanded ? ColorBrush("#475569") : ColorBrush("#2B394E");
        Brush borderBrush = isExpanded ? ColorBrush("#64748B") : ColorBrush("#334155");
        Brush fg = isExpanded ? Brushes.White : ColorBrush("#CBD5E1");
        Brush iconBrush = isExpanded ? Brushes.White : ColorBrush("#94A3B8");

        btn.Template = BuildButtonTemplate(text, SvgGrid, bg, bgHover, borderBrush, fg, iconBrush);
    }

    private static ControlTemplate BuildButtonTemplate(string text, string svgPath, Brush bg, Brush bgHover, Brush borderBrush, Brush fg, Brush iconBrush)
    {
        var template = new ControlTemplate(typeof(Button));

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "btnBorder";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        borderFactory.SetValue(Border.BackgroundProperty, bg);
        borderFactory.SetValue(Border.BorderBrushProperty, borderBrush);
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

        var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
        stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        stackFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        stackFactory.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

        var pathFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        pathFactory.Name = "iconPath";
        pathFactory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse(svgPath));
        pathFactory.SetValue(System.Windows.Shapes.Path.FillProperty, iconBrush);
        pathFactory.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.Uniform);

        var viewboxFactory = new FrameworkElementFactory(typeof(Viewbox));
        viewboxFactory.SetValue(Viewbox.WidthProperty, 15.0);
        viewboxFactory.SetValue(Viewbox.HeightProperty, 15.0);
        viewboxFactory.SetValue(Viewbox.VerticalAlignmentProperty, VerticalAlignment.Center);
        viewboxFactory.AppendChild(pathFactory);
        stackFactory.AppendChild(viewboxFactory);

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.Name = "btnText";
        textFactory.SetValue(TextBlock.TextProperty, text);
        textFactory.SetValue(TextBlock.FontSizeProperty, 12.5);
        textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        textFactory.SetValue(TextBlock.ForegroundProperty, fg);
        textFactory.SetValue(TextBlock.MarginProperty, new Thickness(7, 0, 0, 0));
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        stackFactory.AppendChild(textFactory);

        borderFactory.AppendChild(stackFactory);

        var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, bgHover, "btnBorder"));
        template.Triggers.Add(trigger);

        template.VisualTree = borderFactory;
        return template;
    }

    private void ToggleLockState()
    {
        if (_isLocked)
        {
            // 切换为：键盘已启用（解除屏蔽，按钮变为黑灰）
            StopKeyboardLock();
            _isLocked = false;
            ApplyLockButtonStyle(_toggleLockBtn, false);
            _context.Log("键盘已启用，恢复正常输入");
        }
        else
        {
            // 切换为：键盘已禁用（重新屏蔽，按钮变为绿色）
            StartKeyboardLock();
            _isLocked = true;
            ApplyLockButtonStyle(_toggleLockBtn, true);
            _context.Log("键盘已禁用，按键已被屏蔽");
        }
    }

    private Button CreateLayoutTab(string mode, string label, bool isSelected)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = isSelected ? ColorBrush("#334155") : ColorBrush("#1E293B"),
            Padding = new Thickness(10, 4, 10, 4)
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = isSelected ? Brushes.White : ColorBrush("#94A3B8"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Child = text;

        var btn = new Button
        {
            Content = border,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(0),
            Focusable = false
        };

        btn.Click += (_, _) =>
        {
            SwitchLayout(mode);
            if (btn.Parent is StackPanel panel)
            {
                foreach (Button child in panel.Children.OfType<Button>())
                {
                    bool sel = child == btn;
                    if (child.Content is Border b && b.Child is TextBlock t)
                    {
                        b.Background = sel ? ColorBrush("#334155") : ColorBrush("#1E293B");
                        t.Foreground = sel ? Brushes.White : ColorBrush("#94A3B8");
                    }
                }
            }
        };
        return btn;
    }

    private void SwitchLayout(string mode)
    {
        _currentLayoutMode = mode;
        if (mode == "104")
        {
            _fRowPanel.Visibility = Visibility.Visible;
            _navClusterCol.Visibility = Visibility.Visible;
            _numpadGrid.Visibility = Visibility.Visible;
        }
        else if (mode == "87")
        {
            _fRowPanel.Visibility = Visibility.Visible;
            _navClusterCol.Visibility = Visibility.Visible;
            _numpadGrid.Visibility = Visibility.Collapsed;
        }
        else if (mode == "68")
        {
            _fRowPanel.Visibility = Visibility.Collapsed;
            _navClusterCol.Visibility = Visibility.Visible;
            _numpadGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void ToggleKeyboardMap()
    {
        _isMapExpanded = !_isMapExpanded;
        ApplyMapButtonStyle(_toggleMapBtn, _isMapExpanded);

        if (_isMapExpanded)
        {
            _mapPanel.Visibility = Visibility.Visible;
            Width = 920;
            Height = 490;
        }
        else
        {
            _mapPanel.Visibility = Visibility.Collapsed;
            Width = 320;
            Height = 112;
        }

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
    }

    private UIElement BuildVisualKeyboardGrid()
    {
        var keyboardGrid = new Grid();
        keyboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        keyboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        keyboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 1. 主键区
        var mainArea = new StackPanel();

        _fRowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _fRowPanel.Children.Add(CreateKey(0x1B, "Esc", 1.0));
        _fRowPanel.Children.Add(new Rectangle { Width = 18 });
        _fRowPanel.Children.Add(CreateKey(0x70, "F1", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x71, "F2", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x72, "F3", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x73, "F4", 1.0));
        _fRowPanel.Children.Add(new Rectangle { Width = 12 });
        _fRowPanel.Children.Add(CreateKey(0x74, "F5", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x75, "F6", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x76, "F7", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x77, "F8", 1.0));
        _fRowPanel.Children.Add(new Rectangle { Width = 12 });
        _fRowPanel.Children.Add(CreateKey(0x78, "F9", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x79, "F10", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x7A, "F11", 1.0));
        _fRowPanel.Children.Add(CreateKey(0x7B, "F12", 1.0));
        mainArea.Children.Add(_fRowPanel);

        var numRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        numRow.Children.Add(CreateKey(0xC0, "`", "~", 1.0));
        numRow.Children.Add(CreateKey(0x31, "1", "!", 1.0));
        numRow.Children.Add(CreateKey(0x32, "2", "@", 1.0));
        numRow.Children.Add(CreateKey(0x33, "3", "#", 1.0));
        numRow.Children.Add(CreateKey(0x34, "4", "$", 1.0));
        numRow.Children.Add(CreateKey(0x35, "5", "%", 1.0));
        numRow.Children.Add(CreateKey(0x36, "6", "^", 1.0));
        numRow.Children.Add(CreateKey(0x37, "7", "&", 1.0));
        numRow.Children.Add(CreateKey(0x38, "8", "*", 1.0));
        numRow.Children.Add(CreateKey(0x39, "9", "(", 1.0));
        numRow.Children.Add(CreateKey(0x30, "0", ")", 1.0));
        numRow.Children.Add(CreateKey(0xBD, "-", "_", 1.0));
        numRow.Children.Add(CreateKey(0xBB, "=", "+", 1.0));
        numRow.Children.Add(CreateKey(0x08, "Backspace", 2.0));
        mainArea.Children.Add(numRow);

        var qRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        qRow.Children.Add(CreateKey(0x09, "Tab", 1.5));
        qRow.Children.Add(CreateKey(0x51, "Q", 1.0));
        qRow.Children.Add(CreateKey(0x57, "W", 1.0));
        qRow.Children.Add(CreateKey(0x45, "E", 1.0));
        qRow.Children.Add(CreateKey(0x52, "R", 1.0));
        qRow.Children.Add(CreateKey(0x54, "T", 1.0));
        qRow.Children.Add(CreateKey(0x59, "Y", 1.0));
        qRow.Children.Add(CreateKey(0x55, "U", 1.0));
        qRow.Children.Add(CreateKey(0x49, "I", 1.0));
        qRow.Children.Add(CreateKey(0x4F, "O", 1.0));
        qRow.Children.Add(CreateKey(0x50, "P", 1.0));
        qRow.Children.Add(CreateKey(0xDB, "[", "{", 1.0));
        qRow.Children.Add(CreateKey(0xDD, "]", "}", 1.0));
        qRow.Children.Add(CreateKey(0xDC, "\\", "|", 1.5));
        mainArea.Children.Add(qRow);

        var aRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        aRow.Children.Add(CreateKey(0x14, "Caps Lock", 1.75));
        aRow.Children.Add(CreateKey(0x41, "A", 1.0));
        aRow.Children.Add(CreateKey(0x53, "S", 1.0));
        aRow.Children.Add(CreateKey(0x44, "D", 1.0));
        aRow.Children.Add(CreateKey(0x46, "F", 1.0));
        aRow.Children.Add(CreateKey(0x47, "G", 1.0));
        aRow.Children.Add(CreateKey(0x48, "H", 1.0));
        aRow.Children.Add(CreateKey(0x4A, "J", 1.0));
        aRow.Children.Add(CreateKey(0x4B, "K", 1.0));
        aRow.Children.Add(CreateKey(0x4C, "L", 1.0));
        aRow.Children.Add(CreateKey(0xBA, ";", ":", 1.0));
        aRow.Children.Add(CreateKey(0xDE, "'", "\"", 1.0));
        aRow.Children.Add(CreateKey(0x0D, "Enter", 2.25));
        mainArea.Children.Add(aRow);

        var zRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        zRow.Children.Add(CreateKey(0xA0, "Shift", 2.25));
        zRow.Children.Add(CreateKey(0x5A, "Z", 1.0));
        zRow.Children.Add(CreateKey(0x58, "X", 1.0));
        zRow.Children.Add(CreateKey(0x43, "C", 1.0));
        zRow.Children.Add(CreateKey(0x56, "V", 1.0));
        zRow.Children.Add(CreateKey(0x42, "B", 1.0));
        zRow.Children.Add(CreateKey(0x4E, "N", 1.0));
        zRow.Children.Add(CreateKey(0x4D, "M", 1.0));
        zRow.Children.Add(CreateKey(0xBC, ",", "<", 1.0));
        zRow.Children.Add(CreateKey(0xBE, ".", ">", 1.0));
        zRow.Children.Add(CreateKey(0xBF, "/", "?", 1.0));
        zRow.Children.Add(CreateKey(0xA1, "Shift", 2.75));
        mainArea.Children.Add(zRow);

        var botRow = new StackPanel { Orientation = Orientation.Horizontal };
        botRow.Children.Add(CreateKey(0xA2, "Ctrl", 1.25));
        botRow.Children.Add(CreateKey(0x5B, "Win", 1.25));
        botRow.Children.Add(CreateKey(0xA4, "Alt", 1.25));
        botRow.Children.Add(CreateKey(0x20, "Spacebar (空格)", 6.25));
        botRow.Children.Add(CreateKey(0xA5, "Alt", 1.25));
        botRow.Children.Add(CreateKey(0x5C, "Win", 1.25));
        botRow.Children.Add(CreateKey(0x5D, "Menu", 1.25));
        botRow.Children.Add(CreateKey(0xA3, "Ctrl", 1.25));
        mainArea.Children.Add(botRow);

        keyboardGrid.Children.Add(mainArea);

        // 2. 编辑与方向键区
        _navClusterCol = new StackPanel();

        var prtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        prtRow.Children.Add(CreateKey(0x2C, "PrtSc", 1.0));
        prtRow.Children.Add(CreateKey(0x91, "ScrLk", 1.0));
        prtRow.Children.Add(CreateKey(0x13, "Pause", 1.0));
        _navClusterCol.Children.Add(prtRow);

        var nav1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        nav1.Children.Add(CreateKey(0x2D, "Ins", 1.0));
        nav1.Children.Add(CreateKey(0x24, "Home", 1.0));
        nav1.Children.Add(CreateKey(0x21, "PgUp", 1.0));
        _navClusterCol.Children.Add(nav1);

        var nav2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        nav2.Children.Add(CreateKey(0x2E, "Del", 1.0));
        nav2.Children.Add(CreateKey(0x23, "End", 1.0));
        nav2.Children.Add(CreateKey(0x22, "PgDn", 1.0));
        _navClusterCol.Children.Add(nav2);

        _navClusterCol.Children.Add(new Rectangle { Height = 40 });

        var arrowUpRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        arrowUpRow.Children.Add(CreateKey(0x26, "↑", 1.0));
        _navClusterCol.Children.Add(arrowUpRow);

        var arrowBotRow = new StackPanel { Orientation = Orientation.Horizontal };
        arrowBotRow.Children.Add(CreateKey(0x25, "←", 1.0));
        arrowBotRow.Children.Add(CreateKey(0x28, "↓", 1.0));
        arrowBotRow.Children.Add(CreateKey(0x27, "→", 1.0));
        _navClusterCol.Children.Add(arrowBotRow);

        Grid.SetColumn(_navClusterCol, 2);
        keyboardGrid.Children.Add(_navClusterCol);

        // 3. 数字小键盘区
        var numpadContainer = new StackPanel();
        numpadContainer.Children.Add(new Rectangle { Height = 40 });

        _numpadGrid = new Grid();
        for (int i = 0; i < 4; i++) _numpadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 5; i++) _numpadGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddNumpadKey(UIElement el, int row, int col, int rowSpan = 1, int colSpan = 1)
        {
            Grid.SetRow(el, row);
            Grid.SetColumn(el, col);
            if (rowSpan > 1) Grid.SetRowSpan(el, rowSpan);
            if (colSpan > 1) Grid.SetColumnSpan(el, colSpan);
            _numpadGrid.Children.Add(el);
        }

        AddNumpadKey(CreateKey(0x90, "Num", 1.0), 0, 0);
        AddNumpadKey(CreateKey(0x6F, "/", 1.0), 0, 1);
        AddNumpadKey(CreateKey(0x6A, "*", 1.0), 0, 2);
        AddNumpadKey(CreateKey(0x6D, "-", 1.0), 0, 3);

        AddNumpadKey(CreateKey(0x67, "7", 1.0), 1, 0);
        AddNumpadKey(CreateKey(0x68, "8", 1.0), 1, 1);
        AddNumpadKey(CreateKey(0x69, "9", 1.0), 1, 2);
        AddNumpadKey(CreateKey(0x6B, "+", 1.0, heightUnits: 2.0), 1, 3, rowSpan: 2);

        AddNumpadKey(CreateKey(0x64, "4", 1.0), 2, 0);
        AddNumpadKey(CreateKey(0x65, "5", 1.0), 2, 1);
        AddNumpadKey(CreateKey(0x66, "6", 1.0), 2, 2);

        AddNumpadKey(CreateKey(0x61, "1", 1.0), 3, 0);
        AddNumpadKey(CreateKey(0x62, "2", 1.0), 3, 1);
        AddNumpadKey(CreateKey(0x63, "3", 1.0), 3, 2);
        AddNumpadKey(CreateKey(0x0D, "Enter", 1.0, heightUnits: 2.0), 3, 3, rowSpan: 2);

        AddNumpadKey(CreateKey(0x60, "0", 2.0), 4, 0, colSpan: 2);
        AddNumpadKey(CreateKey(0x6E, ".", 1.0), 4, 2);

        numpadContainer.Children.Add(_numpadGrid);
        Grid.SetColumn(numpadContainer, 4);
        keyboardGrid.Children.Add(numpadContainer);

        return keyboardGrid;
    }

    private Border CreateKey(uint vkCode, string primary, double widthUnits = 1.0, double heightUnits = 1.0) =>
        CreateKey(vkCode, primary, null, widthUnits, heightUnits);

    private Border CreateKey(uint vkCode, string primary, string? secondary, double widthUnits = 1.0, double heightUnits = 1.0)
    {
        const double baseUnit = 32.0;
        const double marginGap = 2.0;

        double calculatedWidth = (widthUnits * baseUnit) + ((widthUnits - 1) * marginGap * 2);
        double calculatedHeight = (heightUnits * baseUnit) + ((heightUnits - 1) * marginGap * 2);

        var keyBorder = new Border
        {
            Width = calculatedWidth,
            Height = calculatedHeight,
            Margin = new Thickness(marginGap),
            CornerRadius = new CornerRadius(5),
            Background = ColorBrush("#1E293B"),
            BorderBrush = ColorBrush("#334155"),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            ToolTip = string.IsNullOrEmpty(secondary) ? primary : $"{secondary}\n{primary}"
        };

        var contentGrid = new Grid();

        if (!string.IsNullOrEmpty(secondary))
        {
            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = 9.5,
                Foreground = ColorBrush("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = primary,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColorBrush("#F1F5F9"),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            contentGrid.Children.Add(stack);
        }
        else
        {
            contentGrid.Children.Add(new TextBlock
            {
                Text = primary,
                FontSize = primary.Length > 4 ? 9 : 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColorBrush("#F1F5F9"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        keyBorder.Child = contentGrid;

        if (!_keyVisualMap.ContainsKey(vkCode))
        {
            _keyVisualMap[vkCode] = new List<Border>();
        }
        _keyVisualMap[vkCode].Add(keyBorder);

        return keyBorder;
    }

    public void OnKeyPressed(uint vkCode, int totalCount)
    {
        _blockedCount = totalCount;

        uint lookupVk = vkCode;
        if (vkCode == 0x10) lookupVk = 0xA0;
        if (vkCode == 0x11) lookupVk = 0xA2;
        if (vkCode == 0x12) lookupVk = 0xA4;

        if (_keyVisualMap.TryGetValue(lookupVk, out var borders) || _keyVisualMap.TryGetValue(vkCode, out borders))
        {
            foreach (var border in borders)
            {
                border.Background = ColorBrush("#10B981");
                border.BorderBrush = ColorBrush("#34D399");

                var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                fadeTimer.Tick += (s, _) =>
                {
                    ((DispatcherTimer)s!).Stop();
                    border.Background = ColorBrush("#1E293B");
                    border.BorderBrush = ColorBrush("#334155");
                };
                fadeTimer.Start();
            }
        }
    }

    private void StartKeyboardLock()
    {
        if (_hookId != IntPtr.Zero) return;

        try
        {
            _hookProc = HookCallback;
            IntPtr hMod = IntPtr.Zero;
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                if (curModule != null)
                {
                    hMod = GetModuleHandle(curModule.ModuleName);
                }
            }
            catch { }

            if (hMod == IntPtr.Zero)
            {
                hMod = GetModuleHandle(null);
            }

            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);
            if (_hookId != IntPtr.Zero)
            {
                _context.Log("键盘清洁锁已开启，所有按键已屏蔽");
            }
        }
        catch (Exception ex)
        {
            _context.Log("启动键盘锁异常: " + ex);
        }
    }

    private void StopKeyboardLock()
    {
        if (_hookId != IntPtr.Zero)
        {
            try
            {
                UnhookWindowsHookEx(_hookId);
                _context.Log("键盘清洁锁已解除，键盘输入恢复正常");
            }
            catch (Exception ex)
            {
                _context.Log("注销键盘钩子异常: " + ex);
            }
            finally
            {
                _hookId = IntPtr.Zero;
                _hookProc = null;
            }
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // 极速吞掉消息，绝不阻塞钩子线程
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (_activeInstance != null)
                {
                    KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int newCount = Interlocked.Increment(ref _activeInstance._blockedCount);
                    _activeInstance.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _activeInstance?.OnKeyPressed(hookStruct.vkCode, newCount);
                    }));
                }
            }

            return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
