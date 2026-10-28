using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

public enum WebPreviewPlacement
{
    Right,
    Bottom
}

/// <summary>
/// Embedded web preview panel for comparing WPF controls with reference sites (shadcn/ui docs).
/// </summary>
internal sealed class GalleryWebPreviewPanel : Border
{
    private readonly WebView2 _webView = new();
    private readonly TextBox _urlBox = new();
    private readonly Button _backButton;
    private readonly Button _forwardButton;
    private readonly Button _refreshButton;
    private readonly Button _homeButton;
    private readonly Button _placementButton;
    private readonly Button _openExternalButton;
    private readonly Button _closeButton;
    private readonly CheckBox _autoSyncCheckBox;
    private readonly Border _fallbackBanner = new();
    private readonly TextBlock _fallbackText = new();

    private string _currentHomeUrl = "https://ui.shadcn.com/docs/components";
    private string? _pendingUrl;
    private bool _isInitialized;
    private bool _isNavigating;
    private WebPreviewPlacement _placement = WebPreviewPlacement.Right;

    public event Action? TogglePlacementRequested;
    public event Action? CloseRequested;
    public event Action<string>? StatusChanged;

    public bool AutoSyncEnabled => _autoSyncCheckBox.IsChecked == true;
    public WebPreviewPlacement Placement => _placement;

