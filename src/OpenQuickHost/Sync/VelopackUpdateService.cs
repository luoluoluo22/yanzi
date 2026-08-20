using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace OpenQuickHost.Sync;

public enum UpdateChannelMode
{
    Official,
    Mirror
}

public sealed class VelopackUpdateService
{
    private static readonly Lazy<VelopackUpdateService> _instance = new(() => new VelopackUpdateService());
    public static VelopackUpdateService Instance => _instance.Value;

    private readonly string _repoUrl = "https://github.com/luoluoluo22/yanzi";
    public const string DefaultMirrorPrefix = "https://ghfast.top/";
    public const string BackupMirrorPrefix = "https://gh.ddlc.top/";

    private UpdateChannelMode _currentChannelMode = UpdateChannelMode.Mirror;
    public UpdateChannelMode CurrentChannelMode => _currentChannelMode;

    private UpdateManager? _updateManager;
    private bool _isDownloading;
    private string _resolvedUpdateUrl = "";

    public bool IsDownloading => _isDownloading;
    public int CurrentProgress { get; private set; } = 0;

    public event Action<int>? DownloadProgressChanged;
    public event Action<string>? UpdateStatusChanged;

    public UpdateInfo? ReadyUpdateInfo { get; private set; }
    public bool IsUpdateReady { get; private set; }
    public event Action? UpdateReadyChanged;

    private int _silentCheckFailureCount = 0;
    private const int MaxSilentCheckFailures = 3;

    private VelopackUpdateService()
    {
        InitializeManager(UpdateChannelMode.Mirror);
    }

    public void InitializeManager(UpdateChannelMode mode)
    {
        try
        {
            _currentChannelMode = mode;
            if (mode == UpdateChannelMode.Mirror)
            {
                // 利用网页端同款加速镜像对 GitHub Release 路径进行代理包装
                _resolvedUpdateUrl = $"{DefaultMirrorPrefix.TrimEnd('/')}/{_repoUrl.TrimEnd('/')}/releases/latest/download/";
            }
            else
            {
                // 官方 GitHub 直连源
                _resolvedUpdateUrl = $"{_repoUrl.TrimEnd('/')}/releases/latest/download/";
            }

            var source = new SimpleWebSource(_resolvedUpdateUrl);
            _updateManager = new UpdateManager(source, null, new HostVelopackLogger());
            
            // 记录完整的诊断启动信息
            var isInstalled = _updateManager.IsInstalled;
            var currentVersion = _updateManager.IsInstalled ? _updateManager.CurrentVersion?.ToString() ?? "null" : "N/A (not installed)";
            var appId = _updateManager.AppId ?? "null";
            HostAssets.AppendLog(
                $"VelopackUpdateService: initialized OK (mode={_currentChannelMode}). " +
                $"isInstalled={isInstalled}, currentVersion={currentVersion}, appId={appId}, " +
                $"updateUrl={_resolvedUpdateUrl}, " +
                $"baseDir={AppDomain.CurrentDomain.BaseDirectory}, " +
                $"processPath={Environment.ProcessPath}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog(
                $"VelopackUpdateService: FAILED to initialize UpdateManager (mode={mode}).\n" +
                $"  ExceptionType: {ex.GetType().FullName}\n" +
                $"  Message: {ex.Message}\n" +
                $"  StackTrace: {ex.StackTrace}\n" +
                $"  InnerException: {ex.InnerException}");
        }
    }

