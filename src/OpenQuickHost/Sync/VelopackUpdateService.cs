using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace OpenQuickHost.Sync;

public sealed class VelopackUpdateService
{
    private static readonly Lazy<VelopackUpdateService> _instance = new(() => new VelopackUpdateService());
    public static VelopackUpdateService Instance => _instance.Value;

    private readonly string _repoUrl = "https://github.com/luoluoluo22/yanzi";
    private readonly string _proxyUrlPrefix = "https://ghfast.top/";
    private UpdateManager? _updateManager;
    private bool _isDownloading;

    public event Action<int>? DownloadProgressChanged;
    public event Action<string>? UpdateStatusChanged;

    private VelopackUpdateService()
    {
        InitializeManager();
    }

    private void InitializeManager()
    {
        try
        {
            // 利用国内加速 CDN 对 GitHub Release 路径进行代理包装
            // Velopack 的 SimpleWebSource 期望拿到 releases.json 所在的根目录
            // 在 GitHub Release 中，文件的物理下载地址根目录大约是 releases/latest/download/ 
            // SimpleWebSource 会去请求 $url/RELEASES，所以我们把加速前缀注入进去
            var updateUrl = $"{_proxyUrlPrefix.TrimEnd('/')}/{_repoUrl.TrimEnd('/')}/releases/latest/download/";
            var source = new SimpleWebSource(updateUrl);
            _updateManager = new UpdateManager(source);
            HostAssets.AppendLog($"VelopackUpdateService: initialized with proxy source={updateUrl}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"VelopackUpdateService: failed to initialize UpdateManager: {ex.Message}");
        }
    }

    /// <summary>
    /// 检测是否有新版本可用
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_updateManager == null)
        {
            InitializeManager();
        }

        if (_updateManager == null)
        {
            UpdateStatusChanged?.Invoke("更新管理器未就绪。");
            return null;
        }

        try
        {
            UpdateStatusChanged?.Invoke("正在检测新版本...");
            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                UpdateStatusChanged?.Invoke("当前已是最新版本。");
                return null;
            }

            var newVersion = updateInfo.TargetFullRelease.Version.ToString();
            UpdateStatusChanged?.Invoke($"发现新版本: v{newVersion}");
            return updateInfo;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"VelopackUpdateService: update check failed: {ex.Message}");
            var friendlyMsg = ex.Message;
            if (ex.Message != null && ex.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase))
            {
                friendlyMsg = "当前运行为非安装模式，自动更新仅在打包安装版中可用。";
            }
            UpdateStatusChanged?.Invoke($"检测失败: {friendlyMsg}");
            return null;
        }
    }

    /// <summary>
    /// 后台静默下载更新包
    /// </summary>
    public async Task<bool> DownloadUpdatesAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        if (_updateManager == null || updateInfo == null)
        {
            return false;
        }

        if (_isDownloading)
        {
            return false;
        }

        _isDownloading = true;
        try
        {
            UpdateStatusChanged?.Invoke("正在后台下载增量更新...");
            DownloadProgressChanged?.Invoke(0);

            // 传入进度回调，Velopack 自动异步调用
            await _updateManager.DownloadUpdatesAsync(updateInfo, (progress) =>
            {
                DownloadProgressChanged?.Invoke(progress);
            });

            DownloadProgressChanged?.Invoke(100);
            UpdateStatusChanged?.Invoke("更新包已下载完成，重启即可生效！");
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"VelopackUpdateService: download failed: {ex.Message}");
            var friendlyMsg = ex.Message;
            if (ex.Message != null && ex.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase))
            {
                friendlyMsg = "当前运行为非安装模式，自动下载仅在打包安装版中可用。";
            }
            UpdateStatusChanged?.Invoke($"下载失败: {friendlyMsg}");
            return false;
        }
        finally
        {
            _isDownloading = false;
        }
    }

    /// <summary>
    /// 立即关机应用并完成升级覆盖，随后自我重启
    /// </summary>
    public void ApplyAndRestart(UpdateInfo updateInfo)
    {
        if (_updateManager == null || updateInfo == null)
        {
            return;
        }

        try
        {
            HostAssets.AppendLog($"VelopackUpdateService: applying updates and restarting...");
            _updateManager.ApplyUpdatesAndRestart(updateInfo);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"VelopackUpdateService: restart failed: {ex.Message}");
            UpdateStatusChanged?.Invoke($"重启失败: {ex.Message}");
        }
    }
}