    public GalleryWebPreviewPanel()
    {
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Background");
        SetResourceReference(BorderBrushProperty, "Yanzi.Brush.Border");

        var mainLayout = new DockPanel { LastChildFill = true };
        Child = mainLayout;

        // Top Toolbar
        var toolbar = new Border
        {
            Height = 38,
            Padding = new Thickness(8, 4, 8, 4),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        toolbar.SetResourceReference(BackgroundProperty, "Yanzi.Color.Sidebar");
        toolbar.SetResourceReference(BorderBrushProperty, "Yanzi.Brush.Border");
        DockPanel.SetDock(toolbar, Dock.Top);
        mainLayout.Children.Add(toolbar);

        var toolGrid = new Grid();
        toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Nav buttons
        toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // URL input
        toolGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions & toggles
        toolbar.Child = toolGrid;

        // Nav buttons (Back, Forward, Refresh, Home)
        var navGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(navGroup, 0);
        toolGrid.Children.Add(navGroup);

        _backButton = CreateToolButton("◀", "后退", () =>
        {
            if (_webView.CanGoBack) _webView.GoBack();
        });
        _forwardButton = CreateToolButton("▶", "前进", () =>
        {
            if (_webView.CanGoForward) _webView.GoForward();
        });
        _refreshButton = CreateToolButton("⟳", "刷新页面", () =>
        {
            if (_isNavigating && _webView.CoreWebView2 != null)
            {
                _webView.Stop();
            }
            else if (_webView.CoreWebView2 != null)
            {
                _webView.Reload();
            }
            else
            {
                NavigateTo(_urlBox.Text);
            }
        });
        _homeButton = CreateToolButton("⌂", "返回当前组件官方文档", () =>
        {
            NavigateTo(_currentHomeUrl);
        });

        navGroup.Children.Add(_backButton);
        navGroup.Children.Add(_forwardButton);
        navGroup.Children.Add(_refreshButton);
        navGroup.Children.Add(_homeButton);

        // URL Box
        _urlBox.Margin = new Thickness(8, 0, 8, 0);
        _urlBox.Height = 28;
        _urlBox.VerticalContentAlignment = VerticalAlignment.Center;
        _urlBox.FontSize = 11.5;
        _urlBox.Padding = new Thickness(6, 0, 6, 0);
        YanziUi.WithStyle(_urlBox, YanziUi.Styles.Input);
        _urlBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var input = _urlBox.Text.Trim();
                if (!string.IsNullOrEmpty(input))
                {
                    if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        input = "https://" + input;
                    }
                    NavigateTo(input);
                }
                e.Handled = true;
            }
        };
        Grid.SetColumn(_urlBox, 1);
        toolGrid.Children.Add(_urlBox);

        // Action controls on the right
        var actionsGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actionsGroup, 2);
        toolGrid.Children.Add(actionsGroup);

        _autoSyncCheckBox = new CheckBox
        {
            Content = "跟随组件",
            IsChecked = true,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "左侧切换组件或页面时，右侧网页自动同步跳转至对应文档"
        };
        YanziUi.WithStyle(_autoSyncCheckBox, YanziUi.Styles.CheckBox);
        actionsGroup.Children.Add(_autoSyncCheckBox);

        _placementButton = CreateToolButton("⬇ 下方", "切换停靠位置（右侧 / 下方）", () => TogglePlacementRequested?.Invoke());
        _placementButton.Margin = new Thickness(0, 0, 4, 0);
        actionsGroup.Children.Add(_placementButton);

        _openExternalButton = CreateToolButton("↗ 浏览器", "在外部默认浏览器中打开当前网页", () =>
        {
            var targetUrl = !string.IsNullOrWhiteSpace(_urlBox.Text) ? _urlBox.Text : _currentHomeUrl;
            try
            {
                Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                StatusChanged?.Invoke("已在外部浏览器中打开：" + targetUrl);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("在外部浏览器打开失败：" + ex.Message);
            }
        });
        _openExternalButton.Margin = new Thickness(0, 0, 4, 0);
        actionsGroup.Children.Add(_openExternalButton);

        _closeButton = CreateToolButton("✕", "收起网页对照", () => CloseRequested?.Invoke());
        actionsGroup.Children.Add(_closeButton);

        // Content Area (WebView2 + Fallback)
        var contentGrid = new Grid();
        mainLayout.Children.Add(contentGrid);

        contentGrid.Children.Add(_webView);

        // Fallback banner if WebView2 is missing or failed
        _fallbackBanner.Visibility = Visibility.Collapsed;
        _fallbackBanner.Padding = new Thickness(24);
        _fallbackBanner.VerticalAlignment = VerticalAlignment.Center;
        _fallbackBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _fallbackBanner.CornerRadius = new CornerRadius(8);
        _fallbackBanner.SetResourceReference(BackgroundProperty, "Yanzi.Color.Card");
        _fallbackBanner.SetResourceReference(BorderBrushProperty, "Yanzi.Brush.Border");
        _fallbackBanner.BorderThickness = new Thickness(1);

        var fallbackStack = new StackPanel { MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Center };
        _fallbackBanner.Child = fallbackStack;

        _fallbackText.Text = "WebView2 预览环境正在准备中…";
        _fallbackText.TextWrapping = TextWrapping.Wrap;
        _fallbackText.FontSize = 12.5;
        _fallbackText.LineHeight = 18;
        _fallbackText.Margin = new Thickness(0, 0, 0, 14);
        _fallbackText.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        fallbackStack.Children.Add(_fallbackText);

        var retryBtn = new Button
        {
            Content = "在系统浏览器中打开",
            Height = 32,
            Cursor = Cursors.Hand
        };
        YanziUi.WithStyle(retryBtn, YanziUi.Styles.PrimaryButton);
        retryBtn.Click += (_, _) =>
        {
            var target = !string.IsNullOrWhiteSpace(_urlBox.Text) ? _urlBox.Text : _currentHomeUrl;
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { }
        };
        fallbackStack.Children.Add(retryBtn);

        contentGrid.Children.Add(_fallbackBanner);

        // Initialize WebView2
        Loaded += OnLoaded;
    }

    public void UpdatePlacementUi(WebPreviewPlacement placement)
    {
        _placement = placement;
        _placementButton.Content = placement == WebPreviewPlacement.Right ? "⬇ 移至下方" : "➡ 移至右侧";
    }

    private YanziTheme _lastAppliedTheme = YanziTheme.Dark;

    public void ApplyTheme(YanziTheme theme)
    {
        _lastAppliedTheme = theme;
        try
        {
            var bg = theme == YanziTheme.Dark
                ? System.Drawing.Color.FromArgb(255, 9, 9, 11)
                : System.Drawing.Color.FromArgb(255, 255, 255, 255);
            _webView.DefaultBackgroundColor = bg;
            if (_isInitialized && _webView.CoreWebView2 != null)
            {
                var isDark = theme == YanziTheme.Dark ? "true" : "false";
                _webView.ExecuteScriptAsync($"try {{ document.documentElement.classList.toggle('dark', {isDark}); }} catch {{}}");
            }
        }
        catch { }
    }

    public void NavigateTo(string url, bool isFromComponentSync = false, string? homeUrl = null)
    {
        if (homeUrl != null)
        {
            _currentHomeUrl = homeUrl;
        }
        else if (!isFromComponentSync)
        {
            _currentHomeUrl = url;
        }

        if (isFromComponentSync && !AutoSyncEnabled)
        {
            // User locked auto sync, don't interrupt their current page
            return;
        }

        _urlBox.Text = url;

        if (!_isInitialized)
        {
            _pendingUrl = url;
            return;
        }

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                _webView.Source = uri;
            }
        }
        catch (Exception ex)
        {
            ShowFallback("加载网页时出错：" + ex.Message);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await InitializeWebViewAsync();
    }

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            // 优先使用当前进程独占的 Profile 目录，彻底杜绝多实例或孤儿进程文件锁冲突 (0x800700AA)
            var primaryFolder = Path.Combine(
                Path.GetTempPath(),
                "OpenQuickHost",
                "WebView2Gallery",
                Environment.ProcessId.ToString());
            Directory.CreateDirectory(primaryFolder);

            CoreWebView2Environment env;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, primaryFolder);
                await _webView.EnsureCoreWebView2Async(env);
            }
            catch
            {
                // 若主目录被锁，自愈机制：立即生成唯一临时目录进行第二轮重试
                var fallbackFolder = Path.Combine(
                    Path.GetTempPath(),
                    "OpenQuickHost",
                    "WebView2Gallery",
                    "fb_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fallbackFolder);
                env = await CoreWebView2Environment.CreateAsync(null, fallbackFolder);
                await _webView.EnsureCoreWebView2Async(env);
            }

            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            _webView.NavigationStarting += (_, args) =>
            {
                _isNavigating = true;
                _refreshButton.Content = "✕";
                _refreshButton.ToolTip = "停止加载";
                _urlBox.Text = args.Uri;
                StatusChanged?.Invoke("正在加载对照网页：" + args.Uri);
            };

            _webView.NavigationCompleted += (_, args) =>
            {
                _isNavigating = false;
                _refreshButton.Content = "⟳";
                _refreshButton.ToolTip = "刷新页面";
                _backButton.IsEnabled = _webView.CanGoBack;
                _forwardButton.IsEnabled = _webView.CanGoForward;
                ApplyTheme(_lastAppliedTheme);
                if (!args.IsSuccess)
                {
                    StatusChanged?.Invoke($"对照网页加载状态: {args.WebErrorStatus}");
                }
                else
                {
                    StatusChanged?.Invoke("对照网页加载完成");
                }
            };

            _webView.SourceChanged += (_, _) =>
            {
                if (_webView.Source != null)
                {
                    _urlBox.Text = _webView.Source.ToString();
                    _backButton.IsEnabled = _webView.CanGoBack;
                    _forwardButton.IsEnabled = _webView.CanGoForward;
                }
            };

            _isInitialized = true;
            _fallbackBanner.Visibility = Visibility.Collapsed;

            var target = _pendingUrl ?? _currentHomeUrl;
            _pendingUrl = null;
            NavigateTo(target);
        }
        catch (Exception ex)
        {
            ShowFallback("未能初始化内置 WebView2 预览引擎：" + ex.Message + "\n你可以使用外部浏览器查看官方对照文档。");
        }
    }

    private void ShowFallback(string message)
    {
        _fallbackText.Text = message;
        _fallbackBanner.Visibility = Visibility.Visible;
    }

    private Button CreateToolButton(string label, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Height = 26,
            MinWidth = 26,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            ToolTip = tooltip,
            Cursor = Cursors.Hand
        };
        YanziUi.WithStyle(btn, YanziUi.Styles.GhostButton);
        btn.Click += (_, _) => onClick();
        return btn;
    }
}

