using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Yanzi.Platform.Mac;

public enum UpdateChannelMode
{
    Mirror,   // 国内镜像加速 (ghfast.top)
    Official  // GitHub 官方直连
}

public class MacReleaseInfo
{
    public string Version { get; set; } = "0.0.0";
    public string TagName { get; set; } = "";
    public string Title { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string AssetFileName { get; set; } = "";
    public long AssetSize { get; set; }
    public DateTime PublishedAt { get; set; }
    public bool IsNewer { get; set; }
}

public sealed class MacUpdateService
{
    private static readonly Lazy<MacUpdateService> _instance = new(() => new MacUpdateService());
    public static MacUpdateService Instance => _instance.Value;

    private readonly string _repo = "luoluoluo22/yanzi";
    private readonly HttpClient _httpClient;

    public const string DefaultMirrorPrefix = "https://ghfast.top/";
    public const string BackupMirrorPrefix = "https://gh.ddlc.top/";

    public string CurrentVersion { get; }
    public UpdateChannelMode CurrentChannel { get; set; } = UpdateChannelMode.Mirror;

    public bool IsChecking { get; private set; }
    public bool IsDownloading { get; private set; }
    public int DownloadProgress { get; private set; }
    public MacReleaseInfo? LatestRelease { get; private set; }
    public string? DownloadedPackagePath { get; private set; }

    public event Action<int>? DownloadProgressChanged;
    public event Action<string>? StatusMessageChanged;

    private MacUpdateService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Yanzi-Mac-Client/1.0");

        // Read current assembly version or fallback
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersion = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.1.0";
    }

    /// <summary>
    /// 检查最新版本发布信息
    /// </summary>
    public async Task<MacReleaseInfo?> CheckForUpdatesAsync(UpdateChannelMode? mode = null, CancellationToken ct = default)
    {
        if (mode.HasValue) CurrentChannel = mode.Value;

        IsChecking = true;
        StatusMessageChanged?.Invoke("正在检查更新...");

        try
        {
            string apiUrl = $"https://api.github.com/repos/{_repo}/releases";
            if (CurrentChannel == UpdateChannelMode.Mirror)
            {
                // In mirror mode, we can fetch via mirror or direct GitHub API
                apiUrl = $"https://api.github.com/repos/{_repo}/releases";
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.Add("Accept", "application/vnd.github.v3+json");
            if (req.Headers.UserAgent.Count == 0)
            {
                req.Headers.Add("User-Agent", "Yanzi-Mac-Client");
            }

            var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                StatusMessageChanged?.Invoke($"检查更新失败: HTTP {resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                StatusMessageChanged?.Invoke("检查更新失败: 响应格式异常");
                return null;
            }

            // Iterate releases to find latest macOS release
            foreach (var releaseElem in doc.RootElement.EnumerateArray())
            {
                var tagName = releaseElem.GetProperty("tag_name").GetString() ?? "";
                var title = releaseElem.TryGetProperty("name", out var n) ? n.GetString() ?? tagName : tagName;
                var body = releaseElem.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                var isDraft = releaseElem.TryGetProperty("draft", out var d) && d.GetBoolean();
                if (isDraft) continue;

                var publishedStr = releaseElem.TryGetProperty("published_at", out var p) ? p.GetString() : null;
                DateTime.TryParse(publishedStr, out var publishedAt);

                // Find macOS assets (zip or dmg)
                string downloadUrl = "";
                string assetName = "";
                long assetSize = 0;

                if (releaseElem.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElem.EnumerateArray())
                    {
                        var aName = asset.GetProperty("name").GetString() ?? "";
                        var aUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        var aSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

                        if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            (aName.Contains("mac", StringComparison.OrdinalIgnoreCase) || aName.Contains("osx", StringComparison.OrdinalIgnoreCase) || aName.StartsWith("Yanzi", StringComparison.OrdinalIgnoreCase)))
                        {
                            downloadUrl = aUrl;
                            assetName = aName;
                            assetSize = aSize;
                            break;
                        }
                        else if (aName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(downloadUrl))
                            {
                                downloadUrl = aUrl;
                                assetName = aName;
                                assetSize = aSize;
                            }
                        }
                    }
                }

                var cleanRemoteVer = ExtractVersion(tagName);
                var isNewer = CompareVersions(cleanRemoteVer, CurrentVersion) > 0;

                var releaseInfo = new MacReleaseInfo
                {
                    Version = cleanRemoteVer,
                    TagName = tagName,
                    Title = title,
                    ReleaseNotes = body,
                    DownloadUrl = downloadUrl,
                    AssetFileName = assetName,
                    AssetSize = assetSize,
                    PublishedAt = publishedAt,
                    IsNewer = isNewer
                };

                LatestRelease = releaseInfo;
                if (isNewer)
                {
                    StatusMessageChanged?.Invoke($"🎉 发现新版本: v{cleanRemoteVer}");
                }
                else
                {
                    StatusMessageChanged?.Invoke("🟢 当前已是最新版本");
                }

                return releaseInfo;
            }

