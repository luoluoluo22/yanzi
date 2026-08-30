using System.Runtime.InteropServices;
using System.Windows.Input;

namespace OpenQuickHost;

/// <summary>
/// 全局快捷键与物理按键状态统一辅助类
/// </summary>
public static class HotkeyHelper
{
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;      // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    /// <summary>
    /// 读取当前物理按下的修饰键（不受 WPF 消息队列延迟影响）
    /// </summary>
    public static ModifierKeys GetCurrentPhysicalModifiers()
    {
        var mods = ModifierKeys.None;
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) mods |= ModifierKeys.Control;
        if ((GetKeyState(VK_MENU) & 0x8000) != 0) mods |= ModifierKeys.Alt;
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) mods |= ModifierKeys.Shift;
        if ((GetKeyState(VK_LWIN) & 0x8000) != 0 || (GetKeyState(VK_RWIN) & 0x8000) != 0) mods |= ModifierKeys.Windows;
        return mods;
    }

    /// <summary>
    /// 判断指定的键是否为纯修饰键（Ctrl、Alt、Shift、Win）
    /// </summary>
    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
                   or Key.LeftShift or Key.RightShift
                   or Key.LeftAlt or Key.RightAlt
                   or Key.LWin or Key.RWin;
    }

    /// <summary>
    /// 将单个 Key 格式化为用户友好的文本（如 D1 -> 1, NumPad1 -> Num1）
    /// </summary>
    public static string FormatKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return $"Num{(int)(key - Key.NumPad0)}";
        }

        return key switch
        {
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            Key.Capital => "CapsLock",
            Key.OemTilde => "~",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Oem1 => ";",
            Key.Oem7 => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString()
        };
    }

    /// <summary>
    /// 将修饰键与按键组合格式化为标准快捷键字符串（如 "Ctrl+Alt+K"）
    /// </summary>
    public static string? FormatHotkey(ModifierKeys modifiers, Key key)
    {
        if (IsModifierKey(key) || key == Key.None)
        {
            return null;
        }

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }
}