    /// <summary>
    /// 检测是否有新版本可用（指定更新源通道）
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(UpdateChannelMode channelMode, CancellationToken cancellationToken = default)
    {
        if (_updateManager == null || _currentChannelMode != channelMode)
        {
            InitializeManager(channelMode);
        }

        if (_updateManager == null)
        {
            HostAssets.AppendLog($"VelopackUpdateService: _updateManager still null after init ({channelMode}), aborting check.");
            UpdateStatusChanged?.Invoke("更新管理器未就绪。");
            return null;
        }

        var channelName = channelMode == UpdateChannelMode.Mirror ? "镜像源 (ghfast.top)" : "官方源 (GitHub)";
        try
        {
            // 在执行检测前，输出运行时诊断快照
            var isInstalled = _updateManager.IsInstalled;
            var currentVer = isInstalled ? _updateManager.CurrentVersion?.ToString() ?? "null" : "N/A";
            HostAssets.AppendLog(
                $"VelopackUpdateService: CheckForUpdatesAsync starting [{channelName}]. " +
                $"isInstalled={isInstalled}, currentVer={currentVer}, url={_resolvedUpdateUrl}");

            UpdateStatusChanged?.Invoke($"[{channelName}] 正在检测新版本...");

            // 先尝试验证网络连通性（预诊断）
            await DiagnoseNetworkAsync(_resolvedUpdateUrl);

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                HostAssets.AppendLog($"VelopackUpdateService: CheckForUpdatesAsync returned null (already up to date) via {channelName}.");
                UpdateStatusChanged?.Invoke($"[{channelName}] 当前已是最新版本。");
                return null;
            }

            var newVersion = updateInfo.TargetFullRelease.Version.ToString();
            HostAssets.AppendLog($"VelopackUpdateService: new version found: v{newVersion} via {channelName}");
            UpdateStatusChanged?.Invoke($"[{channelName}] 发现新版本: v{newVersion}");
            return updateInfo;
        }
        catch (Exception ex)
        {
            // 记录完整的异常堆栈链
            LogUpdateException($"CheckForUpdatesAsync [{channelName}]", ex);

            var friendlyMsg = BuildFriendlyErrorMessage(ex);
            UpdateStatusChanged?.Invoke($"[{channelName}] 检测失败: {friendlyMsg}");
            return null;
        }
    }

    /// <summary>
    /// 检测是否有新版本可用（使用当前通道）
    /// </summary>
    public Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesAsync(_currentChannelMode, cancellationToken);
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

        var channelName = _currentChannelMode == UpdateChannelMode.Mirror ? "镜像源 (ghfast.top)" : "官方源 (GitHub)";
        _isDownloading = true;
        try
        {
            var targetVer = updateInfo.TargetFullRelease.Version.ToString();
            HostAssets.AppendLog($"VelopackUpdateService: DownloadUpdatesAsync starting for v{targetVer} via {channelName}");
            UpdateStatusChanged?.Invoke($"[{channelName}] 正在后台下载增量更新...");
            CurrentProgress = 0;
            DownloadProgressChanged?.Invoke(0);

            // 传入进度回调，Velopack 自动异步调用
            await _updateManager.DownloadUpdatesAsync(updateInfo, (progress) =>
            {
                CurrentProgress = progress;
                DownloadProgressChanged?.Invoke(progress);
            });

            CurrentProgress = 100;
            DownloadProgressChanged?.Invoke(100);
            HostAssets.AppendLog($"VelopackUpdateService: download completed for v{targetVer} via {channelName}");
            UpdateStatusChanged?.Invoke($"[{channelName}] 更新包已下载完成，重启即可生效！");

            ReadyUpdateInfo = updateInfo;
            IsUpdateReady = true;
            UpdateReadyChanged?.Invoke();

            return true;
        }
        catch (Exception ex)
        {
            LogUpdateException($"DownloadUpdatesAsync [{channelName}]", ex);

            var friendlyMsg = BuildFriendlyErrorMessage(ex);
            UpdateStatusChanged?.Invoke($"[{channelName}] 下载失败: {friendlyMsg}");
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
    /// 启动应用后的静默检测更新和下载逻辑（有失败重试、避免重复下载和最大失败上限保护）
    /// </summary>
    public async Task StartSilentUpdateCheckAndDownloadAsync()
    {
        // 1. 用户配置检查
        var settings = AppSettingsStore.Load();
        if (!settings.EnableAutoUpdate)
        {
            HostAssets.AppendLog("VelopackUpdateService: auto update is disabled by user settings.");
            return;
        }

        // 2. 重复任务规避：是否已就绪
        if (IsUpdateReady)
        {
            HostAssets.AppendLog("VelopackUpdateService: an update is already ready, skipping silent check.");
            return;
        }

        // 3. 保护机制：累计失败达到上限则规避，不进行死循环重试
        if (_silentCheckFailureCount >= MaxSilentCheckFailures)
        {
            HostAssets.AppendLog($"VelopackUpdateService: silent update check skipped due to too many failures ({_silentCheckFailureCount}).");
            return;
        }

        try
        {
            var updateInfo = await CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                return;
            }

            int downloadAttempts = 0;
            bool downloadSuccess = false;

            // 4. 重试机制：若异常中断，最多重试 3 次，每次间隔 5 秒
            while (downloadAttempts < 3 && !downloadSuccess)
            {
                downloadAttempts++;
                HostAssets.AppendLog($"VelopackUpdateService: silent download attempt {downloadAttempts} of 3...");
                downloadSuccess = await DownloadUpdatesAsync(updateInfo);
                if (downloadSuccess)
                {
                    break;
                }

                if (downloadAttempts < 3)
                {
                    HostAssets.AppendLog("VelopackUpdateService: silent download failed, retrying in 5 seconds...");
                    await Task.Delay(5000);
                }
            }

            if (downloadSuccess)
            {
                _silentCheckFailureCount = 0; // 成功后清空连续失败记录
                HostAssets.AppendLog($"VelopackUpdateService: silent update check and download completed. Ready to update to v{updateInfo.TargetFullRelease.Version}.");
            }
            else
            {
                _silentCheckFailureCount++;
                HostAssets.AppendLog($"VelopackUpdateService: silent update download failed after 3 attempts. Failure count={_silentCheckFailureCount}.");
            }
        }
        catch (Exception ex)
        {
            _silentCheckFailureCount++;
            HostAssets.AppendLog($"VelopackUpdateService: silent update process exception (fail count={_silentCheckFailureCount}): {ex.Message}");
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