            StatusMessageChanged?.Invoke("未发现可用的发布版本");
            return null;
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke($"检查更新失败: {ex.Message}");
            return null;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// 下载更新包
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(MacReleaseInfo release, Action<int>? onProgress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            StatusMessageChanged?.Invoke("更新包下载地址为空");
            return null;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        StatusMessageChanged?.Invoke("正在下载更新包...");

        try
        {
            var url = release.DownloadUrl;
            if (CurrentChannel == UpdateChannelMode.Mirror)
            {
                url = $"{DefaultMirrorPrefix.TrimEnd('/')}/{url.TrimStart('/')}";
            }

            var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yanzi", "updates");
            Directory.CreateDirectory(cacheDir);

            var targetFileName = string.IsNullOrWhiteSpace(release.AssetFileName) ? $"Yanzi-macos-v{release.Version}.zip" : release.AssetFileName;
            var targetPath = Path.Combine(cacheDir, targetFileName);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? release.AssetSize;
            var canReportProgress = totalBytes > 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                if (canReportProgress)
                {
                    var progress = (int)((totalRead * 100) / totalBytes);
                    if (progress != DownloadProgress)
                    {
                        DownloadProgress = progress;
                        DownloadProgressChanged?.Invoke(progress);
                        onProgress?.Invoke(progress);
                        StatusMessageChanged?.Invoke($"正在下载更新包... {progress}%");
                    }
                }
            }

            DownloadedPackagePath = targetPath;
            StatusMessageChanged?.Invoke("✅ 更新包下载完成，准备安装");
            return targetPath;
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke($"下载更新失败: {ex.Message}");
            return null;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// 一键安装更新并自动重启应用
    /// </summary>
    public bool ApplyUpdateAndRestart(string packagePath)
    {
        try
        {
            if (!File.Exists(packagePath))
            {
                StatusMessageChanged?.Invoke("更新文件不存在");
                return false;
            }

            var currentAppPath = FindCurrentAppBundlePath();
            if (string.IsNullOrEmpty(currentAppPath))
            {
                currentAppPath = "/Applications/Yanzi.app";
            }

            var stagingDir = Path.Combine(Path.GetDirectoryName(packagePath)!, "staging_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(stagingDir);

            // Extract zip or handle dmg
            if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(packagePath, stagingDir, true);

                // Find Yanzi.app in extracted staging directory
                var extractedAppPath = Path.Combine(stagingDir, "Yanzi.app");
                if (!Directory.Exists(extractedAppPath))
                {
                    var foundDirs = Directory.GetDirectories(stagingDir, "*.app", SearchOption.AllDirectories);
                    if (foundDirs.Length > 0)
                    {
                        extractedAppPath = foundDirs[0];
                    }
                }

                if (!Directory.Exists(extractedAppPath))
                {
                    StatusMessageChanged?.Invoke("更新包中未找到有效的 Yanzi.app");
                    return false;
                }

                // Generate bash update script
                var scriptPath = Path.Combine(Path.GetTempPath(), "yanzi_update_restart.sh");
                var scriptContent = $@"#!/bin/bash
sleep 1
# Replace target app bundle
rm -rf ""{currentAppPath}""
cp -R ""{extractedAppPath}"" ""{currentAppPath}""
# Clear quarantine
xattr -cr ""{currentAppPath}"" 2>/dev/null || true
# Clean staging
rm -rf ""{stagingDir}""
# Launch updated application
open ""{currentAppPath}""
";
                File.WriteAllText(scriptPath, scriptContent);
                Process.Start("chmod", $"+x \"{scriptPath}\"").WaitForExit();

                // Launch updater script in background
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                // Terminate current process cleanly
                Environment.Exit(0);
                return true;
            }
            else if (packagePath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
            {
                // Open DMG installer for user
                Process.Start("open", $"\"{packagePath}\"");
                StatusMessageChanged?.Invoke("已为您打开 DMG 安装镜像，请拖拽更新");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke($"安装更新失败: {ex.Message}");
            return false;
        }
    }

    private string? FindCurrentAppBundlePath()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null && dir.Parent != null)
            {
                if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch { }

        return null;
    }

    private static string ExtractVersion(string tag)
    {
        var match = Regex.Match(tag, @"\d+(\.\d+)+");
        return match.Success ? match.Value : tag;
    }

    private static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(v1, out var ver1) && Version.TryParse(v2, out var ver2))
        {
            return ver1.CompareTo(ver2);
        }
        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }
}