internal sealed partial class GalleryWindow
{
    private readonly Grid _contentContainer = new();
    private readonly GridSplitter _webSplitter = new();
    private GalleryWebPreviewPanel? _webPreviewPanel;
    private WebPreviewPlacement _webPlacement = WebPreviewPlacement.Right;
    private bool _webPreviewExpanded = true;
    private Button? _webToggleButton;
    private double _webRightWidth = 540;
    private double _webBottomHeight = 360;

    private void InitializeWebPreview()
    {
        _webPreviewPanel = new GalleryWebPreviewPanel();
        _webPreviewPanel.StatusChanged += msg => Status(msg);
        _webPreviewPanel.CloseRequested += () => SetWebPreviewExpanded(false);
        _webPreviewPanel.TogglePlacementRequested += () =>
        {
            _webPlacement = _webPlacement == WebPreviewPlacement.Right
                ? WebPreviewPlacement.Bottom
                : WebPreviewPlacement.Right;
            _webPreviewPanel.UpdatePlacementUi(_webPlacement);
            ApplyWebPreviewLayout();
        };

        _webSplitter.SetResourceReference(GridSplitter.BackgroundProperty, "Yanzi.Brush.Border");
        _webSplitter.ShowsPreview = false;

        ApplyWebPreviewLayout();
    }

    private void SetWebPreviewExpanded(bool expanded)
    {
        _webPreviewExpanded = expanded;
        if (_webToggleButton != null)
        {
            _webToggleButton.Content = expanded ? "🌐 网页对照 [开]" : "🌐 网页对照 [关]";
            _webToggleButton.FontWeight = expanded ? FontWeights.SemiBold : FontWeights.Normal;
        }
        ApplyWebPreviewLayout();
    }

