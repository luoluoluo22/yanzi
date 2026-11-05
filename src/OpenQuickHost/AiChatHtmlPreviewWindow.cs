using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OpenQuickHost;

/// <summary>
/// On-demand, non-scriptable HTML/SVG/CSS preview. A separate WebView2 profile,
/// CSP and deny-all resource/navigation handlers keep model output isolated.
/// No model HTML is loaded as a local file or given host privileges.
/// </summary>
public sealed class AiChatHtmlPreviewWindow : Window
{
    private readonly string _protectedHtml;
    private readonly WebView2 _browser;
    private readonly TextBlock _status;

    public AiChatHtmlPreviewWindow(string html)
    {
        _protectedHtml = AiChatHtmlPreviewContent.BuildProtectedDocument(html);

        Title = "燕子 · 网页预览";
        Width = 1020;
        Height = 740;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 22, 26));
        Foreground = System.Windows.Media.Brushes.White;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

        var bar = new DockPanel
        {
            LastChildFill = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 30, 35)),
            Margin = new Thickness(0),
        };
        var title = new TextBlock
        {
            Text = "HTML / SVG 预览",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            Foreground = System.Windows.Media.Brushes.White,
        };
        DockPanel.SetDock(title, Dock.Left);
        bar.Children.Add(title);

        var copy = new System.Windows.Controls.Button
        {
            Content = "复制 HTML 源码",
            ToolTip = "复制原始 HTML；不会执行其中的脚本",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 12, 0),
            Padding = new Thickness(12, 5, 12, 5),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 60, 69)),
        };
        DockPanel.SetDock(copy, Dock.Right);
        copy.Click += (_, _) => ClipboardService.SetText(html);
        bar.Children.Insert(0, copy);
        Grid.SetRow(bar, 0);
        layout.Children.Add(bar);

        _browser = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
        };
        Grid.SetRow(_browser, 1);
        layout.Children.Add(_browser);

        _status = new TextBlock
        {
            Text = "安全预览：支持 CSS / SVG 动画，禁止 JavaScript、网络访问和页面跳转",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(12, 0, 12, 0),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 188, 198)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(_status, 2);
        layout.Children.Add(_status);
        Content = layout;

        Loaded += async (_, _) => await InitializePreviewAsync();
        Closed += (_, _) => _browser.Dispose();
    }

    private async Task InitializePreviewAsync()
    {
        try
        {
            // Never share the authenticated extension/host browser profile.
            var userData = HostAssets.ResolveDataDirectoryPath("AiChatHtmlPreviewWebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _browser.EnsureCoreWebView2Async(environment);

            var core = _browser.CoreWebView2;
            var settings = core.Settings;
            settings.IsScriptEnabled = false;
            settings.IsWebMessageEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            // NavigateToString opens a data: URI; permit only our exact CSP-protected document.
            var permittedDocument = "data:text/html;charset=utf-8;base64," +
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_protectedHtml));
            core.NavigationStarting += (_, args) =>
            {
                if (!string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(args.Uri, permittedDocument, StringComparison.Ordinal))
                    args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                args.Response = core.Environment.CreateWebResourceResponse(
                    null, 403, "Blocked", "Content-Type: text/plain");
            };

            // NavigateToString creates an opaque-origin document. CSS and SVG
            // animations render, but no JavaScript or external resource can run.
            core.NavigateToString(_protectedHtml);
        }
        catch (Exception exception)
        {
            _status.Text = "安全网页预览未能启动：" + exception.Message;
            _status.ToolTip = _status.Text;
            HostAssets.AppendLog("AiChat HTML preview unavailable: " +
                exception.GetType().Name + ": " + exception.Message);
        }
    }
}
