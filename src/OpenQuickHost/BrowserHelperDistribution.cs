using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OpenQuickHost;

/// <summary>
/// Browser selection and consent-aware extension onboarding. Browser stores own signed
/// extension updates. Ego can load and refresh a trusted unpacked build via its CDP API.
/// </summary>
internal static class BrowserHelperDistribution
{
    internal const string EdgeListing =
        "https://microsoftedge.microsoft.com/addons/detail/yanzi-browser-helper/cflclhbkmiepfgholcobghgapgfbigne";
    internal const string ChromeStoreSearch = "https://chromewebstore.google.com/search/Yanzi%20Browser%20Helper";

    internal static BrowserHelperBootstrap.BrowserProfile? ChooseTarget(
        Window owner, IReadOnlyList<BrowserHelperBootstrap.BrowserProfile> profiles)
    {
        var entries = profiles.GroupBy(p => p.Browser)
            .Select(g => g.OrderByDescending(p => p.Installed && p.Enabled)
                          .ThenByDescending(p => p.Installed).First())
            .OrderBy(p => p.Browser switch
            {
                "Ego" => 0, "Edge" => 1, "Chrome" => 2,
                "Brave" => 3, "Vivaldi" => 4, "Opera" => 5, _ => 10
            }).ToArray();
        if (entries.Length == 0) return null;

        var dialog = new Window
        {
            Title = "燕子浏览器助手 · 选择浏览器",
            Owner = owner,
            Width = 495, Height = 345,
            MinWidth = 450, MinHeight = 315,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanMinimize,
            ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "选择要安装或检查燕子助手的浏览器",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Edge 走微软商店；Ego 支持自动加载；其他 Chromium 浏览器使用稳定目录手动授权，未来可走 Chrome Web Store。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var selection = new System.Windows.Controls.ListBox { Height = 145 };
        foreach (var entry in entries)
        {
            var status = entry.Browser == "Ego" && !entry.Installed
                ? "可自动检测、安装或升级"
                : entry.Installed
                    ? entry.Enabled ? $"已启用 · {entry.Version ?? "版本未知"}"
                                    : "已安装但未启用"
                    : "待安装或待检测";
            selection.Items.Add(new ListBoxItem
            {
                Content = $"{entry.Browser}  ·  {status}",
                Tag = entry
            });
        }
        selection.SelectedIndex = 0;
        panel.Children.Add(selection);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new System.Windows.Controls.Button { Content = "取消", MinWidth = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        var okay = new System.Windows.Controls.Button { Content = "继续", MinWidth = 90, Height = 30 };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        okay.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(okay);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true
            ? (selection.SelectedItem as ListBoxItem)?.Tag as BrowserHelperBootstrap.BrowserProfile
            : null;
    }

    internal static void OpenStore(BrowserHelperBootstrap.BrowserProfile profile)
    {
        var url = profile.Browser == "Edge" ? EdgeListing : ChromeStoreSearch;
        var start = new ProcessStartInfo(profile.Executable) { UseShellExecute = true };
        start.ArgumentList.Add(url);
        Process.Start(start);
    }

    internal static async Task<(bool Success, string Message)> ConfigureEgoAsync(
        string extensionDirectory, bool allowInstall, CancellationToken cancellationToken = default)
    {
        var cli = FindEgoCli();
        if (cli == null) return (false, "找不到 ego-browser 命令。请先安装并启动 Ego Lite。");
        var script = Path.Combine(AppContext.BaseDirectory, "BrowserHelper", "ego-browser-helper.mjs");
        if (!File.Exists(script)) return (false, "安装包缺少 Ego 自动安装脚本，请修复燕子安装包。");
        if (!File.Exists(Path.Combine(extensionDirectory, "manifest.json")))
            return (false, "稳定目录缺少浏览器扩展，请先更新燕子。");

        var start = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        // Ego nodejs runs inside its isolated automation runtime; inherited env vars
        // aren't available there. Pass only the nonsecret extension path and action.
        var options = System.Text.Json.JsonSerializer.Serialize(new
        {
            path = extensionDirectory,
            action = allowInstall ? "install" : "update"
        });
        start.ArgumentList.Add("nodejs");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("globalThis.__yanziHelperOptions=" + options
            + ";await import(" + System.Text.Json.JsonSerializer.Serialize(new Uri(script).AbsoluteUri) + ")");
        try
        {
            using var process = new Process { StartInfo = start };
            if (!process.Start()) return (false, "Ego 运行时未启动。");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            // Drain both streams concurrently to avoid a blocked process.
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            var error = await stderr;
            var line = (output + "\n" + error).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(x => x.Contains("YANZI_EGO_RESULT ", StringComparison.Ordinal));
            if (process.ExitCode != 0 || line == null)
                return (false, "Ego 检查失败：" + (error.Length > 300 ? error[..300] : error));
            using var json = System.Text.Json.JsonDocument.Parse(line[(line.IndexOf("YANZI_EGO_RESULT ", StringComparison.Ordinal)
                + "YANZI_EGO_RESULT ".Length)..]);
            var result = json.RootElement;
            var state = result.GetProperty("status").GetString() ?? "";
            var version = result.TryGetProperty("version", out var v) ? v.GetString() : "";
            if (state is "installed" or "updated" or "ready")
                return (true, $"Ego 燕子助手已就绪，版本 {version}。");
            if (state == "external")
                return (false, "Ego 已加载其他路径的燕子助手；为保护扩展 ID 和数据，未重复安装。请先在扩展管理页确认旧版本路径。");
            return (false, $"Ego 返回状态：{state}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Ego 检查超时，未确认安装成功。");
        }
        catch (Exception ex)
        {
            return (false, $"Ego 检查失败：{ex.GetType().Name}：{ex.Message}");
        }
    }

    internal static string? FindEgoCli()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in new[]
        {
            Path.Combine(home, ".local", "share", "ego", "active_version_dir", "ego-browser.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "ego-browser", "ego-browser.exe")
        })
        {
            if (File.Exists(path)) return path;
        }
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(entry.Trim('"'), "ego-browser.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    internal static string ManagedEgoMarker => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", ".ego-managed-install");

    internal static async Task UpdateManagedEgoIfRunningAsync()
    {
        if (!File.Exists(ManagedEgoMarker)) return;
        if (Process.GetProcessesByName("ego").Length == 0) return;
        var result = await ConfigureEgoAsync(BrowserHelperBootstrap.StableDirectory, false);
        HostAssets.AppendLog(result.Success
            ? "[BrowserHelper] Ego managed extension reload checked."
            : $"[BrowserHelper] Ego managed extension reload skipped: {result.Message}");
    }
}