    private void ApplyWebPreviewLayout()
    {
        if (_scroll == null || _webPreviewPanel == null) return;

        // Remember existing dimensions if changing layout
        if (_contentContainer.ColumnDefinitions.Count >= 3 && _contentContainer.ColumnDefinitions[2].ActualWidth > 100)
        {
            _webRightWidth = _contentContainer.ColumnDefinitions[2].ActualWidth;
        }
        else if (_contentContainer.RowDefinitions.Count >= 3 && _contentContainer.RowDefinitions[2].ActualHeight > 80)
        {
            _webBottomHeight = _contentContainer.RowDefinitions[2].ActualHeight;
        }

        _contentContainer.Children.Clear();
        _contentContainer.ColumnDefinitions.Clear();
        _contentContainer.RowDefinitions.Clear();

        if (!_webPreviewExpanded)
        {
            _contentContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _contentContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(_scroll, 0);
            Grid.SetRow(_scroll, 0);
            _contentContainer.Children.Add(_scroll);
            _webSplitter.Visibility = Visibility.Collapsed;
            _webPreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _webSplitter.Visibility = Visibility.Visible;
        _webPreviewPanel.Visibility = Visibility.Visible;

        if (_webPlacement == WebPreviewPlacement.Right)
        {
            // Left: WPF controls (Star), Center: Splitter (Auto), Right: WebView2 (Pixel width)
            _contentContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
            _contentContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            _contentContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_webRightWidth), MinWidth = 320 });
            _contentContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(_scroll, 0);
            Grid.SetRow(_scroll, 0);
            _contentContainer.Children.Add(_scroll);

            Grid.SetColumn(_webSplitter, 1);
            Grid.SetRow(_webSplitter, 0);
            _webSplitter.Width = 5;
            _webSplitter.Height = double.NaN;
            _webSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            _webSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            _webSplitter.ResizeDirection = GridResizeDirection.Columns;
            _webSplitter.Cursor = Cursors.SizeWE;
            _contentContainer.Children.Add(_webSplitter);

            Grid.SetColumn(_webPreviewPanel, 2);
            Grid.SetRow(_webPreviewPanel, 0);
            _contentContainer.Children.Add(_webPreviewPanel);
        }
        else
        {
            // Top: WPF controls (Star), Center: Splitter (Auto), Bottom: WebView2 (Pixel height)
            _contentContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _contentContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 220 });
            _contentContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            _contentContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_webBottomHeight), MinHeight = 180 });

            Grid.SetColumn(_scroll, 0);
            Grid.SetRow(_scroll, 0);
            _contentContainer.Children.Add(_scroll);

            Grid.SetColumn(_webSplitter, 0);
            Grid.SetRow(_webSplitter, 1);
            _webSplitter.Height = 5;
            _webSplitter.Width = double.NaN;
            _webSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            _webSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            _webSplitter.ResizeDirection = GridResizeDirection.Rows;
            _webSplitter.Cursor = Cursors.SizeNS;
            _contentContainer.Children.Add(_webSplitter);

            Grid.SetColumn(_webPreviewPanel, 0);
            Grid.SetRow(_webPreviewPanel, 2);
            _contentContainer.Children.Add(_webPreviewPanel);
        }

        _webPreviewPanel.UpdatePlacementUi(_webPlacement);
        _webPreviewPanel.ApplyTheme(_theme);
    }

    private void SyncWebPreviewWithComponent(YanziComponentDescriptor item)
    {
        if (_webPreviewPanel == null) return;
        _webPreviewPanel.NavigateTo(item.OfficialUrl, isFromComponentSync: true, homeUrl: item.OfficialUrl);
    }

    private void SyncWebPreviewWithPage(int page)
    {
        if (_webPreviewPanel == null) return;
        var url = GetPageOfficialUrl(page);
        _webPreviewPanel.NavigateTo(url, isFromComponentSync: true, homeUrl: url);
    }

    private static string GetPageOfficialUrl(int page) => page switch
    {
        0 => "https://ui.shadcn.com/docs/components",
        1 => "https://ui.shadcn.com/docs/components/button",
        2 => "https://ui.shadcn.com/docs/components/input",
        3 => "https://ui.shadcn.com/docs/components/switch",
        4 => "https://ui.shadcn.com/docs/components/scroll-area",
        5 => "https://ui.shadcn.com/docs/components/dialog",
        6 => "https://ui.shadcn.com/docs/theming",
        7 => "https://ui.shadcn.com/docs/components/badge",
        8 => "https://ui.shadcn.com/docs/components/alert",
        9 => "https://ui.shadcn.com/docs/components/form",
        10 => "https://ui.shadcn.com/docs/components/tabs",
        11 => "https://ui.shadcn.com/docs/components/table",
        12 => "https://ui.shadcn.com/docs/components/dialog",
        13 => "https://ui.shadcn.com/docs/components",
        _ => "https://ui.shadcn.com/docs/components"
    };
}
