using System;
using System.IO;
using System.Net.Http;
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
    private string _resolvedUpdateUrl = "";

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
            _resolvedUpdateUrl = $"{_proxyUrlPrefix.TrimEnd('/')}/{_repoUrl.TrimEnd('/')}/releases/latest/download/";
            var source = new SimpleWebSource(_resolvedUpdateUrl);
            _updateManager = new UpdateManager(source);

            // 记录完整的诊断启动信息
            var isInstalled = _updateManager.IsInstalled;
            var currentVersion = _updateManager.IsInstalled ? _updateManager.CurrentVersion?.ToString() ?? "null" : "N/A (not installed)";
            var appId = _updateManager.AppId ?? "null";
            HostAssets.AppendLog(
                $"VelopackUpdateService: initialized OK. " +
                $"isInstalled={isInstalled}, currentVersion={currentVersion}, appId={appId}, " +
                $"updateUrl={_resolvedUpdateUrl}, " +
                $"baseDir={AppDomain.CurrentDomain.BaseDirectory}, " +
                $"processPath={Environment.ProcessPath}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog(
                $"VelopackUpdateService: FAILED to initialize UpdateManager.\n" +
                $"  ExceptionType: {ex.GetType().FullName}\n" +
                $"  Message: {ex.Message}\n" +
                $"  StackTrace: {ex.StackTrace}\n" +
                $"  InnerException: {ex.InnerException}");
        }
    }

    /// <summary>
    /// 检测是否有新版本可用
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_updateManager == null)
        {
            HostAssets.AppendLog("VelopackUpdateService: _updateManager is null, attempting re-init...");
            InitializeManager();
        }

        if (_updateManager == null)
        {
            HostAssets.AppendLog("VelopackUpdateService: _updateManager still null after re-init, aborting check.");
            UpdateStatusChanged?.Invoke("更新管理器未就绪。");
            return null;
        }

        try
        {
            // 在执行检测前，输出运行时诊断快照
            var isInstalled = _updateManager.IsInstalled;
            var currentVer = isInstalled ? _updateManager.CurrentVersion?.ToString() ?? "null" : "N/A";
            HostAssets.AppendLog(
                $"VelopackUpdateService: CheckForUpdatesAsync starting. " +
                $"isInstalled={isInstalled}, currentVer={currentVer}, url={_resolvedUpdateUrl}");

            UpdateStatusChanged?.Invoke("正在检测新版本...");

            // 先尝试验证网络连通性（预诊断）
            await DiagnoseNetworkAsync(_resolvedUpdateUrl);

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                HostAssets.AppendLog("VelopackUpdateService: CheckForUpdatesAsync returned null (already up to date).");
                UpdateStatusChanged?.Invoke("当前已是最新版本。");
                return null;
            }

            var newVersion = updateInfo.TargetFullRelease.Version.ToString();
            HostAssets.AppendLog($"VelopackUpdateService: new version found: v{newVersion}");
            UpdateStatusChanged?.Invoke($"发现新版本: v{newVersion}");
            return updateInfo;
        }
        catch (Exception ex)
        {
            // 记录完整的异常堆栈链
            LogUpdateException("CheckForUpdatesAsync", ex);

            var friendlyMsg = BuildFriendlyErrorMessage(ex);
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
            HostAssets.AppendLog("VelopackUpdateService: DownloadUpdatesAsync aborted (manager or updateInfo is null).");
            return false;
        }

        if (_isDownloading)
        {
            HostAssets.AppendLog("VelopackUpdateService: DownloadUpdatesAsync skipped (already downloading).");
            return false;
        }

        _isDownloading = true;
        try
        {
            var targetVer = updateInfo.TargetFullRelease.Version.ToString();
            HostAssets.AppendLog($"VelopackUpdateService: DownloadUpdatesAsync starting for v{targetVer}");
            UpdateStatusChanged?.Invoke("正在后台下载增量更新...");
            DownloadProgressChanged?.Invoke(0);

            // 传入进度回调，Velopack 自动异步调用
            await _updateManager.DownloadUpdatesAsync(updateInfo, (progress) =>
            {
                DownloadProgressChanged?.Invoke(progress);
            });

            DownloadProgressChanged?.Invoke(100);
            HostAssets.AppendLog($"VelopackUpdateService: download completed for v{targetVer}");
            UpdateStatusChanged?.Invoke("更新包已下载完成，重启即可生效！");
            return true;
        }
        catch (Exception ex)
        {
            LogUpdateException("DownloadUpdatesAsync", ex);

            var friendlyMsg = BuildFriendlyErrorMessage(ex);
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
            HostAssets.AppendLog("VelopackUpdateService: ApplyAndRestart aborted (manager or updateInfo is null).");
            return;
        }

        try
        {
            HostAssets.AppendLog($"VelopackUpdateService: ApplyAndRestart for v{updateInfo.TargetFullRelease.Version}...");
            _updateManager.ApplyUpdatesAndRestart(updateInfo);
        }
        catch (Exception ex)
        {
            LogUpdateException("ApplyAndRestart", ex);
            UpdateStatusChanged?.Invoke($"重启失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 记录完整的异常诊断信息（含堆栈、内部异常链、异常类型）
    /// </summary>
    private static void LogUpdateException(string operation, Exception ex)
    {
        var logBuilder = new System.Text.StringBuilder();
        logBuilder.AppendLine($"VelopackUpdateService: {operation} FAILED");
        logBuilder.AppendLine($"  ExceptionType : {ex.GetType().FullName}");
        logBuilder.AppendLine($"  Message       : {ex.Message}");
        logBuilder.AppendLine($"  StackTrace    : {ex.StackTrace}");

        var inner = ex.InnerException;
        int depth = 1;
        while (inner != null && depth <= 5)
        {
            logBuilder.AppendLine($"  InnerException[{depth}] Type    : {inner.GetType().FullName}");
            logBuilder.AppendLine($"  InnerException[{depth}] Message : {inner.Message}");
            logBuilder.AppendLine($"  InnerException[{depth}] Stack   : {inner.StackTrace}");
            inner = inner.InnerException;
            depth++;
        }

        HostAssets.AppendLog(logBuilder.ToString());
    }

    /// <summary>
    /// 将技术异常信息翻译为中文友好提示
    /// </summary>
    private static string BuildFriendlyErrorMessage(Exception ex)
    {
        var msg = ex.Message ?? "";

        if (msg.Contains("not installed", StringComparison.OrdinalIgnoreCase))
        {
            return "当前运行为非安装模式，自动更新仅在打包安装版中可用。";
        }

        // 网络超时
        if (ex is TaskCanceledException || ex is OperationCanceledException)
        {
            return $"网络请求超时，请检查网络连接后重试。(原始: {msg})";
        }

        // HTTP 请求失败
        if (ex is HttpRequestException httpEx)
        {
            return $"网络请求失败 (HTTP {httpEx.StatusCode}): {msg}";
        }

        // 文件系统权限
        if (ex is UnauthorizedAccessException)
        {
            return $"文件访问被拒绝，请以管理员身份运行。(路径: {msg})";
        }

        // IO 异常
        if (ex is IOException)
        {
            return $"文件读写失败: {msg}";
        }

        // 通用回退
        return $"{msg} (详情已记录至日志: {HostAssets.HostLogPath})";
    }

    /// <summary>
    /// 在正式检测前，预先诊断到更新源的网络连通性，并将结果写入日志
    /// </summary>
    private static async Task DiagnoseNetworkAsync(string updateUrl)
    {
        try
        {
            // 尝试请求 RELEASES 文件（Velopack 约定），仅判定是否可达
            var releasesUrl = updateUrl.TrimEnd('/') + "/RELEASES";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync(releasesUrl, HttpCompletionOption.ResponseHeadersRead);
            HostAssets.AppendLog(
                $"VelopackUpdateService: network diagnosis OK. " +
                $"url={releasesUrl}, statusCode={response.StatusCode}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog(
                $"VelopackUpdateService: network diagnosis FAILED. " +
                $"ExceptionType={ex.GetType().FullName}, Message={ex.Message}");
        }
    }
}
