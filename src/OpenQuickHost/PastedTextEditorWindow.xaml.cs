using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class PastedTextEditorWindow : Window
{
    private static readonly ConcurrentDictionary<string, PastedTextEditorWindow> OpenWindows =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    private readonly Action<string, string>? _onSaved;
    private bool _isDirty;

    public PastedTextEditorWindow(string filePath, string displayTitle, Action<string, string>? onSaved = null)
    {
        InitializeComponent();
        _filePath = Path.GetFullPath(filePath);
        _onSaved = onSaved;

        Title = string.IsNullOrWhiteSpace(displayTitle) ? "便签编辑" : $"便签 - {displayTitle}";
        TitleTextBox.Text = displayTitle;
        PathInfoText.Text = _filePath;

        LoadContent();
        Closed += (_, _) => OpenWindows.TryRemove(_filePath, out _);
    }

    public static PastedTextEditorWindow Open(string filePath, string displayTitle, Window? owner = null, Action<string, string>? onSaved = null)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (OpenWindows.TryGetValue(normalizedPath, out var existingWindow) && existingWindow.IsLoaded)
        {
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }
            existingWindow.Activate();
            existingWindow.Focus();
            return existingWindow;
        }

        var window = new PastedTextEditorWindow(normalizedPath, displayTitle, onSaved);
        if (owner != null && owner.IsVisible)
        {
            try
            {
                window.Owner = owner;
            }
            catch
            {
                // Ignore if owner is invalid
            }
        }
        OpenWindows[normalizedPath] = window;
        window.Show();
        window.Activate();
        window.ContentTextBox.Focus();
        return window;
    }

    private void LoadContent()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                ContentTextBox.Text = File.ReadAllText(_filePath, Encoding.UTF8);
            }
            else
            {
                ContentTextBox.Text = string.Empty;
            }
            UpdateCharCount();
            _isDirty = false;
        }
        catch (Exception ex)
        {
            StatusMessageText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusMessageText.Text = $"加载失败: {ex.Message}";
        }
    }

    private void UpdateCharCount()
    {
        var length = ContentTextBox.Text?.Length ?? 0;
        var lineCount = ContentTextBox.LineCount;
        CharCountText.Text = $"共 {length} 字 / {lineCount} 行";
    }

    private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isDirty = true;
        UpdateCharCount();
    }

    private void ContentTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveContent();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveContent();
    }

    private void SaveContent()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var newText = ContentTextBox.Text ?? string.Empty;
            File.WriteAllText(_filePath, newText, Encoding.UTF8);

            var newTitle = TitleTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                var firstLine = newText.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                newTitle = firstLine.Length > 0 ? (firstLine[0].Trim().Length > 16 ? firstLine[0].Trim()[..16] + "..." : firstLine[0].Trim()) : "未命名便签";
                TitleTextBox.Text = newTitle;
            }

            Title = $"便签 - {newTitle}";
            _isDirty = false;
            StatusMessageText.Foreground = System.Windows.Media.Brushes.LightGreen;
            StatusMessageText.Text = $"已保存 ({DateTime.Now:HH:mm:ss})";

            _onSaved?.Invoke(newTitle, newText);
        }
        catch (Exception ex)
        {
            StatusMessageText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusMessageText.Text = $"保存失败: {ex.Message}";
        }
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ContentTextBox.Text ?? string.Empty);
            StatusMessageText.Foreground = System.Windows.Media.Brushes.LightGreen;
            StatusMessageText.Text = "全文已复制到剪贴板！";
        }
        catch (Exception ex)
        {
            StatusMessageText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            StatusMessageText.Text = $"复制失败: {ex.Message}";
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_filePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty)
        {
            var res = System.Windows.MessageBox.Show(this, "便签内容尚未保存，确定要退出吗？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes)
            {
                return;
            }
        }
        Close();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelButton_Click(this, new RoutedEventArgs());
        }
    }
}
