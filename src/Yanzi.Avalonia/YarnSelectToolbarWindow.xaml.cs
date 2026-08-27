using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Yanzi.Avalonia;

public partial class YarnSelectToolbarWindow : Window
{
    private readonly MainWindow? _mainWindow;
    private string _selectedText = string.Empty;

    public YarnSelectToolbarWindow()
        : this(null!)
    {
    }

    public YarnSelectToolbarWindow(MainWindow? mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void ShowAt(Point position, string selectedText)
    {
        _selectedText = selectedText;
        if (string.IsNullOrWhiteSpace(_selectedText)) return;

        // Position slightly above or below the cursor
        Position = new PixelPoint((int)position.X - 190, (int)position.Y - 65);
        Show();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    private void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        if (string.IsNullOrWhiteSpace(_selectedText)) return;
        var query = Uri.EscapeDataString(_selectedText.Trim());
        try
        {
            Process.Start("open", $"https://www.google.com/search?q={query}");
        }
        catch { }
    }

    private void OnTranslateClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        if (string.IsNullOrWhiteSpace(_selectedText)) return;
        var query = Uri.EscapeDataString(_selectedText.Trim());
        try
        {
            Process.Start("open", $"https://translate.google.com/?text={query}");
        }
        catch { }
    }

    private void OnAiExplainClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        if (string.IsNullOrWhiteSpace(_selectedText)) return;
        _mainWindow?.ShowLauncherWithQuery($"ai 解释: {_selectedText.Trim()}");
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        if (string.IsNullOrWhiteSpace(_selectedText) || Clipboard == null) return;
        await Clipboard.SetTextAsync(_selectedText);
    }

    private void OnOpenInLauncherClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        if (string.IsNullOrWhiteSpace(_selectedText)) return;
        _mainWindow?.ShowLauncherWithQuery(_selectedText.Trim());
    }
}
