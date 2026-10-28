using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    internal sealed record EgoResult(bool Success, string Message, string Status = "",
        string? ExtensionId = null, string? ActualPath = null, string? Version = null);

    internal static async Task<EgoResult> ConfigureEgoAsync(
        string extensionDirectory, bool allowInstall, CancellationToken cancellationToken = default,
        bool forceReload = false)
    {
        var cli = FindEgoCli();
        if (cli == null) return new EgoResult(false, "找不到 ego-browser 命令。请先安装并启动 Ego Lite。");
        var script = Path.Combine(AppContext.BaseDirectory, "BrowserHelper", "ego-browser-helper.mjs");
        if (!File.Exists(script)) return new EgoResult(false, "安装包缺少 Ego 自动安装脚本，请修复燕子安装包。");
        if (!File.Exists(Path.Combine(extensionDirectory, "manifest.json")))
            return new EgoResult(false, "扩展目录缺少 manifest.json，请先检查安装路径。");

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
            action = forceReload ? "reload" : allowInstall ? "install" : "update"
        });
        start.ArgumentList.Add("nodejs");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("globalThis.__yanziHelperOptions=" + options
            + ";await import(" + System.Text.Json.JsonSerializer.Serialize(new Uri(script).AbsoluteUri) + ")");
        try
        {
            using var process = new Process { StartInfo = start };
            if (!process.Start()) return new EgoResult(false, "Ego 运行时未启动。");
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
                return new EgoResult(false, "Ego 检查失败：" + (error.Length > 300 ? error[..300] : error));
            using var json = System.Text.Json.JsonDocument.Parse(line[(line.IndexOf("YANZI_EGO_RESULT ", StringComparison.Ordinal)
                + "YANZI_EGO_RESULT ".Length)..]);
            var result = json.RootElement;
            var state = result.GetProperty("status").GetString() ?? "";
            var version = result.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : "";
            var id = result.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
                ? idValue.GetString() : null;
            var actualPath = result.TryGetProperty("path", out var pathValue)
                && pathValue.ValueKind == JsonValueKind.String ? pathValue.GetString() : null;
            if (state is "installed" or "updated" or "ready" or "reloaded")
                return new EgoResult(true, $"Ego 燕子助手已就绪，版本 {version}。", state, id, actualPath, version);
            if (state == "external")
                return new EgoResult(false, "Ego 已加载另一个路径中的燕子助手，可登记当前目录以保留 ID 和数据。",
                    state, id, actualPath, version);
            return new EgoResult(false, $"Ego 返回状态：{state}", state, id, actualPath, version);
        }
        catch (OperationCanceledException)
        {
            return new EgoResult(false, "Ego 检查超时，未确认安装成功。");
        }
        catch (Exception ex)
        {
            return new EgoResult(false, $"Ego 检查失败：{ex.GetType().Name}：{ex.Message}");
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

    internal sealed record EgoManagedState(
        string Mode, string Path, string ExtensionId, string Fingerprint);

    internal static string ManagedEgoMarker => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", ".ego-managed-install");

    internal static string GetBundleFingerprint(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "manifest.json")))
            throw new FileNotFoundException("托管扩展缺少 manifest.json。");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(p => {
                var rel = Path.GetRelativePath(directory, p).Replace('\\', '/');
                return !rel.StartsWith("scratch/", StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                    && !rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    && !rel.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
            }).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/')));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static void SaveManagedEgo(EgoManagedState state)
    {
        if (!Path.IsPathFullyQualified(state.Path)
            || state.ExtensionId.Length != 32 || !state.ExtensionId.All(c => c is >= 'a' and <= 'p'))
            throw new InvalidDataException("Ego 扩展托管登记信息无效。");
        Directory.CreateDirectory(Path.GetDirectoryName(ManagedEgoMarker)!);
        var temp = ManagedEgoMarker + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state), new UTF8Encoding(false));
            File.Move(temp, ManagedEgoMarker, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static EgoManagedState? ReadManagedEgo()
    {
        if (!File.Exists(ManagedEgoMarker)) return null;
        try
        {
            var contents = File.ReadAllText(ManagedEgoMarker);
            // Old 1.0.65 installations stored a timestamp only.
            if (!contents.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;
            var result = JsonSerializer.Deserialize<EgoManagedState>(contents);
            if (result == null || result.ExtensionId.Length != 32
                || !Path.IsPathFullyQualified(result.Path)
                || result.Mode is not ("stable" or "source")) return null;
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NullReferenceException) { return null; }
    }

    private static System.Threading.Timer? _egoManagementTimer;
    private static int _egoManagementCheckRunning;

    internal static void StartManagedEgoMonitor()
    {
        // Local-only check. No network polling; does not run if Ego has never
        // been explicitly enrolled. One check per minute while Yanzi is running.
        if (!File.Exists(ManagedEgoMarker)) return;
        if (_egoManagementTimer != null) return;
        _egoManagementTimer = new System.Threading.Timer(async _ =>
        {
            if (Interlocked.Exchange(ref _egoManagementCheckRunning, 1) != 0) return;
            try { await UpdateManagedEgoIfRunningAsync(); }
            finally { Interlocked.Exchange(ref _egoManagementCheckRunning, 0); }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    internal static async Task UpdateManagedEgoIfRunningAsync()
    {
        var state = ReadManagedEgo();
        if (state == null) return;
        if (Process.GetProcessesByName("ego").Length == 0) return;
        if (!File.Exists(Path.Combine(state.Path, "manifest.json"))) return;
        try
        {
            var fingerprint = GetBundleFingerprint(state.Path);
            if (fingerprint == state.Fingerprint) return;
            // Never overwrite the developer's Git worktree with a store package:
            // reload its existing path after the developer updates the source.
            var result = await ConfigureEgoAsync(state.Path, allowInstall: false,
                forceReload: true);
            if (!result.Success || result.ExtensionId != state.ExtensionId)
            {
                HostAssets.AppendLog($"[BrowserHelper] Ego managed reload skipped: {result.Message}");
                return;
            }
            SaveManagedEgo(state with { Fingerprint = fingerprint });
            HostAssets.AppendLog($"[BrowserHelper] Ego managed {state.Mode} extension reloaded, ID preserved.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[BrowserHelper] Ego managed reload error: {ex.GetType().Name}");
        }
    }
}
