using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Yanzi.Capture;

public sealed class CaptureShortcutPreferences
{
    public string OcrShortcut { get; set; } = "F3";
    public bool AutoCopyOcrText { get; set; } = true;
}

public static class CapturePreferencesStore
{
    private const string FileName = "capture-shortcuts.json";

    public static CaptureShortcutPreferences Load(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return new CaptureShortcutPreferences();
            return JsonSerializer.Deserialize<CaptureShortcutPreferences>(File.ReadAllText(path))
                   ?? new CaptureShortcutPreferences();
        }
        catch
        {
            return new CaptureShortcutPreferences();
        }
    }

    public static void Save(string directory, CaptureShortcutPreferences value)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}

/// <summary>
/// The resident screenshot extension owns only its additional OCR shortcut.
/// The normal screenshot shortcut remains registered by the Yanzi host.
/// Windows RegisterHotKey arbitrates conflicts with all other applications.
/// </summary>
internal sealed class CaptureOcrHotkeyReceiver : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4311;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _source;
    private readonly Action _onOcr;
    private string? _registeredShortcut;

    public string? RegisteredShortcut => _registeredShortcut;

    public CaptureOcrHotkeyReceiver(Action onOcr)
    {
        _onOcr = onOcr;
        var parameters = new HwndSourceParameters("Yanzi.Capture.OcrHotkey")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: never shows a taskbar/tray window
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool TryApply(string shortcut)
    {
        if (!TryParse(shortcut, out var modifiers, out var key)) return false;
        if (string.Equals(_registeredShortcut, shortcut.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        var old = _registeredShortcut;
        if (old is not null)
            UnregisterHotKey(_source.Handle, HotkeyId);

        if (RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            _registeredShortcut = shortcut.Trim();
            return true;
        }

        _registeredShortcut = null;
        if (old is not null && TryParse(old, out var previousModifiers, out var previousKey) &&
            RegisterHotKey(_source.Handle, HotkeyId, previousModifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(previousKey)))
            _registeredShortcut = old;
        return false;
    }

    public static bool TryParse(string? shortcut, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var tokens = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 4) return false;
        foreach (var token in tokens)
        {
            var flag = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 0x0002u,
                "ALT" => 0x0001u,
                "SHIFT" => 0x0004u,
                "WIN" or "WINDOWS" => 0x0008u,
                _ => 0u
            };
            if (flag != 0)
            {
                if ((modifiers & flag) != 0) return false;
                modifiers |= flag;
            }
            else if (key == Key.None && Enum.TryParse<Key>(token, true, out var parsed))
                key = parsed;
            else
                return false;
        }
        if (key == Key.None || KeyInterop.VirtualKeyFromKey(key) <= 0) return false;
        return modifiers != 0 || (key >= Key.F1 && key <= Key.F24);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onOcr();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registeredShortcut is not null)
            UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _registeredShortcut = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

public sealed class CaptureSettingsWindow : Window
{
    public CaptureSettingsWindow(
        string directory,
        Func<CaptureShortcutPreferences, bool> apply,
        string? activeShortcut = null)
    {
        var saved = CapturePreferencesStore.Load(directory);
        Title = "燕子截图 · 设置";
        Width = 425;
        Height = 382;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(24, 27, 33));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "截图快捷键",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 17)
        });
        root.Children.Add(new TextBlock
        {
            Text = "普通截图：由燕子宿主的“小程序快捷键”管理（当前默认 F1）",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 17)
        });
        root.Children.Add(new TextBlock { Text = "截图并 OCR 快捷键", Margin = new Thickness(0, 0, 0, 7) });
        var shortcut = new TextBox
        {
            Text = saved.OcrShortcut,
            Height = 32,
            Padding = new Thickness(9, 4, 9, 4),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(38, 42, 50)),
            Foreground = Brushes.White,
            BorderBrush = Brushes.DimGray
        };
        root.Children.Add(shortcut);
        root.Children.Add(new TextBlock
        {
            Text = "例如 F3、Ctrl+Alt+O；已被占用的快捷键不能保存",
            FontSize = 11,
            Foreground = Brushes.DarkGray,
            Margin = new Thickness(0, 6, 0, 13)
        });
        root.Children.Add(new TextBlock
        {
            Text = "✓ OCR 成功后自动复制文字；右下角提示 3 秒，点击可查看原文",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGreen,
            Margin = new Thickness(0, 0, 0, 13)
        });
        root.Children.Add(new TextBlock
        {
            Text = "OCR 引擎：优先燕子 PaddleOCR，失败时自动回退 Windows OCR",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 13)
        });
        var message = new TextBlock
        {
            Text = !string.IsNullOrEmpty(activeShortcut) &&
                   !string.Equals(activeShortcut, saved.OcrShortcut, StringComparison.OrdinalIgnoreCase)
                ? $"当前 {saved.OcrShortcut} 被占用，备用键 {activeShortcut} 已启用。"
                : string.IsNullOrEmpty(activeShortcut) ? "OCR 快捷键注册失败，请更换组合键。" : "",
            Foreground = Brushes.Orange,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 21
        };
        root.Children.Add(message);
        var save = new Button
        {
            Content = "保存设置",
            Height = 35,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 105,
            Margin = new Thickness(0, 4, 0, 0)
        };
        save.Click += (_, _) =>
        {
            var next = new CaptureShortcutPreferences
            {
                OcrShortcut = shortcut.Text.Trim(),
                AutoCopyOcrText = true
            };
            if (!CaptureOcrHotkeyReceiver.TryParse(next.OcrShortcut, out _, out _))
            {
                message.Text = "请输入有效快捷键（单独按键仅支持 F1–F24）。";
                return;
            }
            if (!apply(next))
            {
                message.Text = "快捷键被其他程序占用，原快捷键保持不变。";
                return;
            }
            try
            {
                CapturePreferencesStore.Save(directory, next);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                apply(saved);
                message.Text = "保存失败：" + ex.Message;
            }
        };
        root.Children.Add(save);
        Content = root;
    }
}
