using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class MouseTriggerCaptureWindow : Window
{
    private const int LongPressMilliseconds = 300;
    private const double DragThreshold = 18;
    private MouseButton? _downButton;
    private System.Windows.Point _downPoint;
    private long _downAt;

    public string TriggerMode { get; private set; } = MouseTriggerModes.None;

    public string Target { get; private set; } = "Panel";

    public MouseTriggerCaptureWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _downButton = e.ChangedButton;
        _downPoint = e.GetPosition(this);
        _downAt = Environment.TickCount64;
        StatusText.Text = $"已按下 {GetButtonLabel(e.ChangedButton)}，松开完成录制。";
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_downButton != MouseButton.Right)
        {
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _downPoint.X;
        var dy = point.Y - _downPoint.Y;
        if ((dx * dx) + (dy * dy) >= DragThreshold * DragThreshold)
        {
            Complete(MouseTriggerModes.RightDrag);
        }
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_downButton != e.ChangedButton)
        {
            return;
        }

        ReleaseMouseCapture();
        var duration = Environment.TickCount64 - _downAt;
        var mode = ResolveMode(e.ChangedButton, duration);
        if (string.Equals(mode, MouseTriggerModes.None, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "当前动作还没有可用触发模式。请尝试中键、侧键、右键拖动、右键/中键长按或 Ctrl+鼠标。";
            _downButton = null;
            return;
        }

        Complete(mode);
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            Complete(MouseTriggerModes.HorizontalWheel);
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Complete(string mode)
    {
        TriggerMode = MouseTriggerModes.Normalize(mode);
        Target = TargetComboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "Panel";
        StatusText.Text = $"已录制：{GetModeLabel(TriggerMode)}";
        DialogResult = true;
        Close();
    }

    private static string ResolveMode(MouseButton button, long duration)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        return button switch
        {
            MouseButton.Left when ctrl => MouseTriggerModes.CtrlLeftClick,
            MouseButton.Right when ctrl => MouseTriggerModes.CtrlRightClick,
            MouseButton.Middle when ctrl => MouseTriggerModes.CtrlMiddleClick,
            MouseButton.Middle when duration >= LongPressMilliseconds => MouseTriggerModes.MiddleLongPress,
            MouseButton.Right when duration >= LongPressMilliseconds => MouseTriggerModes.RightLongPress,
            MouseButton.Middle => MouseTriggerModes.MiddleDown,
            MouseButton.XButton1 => MouseTriggerModes.X1Down,
            MouseButton.XButton2 => MouseTriggerModes.X2Down,
            _ => MouseTriggerModes.None
        };
    }

    private static string GetButtonLabel(MouseButton button) => button switch
    {
        MouseButton.Left => "左键",
        MouseButton.Right => "右键",
        MouseButton.Middle => "中键",
        MouseButton.XButton1 => "侧键 X1",
        MouseButton.XButton2 => "侧键 X2",
        _ => button.ToString()
    };

    private static string GetModeLabel(string mode) => MouseTriggerModes.Normalize(mode) switch
    {
        MouseTriggerModes.MiddleDown => "按下中键",
        MouseTriggerModes.X1Down => "按下 X1 键",
        MouseTriggerModes.X2Down => "按下 X2 键",
        MouseTriggerModes.CtrlLeftClick => "Ctrl+左键单击",
        MouseTriggerModes.CtrlRightClick => "Ctrl+右键单击",
        MouseTriggerModes.CtrlMiddleClick => "Ctrl+中键单击",
        MouseTriggerModes.MiddleLongPress => "长按中键",
        MouseTriggerModes.RightLongPress => "长按右键",
        MouseTriggerModes.RightDrag => "按右键移动",
        MouseTriggerModes.HorizontalWheel => "滚轮左右",
        _ => "未识别"
    };
}
