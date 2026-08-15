using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfBrush = System.Windows.Media.SolidColorBrush;
using Microsoft.Win32;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace OpenQuickHost;

public static class WindowDwmBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(WindowDwmBehavior), new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);
    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && (bool)e.NewValue)
        {
            window.SourceInitialized -= Window_SourceInitialized;
            window.SourceInitialized += Window_SourceInitialized;
        }
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            App.UpdateWindowDwmTheme(window);
        }
    }
}

public partial class App : WpfApplication
{
    private const string SingleInstanceAppId = "Yanzi.OpenQuickHost";

    private Forms.NotifyIcon? _notifyIcon;
    private SettingsWindow? _settingsWindow;
    private RunningExtensionsWindow? _runningExtensionsWindow;
    private InputStateWindow? _inputStateWindow;
    private LocalAgentApiServer? _agentApiServer;
    public LocalAgentApiServer? AgentApiServer => _agentApiServer;
    private LanDiscoveryService? _lanDiscoveryService;
    private SingleInstanceService? _singleInstanceService;
    private bool _listenerServicesPaused;
    private bool _isAutoPausedByBlacklist;
    private IntPtr _foregroundHook = IntPtr.Zero;
    private WinEventDelegate? _winEventDelegate;
    private bool _isAppFullyInitialized;
    private string _lastTrayForegroundProcess = string.Empty;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        // 运行端到端加密 E2EE 模块启动自检
        try
        {
            Sync.SyncCryptoService.SelfTest();
            HostAssets.AppendLog("E2EE SyncCryptoService self-test passed successfully.");
        }
        catch (System.Exception ex)
        {
            HostAssets.AppendLog($"CRITICAL: E2EE SyncCryptoService self-test failed! {ex.Message}");
            System.Windows.MessageBox.Show($"加密服务启动自检失败：{ex.Message}\n请检查系统加密组件是否完整。", "安全自检失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // 0. 将工作目录切换到临时目录，防止进程被强杀后工作目录句柄锁住安装目录
        //    导致 Velopack 覆盖安装时 "Failed to remove existing application directory" 错误
        try { System.IO.Directory.SetCurrentDirectory(System.IO.Path.GetTempPath()); } catch { /* ignore */ }

        // 1. 必须最先执行，拦截 Velopack 的命令行钩子（如快捷方式生成、升级更新等）
        Velopack.VelopackApp.Build()
            .WithAfterInstallFastCallback(v =>
            {
                try
                {
                    // 在安装过程中，如果检测到旧版（C:\Program Files\Yanzi），直接静默卸载清理
                    LegacyCleanupService.SilentUninstallOldVersion();

                    // 注册 yanzi:// URI 协议到 HKCU
                    var exePath = System.Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        UriProtocolRegistrationService.EnsureRegistered(exePath);
                    }

                    // 注册开机自启
                    var settings = AppSettingsStore.Load();
                    StartupRegistrationService.Apply(settings.LaunchAtStartup);
                }
                catch (System.Exception ex)
                {
                    HostAssets.AppendLog($"Velopack AfterInstall Hook error: {ex.Message}");
                }
            })
            .WithBeforeUninstallFastCallback(v =>
            {
                try
                {
                    // 清理 URI 协议注册
                    UriProtocolRegistrationService.Unregister();

                    // 清理开机自启注册
                    StartupRegistrationService.Apply(false);

                    // 停止所有关联的 Everything 进程，防止目录被占用无法删除
                    EverythingRuntimeService.KillAllYanziEverythingProcesses();

                    // 清理软件的所有本地运行数据与用户配置目录 (%LOCALAPPDATA%\OpenQuickHost)
                    try
                    {
                        var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
                        var dataDir = System.IO.Path.Combine(localAppData, "OpenQuickHost");
                        if (System.IO.Directory.Exists(dataDir))
                        {
                            // 启动独立分离的 cmd 进程，在 1 秒后强删数据目录（等待当前进程完全退出释放句柄锁）
                            var cmdText = $"/c timeout /t 1 /nobreak >nul & rd /s /q \"{dataDir}\"";
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = cmdText,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                    }
                    catch { /* ignore */ }
                }
                catch (System.Exception ex)
                {
                    HostAssets.AppendLog($"Velopack BeforeUninstall Hook error: {ex.Message}");
                }
                System.Environment.Exit(0);
            })
            .WithBeforeUpdateFastCallback(v =>
            {
                try
                {
                    // 停止所有关联的 Everything 进程，防止旧目录被占用无法清理或覆盖
                    EverythingRuntimeService.KillAllYanziEverythingProcesses();
                }
                catch (System.Exception ex)
                {
                    HostAssets.AppendLog($"Velopack BeforeUpdate Hook error: {ex.Message}");
                }
            })
            .WithAfterUpdateFastCallback(v =>
            {
                try
                {
                    // 更新后重新注册 URI 协议（路径可能变化）
                    var exePath = System.Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        UriProtocolRegistrationService.EnsureRegistered(exePath);
                    }
                }
                catch (System.Exception ex)
                {
                    HostAssets.AppendLog($"Velopack AfterUpdate Hook error: {ex.Message}");
                }
            })
            .Run();

        // 2. 立即执行单实例拦截，拒绝任何多开开销与初始化异常。
        // 将此逻辑提到最前，不仅大幅降低了多实例点击时的 CPU/IO 损耗，更避免了多个进程并发做环境初始化（如读写配置、加载 Everything）所产生的死锁和异常崩溃。
        _singleInstanceService = new SingleInstanceService(SingleInstanceAppId);
        if (!_singleInstanceService.TryAcquirePrimaryInstance())
        {
            try
            {
                var protocolArgument = e.Args.FirstOrDefault(static arg =>
                    arg.StartsWith("yanzi://", StringComparison.OrdinalIgnoreCase));
                var message = string.IsNullOrWhiteSpace(protocolArgument)
                    ? "__show__"
                    : protocolArgument;
                
                // 同步等待 Named Pipe 通信（至多 300 毫秒），超时自动断开
                using var cts = new CancellationTokenSource(300);
                _ = _singleInstanceService.SendToPrimaryInstanceAsync(message, cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略任何发送侧异常，以闪退为第一核心纪律
            }
            finally
            {
                // 极其彻底、闪瞬地秒杀当前冗余进程，在内存中绝不容许留下半个字节
                System.Environment.Exit(0);
            }
            return;
        }

        // 3. 只有抢占到 Mutex 的唯一主实例，才进行后续复杂的环境配置初始化
        TrySetProcessDpiAwareness();
        base.OnStartup(e);
        
        // 绑定未处理异常捕获，开始进入核心初始化阶段
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        SyncConfigLoader.EnsureExampleFile();
        var settings = AppSettingsStore.Load();
        ApplyTheme(settings.ThemeMode);
        EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler(Window_GlobalLoaded));
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        StartupRegistrationService.Apply(settings.LaunchAtStartup);
        EverythingRuntimeService.EnsureStartedInBackground();

        _ = Task.Run(() => OpenQuickHost.Sync.ExtensionRecycleBinService.PurgeExpiredItems(30));

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            TryRegisterUriProtocol();
            _notifyIcon = BuildNotifyIcon(window);
            HostAssets.AppendLog($"App startup: version={AppVersionInfo.Version}, build={AppVersionInfo.BuildStamp}, baseDir={AppDomain.CurrentDomain.BaseDirectory}");
            window.Show();
            if (ShouldStartHidden(e.Args))
            {
                window.HideToTray();
            }
            else
            {
                window.ShowPanel();
            }

            StartLocalAgentApi(window, settings);
            _singleInstanceService.StartServer(message => HandleSecondaryLaunchMessageAsync(window, message));
            _ = HandleLaunchArgumentsAsync(window, e.Args);

            // 4. 标识整个应用的所有核心初始化步骤均顺利执行完成，正式转换为运行期柔性容错模式
            _isAppFullyInitialized = true;

            // 预加载设置窗口以避免第一次打开时解析庞大 XAML 导致 UI 线程和鼠标卡顿
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_settingsWindow == null && MainWindow is MainWindow mainWindow)
                {
                    try
                    {
                        _settingsWindow = new SettingsWindow(mainWindow);
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"Settings window pre-load failed: {ex.Message}");
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            StartForegroundMonitorTimer();

            // 启动 5 秒后在后台静默发起更新流程
            _ = Task.Delay(5000).ContinueWith(async _ =>
            {
                try
                {
                    await VelopackUpdateService.Instance.StartSilentUpdateCheckAndDownloadAsync();
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"App silent update worker error: {ex.Message}");
                }
            }, TaskScheduler.Default);

            // 启动 8 秒后在后台静默发起自动备份检测
            _ = Task.Delay(8000).ContinueWith(_ =>
            {
                try
                {
                    BackupService.RunAutoBackupIfNeeded();
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"App auto backup worker error: {ex.Message}");
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"MainWindow startup crash: {ex.ToString()}");
            throw;
        }
    }

    private static bool ShouldStartHidden(string[] args)
    {
        return args.Any(arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-tray", StringComparison.OrdinalIgnoreCase));
    }

    private static void TrySetProcessDpiAwareness()
    {
        try
        {
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
            SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch
        {
            // The manifest is the primary DPI declaration; this is a startup-time fallback.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static void Window_GlobalLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        UpdateWindowDwmTheme(window);

        // 延迟异步再次更新，防止在窗口首次呈现时，DWM 设置被操作系统的默认绘制所覆盖
        window.Dispatcher.BeginInvoke(new Action(() => UpdateWindowDwmTheme(window)), System.Windows.Threading.DispatcherPriority.Background);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr", CharSet = CharSet.Auto)]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLong", CharSet = CharSet.Auto)]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetClassLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return SetClassLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_ERASEBKGND = 0x0014;
    private const int GCLP_HBRBACKGROUND = -10;
    private const int WHITE_BRUSH = 0;
    private const int BLACK_BRUSH = 4;

    internal static void UpdateWindowDwmTheme(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            HostAssets.AppendLog($"UpdateWindowDwmTheme: Handle is Zero for {window.GetType().Name}. Skipping.");
            return;
        }

        bool useLightTheme = false;
        if (string.Equals(_currentThemeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            useLightTheme = IsSystemLightTheme();
        }
        else if (string.Equals(_currentThemeMode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            useLightTheme = true;
        }

        var useDarkMode = useLightTheme ? 0 : 1;
        HostAssets.AppendLog($"UpdateWindowDwmTheme: Applying DarkMode={useDarkMode} to {window.GetType().Name} (Handle: {handle}).");
        
        // 修改 WPF 窗口类的背景画刷，防止 WPF DirectX 渲染首帧前的瞬间闪烁白底或黑底
        var hBrush = GetStockObject(useDarkMode == 1 ? BLACK_BRUSH : WHITE_BRUSH);
        SetClassLong(handle, GCLP_HBRBACKGROUND, hBrush);

        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        
        // Force the OS to redraw the non-client area immediately
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        
        // 额外发送 WM_NCACTIVATE 消息，强制非客户区（标题栏）立刻重绘，解决主题切换时标题栏变色不瞬间的问题
        SendMessage(handle, WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);
        SendMessage(handle, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
    }

    private static void UpdateAllWindowDwmThemes()
    {
        if (Current == null) return;
        foreach (Window window in Current.Windows)
        {
            UpdateWindowDwmTheme(window);
        }

        // Force Tray Context Menu to update its dynamic resources by toggling its style
        if (Current.TryFindResource("TrayContextMenu") is System.Windows.Controls.ContextMenu menu)
        {
            var currentStyle = menu.Style;
            menu.Style = null;
            menu.Style = currentStyle;
        }
    }

    private static void TryRegisterUriProtocol()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                UriProtocolRegistrationService.EnsureRegistered(executablePath);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Protocol registration skipped: {ex.Message}");
        }
    }

    private static async Task HandleLaunchArgumentsAsync(MainWindow window, string[] args)
    {
        var protocolArgument = args.FirstOrDefault(static arg =>
            arg.StartsWith("yanzi://", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(protocolArgument))
        {
            return;
        }

        try
        {
            window.ShowPanel();
            await window.HandleProtocolLaunchAsync(protocolArgument);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Protocol launch failed: {ex}");
        }
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        try
        {
            var terminatedCount = RunningExtensionRegistry.TerminateAll();
            if (terminatedCount > 0)
            {
                HostAssets.AppendLog($"App shutdown terminated running extensions: count={terminatedCount}");
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"App shutdown terminate running extensions failed: {ex.Message}");
        }

        try
        {
            EverythingRuntimeService.StopOwnedRuntime();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"App shutdown stop Everything failed: {ex.Message}");
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_agentApiServer != null)
        {
            _agentApiServer.Dispose();
            _agentApiServer = null;
        }

        if (_lanDiscoveryService != null)
        {
            _lanDiscoveryService.Dispose();
            _lanDiscoveryService = null;
        }

        if (_singleInstanceService != null)
        {
            _singleInstanceService.Dispose();
            _singleInstanceService = null;
        }

        base.OnExit(e);
    }



    private static async Task HandleSecondaryLaunchMessageAsync(MainWindow window, string message)
    {
        await window.Dispatcher.InvokeAsync(async () =>
        {
            window.ShowPanel();
            if (!string.Equals(message, "__show__", StringComparison.Ordinal))
            {
                await window.HandleProtocolLaunchAsync(message);
            }
        }).Task.Unwrap();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        HostAssets.AppendLog($"DispatcherUnhandledException: {e.Exception}");
        
        if (!_isAppFullyInitialized)
        {
            // 如果在启动初始化阶段发生任何致命异常，我们绝对不能吞掉异常并任由其变成后台无窗口常驻僵尸进程，必须立刻退出
            try
            {
                System.Windows.MessageBox.Show(
                    $"燕子启动失败。\n\n错误原因: {e.Exception.Message}\n\n详细异常堆栈已记录至日志中，您可以通过查看以下文件进行排查：\n{HostAssets.HostLogPath}",
                    "燕子 - 启动致命错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch
            {
                // 忽略弹出本身的二次崩溃
            }
            finally
            {
                System.Environment.Exit(1);
            }
        }
        else
        {
            // 只有当程序已经成功初始化并在运行状态时，我们才采取柔性容错机制，将异常标记为 Handled，以防程序闪退影响用户体验
            e.Handled = true;
        }
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        HostAssets.AppendLog($"AppDomainUnhandledException: {e.ExceptionObject}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HostAssets.AppendLog($"UnobservedTaskException: {e.Exception}");
    }

    private void StartLocalAgentApi(MainWindow window, AppSettings settings)
    {
        if (!settings.EnableAgentApi)
        {
            return;
        }

        try
        {
            var prefix = settings.EnableLanSync
                ? $"http://*:{settings.AgentApiPort}/"
                : $"http://127.0.0.1:{settings.AgentApiPort}/";
            _agentApiServer = new LocalAgentApiServer(
                prefix,
                settings.AgentApiToken,
                 (extensionId) =>
                 {
                     window.Dispatcher.Invoke(() =>
                     {
                         if (!string.IsNullOrEmpty(extensionId))
                         {
                             window.TrackRecentlyAddedExtension(extensionId);
                         }
                         window.ReloadLocalExtensionsFromExternal();
                         if (_settingsWindow != null && _settingsWindow.IsLoaded)
                         {
                             _settingsWindow.RefreshExtensionsFromExternal();
                         }
                     });
                 },
                () =>
                {
                    window.Dispatcher.Invoke(() => window.QueueBackgroundWebDavSync("api-trigger", forceImmediate: true));
                },
                (id) =>
                {
                    var tcs = new TaskCompletionSource<(bool ok, string message)>();
                    window.Dispatcher.Invoke(async () => tcs.SetResult(await window.PublishExtensionFromSettingsAsync(id)));
                    return tcs.Task;
                },
                (id) =>
                {
                    var tcs = new TaskCompletionSource<(bool ok, string message)>();
                    window.Dispatcher.Invoke(async () => tcs.SetResult(await window.UnpublishExtensionFromSettingsAsync(id)));
                    return tcs.Task;
                },
                (id) =>
                {
                    var tcs = new TaskCompletionSource<(bool ok, string message)>();
                    window.Dispatcher.Invoke(async () => tcs.SetResult(await window.InstallStoreExtensionAsync(id)));
                    return tcs.Task;
                },
                () =>
                {
                    var tcs = new TaskCompletionSource<OpenQuickHost.Sync.AuthMeResponse?>();
                    window.Dispatcher.Invoke(async () =>
                    {
                        var client = window.CloudSyncClient;
                        if (client == null) tcs.SetResult(null);
                        else tcs.SetResult(await client.GetMeAsync());
                    });
                    return tcs.Task;
                },
                (title, message) =>
                {
                    window.Dispatcher.Invoke(() => System.Windows.MessageBox.Show(message, title));
                    return Task.CompletedTask;
                },
                async (title, message) =>
                {
                    var sentByLan = false;
                    var mobileIp = LanDiscoveryService.LastKnownMobileIp;
                    if (mobileIp != null)
                    {
                        try
                        {
                            using var client = new System.Net.Http.HttpClient();
                            client.Timeout = TimeSpan.FromSeconds(3);
                            var payload = System.Text.Json.JsonSerializer.Serialize(new { title, message });
                            var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                            using var response = await client.PostAsync($"http://{mobileIp}:42981/", content);
                            response.EnsureSuccessStatusCode();
                            sentByLan = true;
                            HostAssets.AppendLog($"Push to mobile delivered by LAN: ip={mobileIp}, title={title}.");
                        }
                        catch (Exception ex)
                        {
                            HostAssets.AppendLog($"Push to mobile LAN failed: ip={mobileIp}, {ex.Message}");
                        }
                    }

                    if (!sentByLan)
                    {
                        try
                        {
                            var cloudClient = window.CloudSyncClient;
                            if (cloudClient == null || !cloudClient.HasCredential)
                            {
                                HostAssets.AppendLog("Push to mobile cloud fallback skipped: cloud client has no credential.");
                                return;
                            }

                            var desktopDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
                            await cloudClient.RegisterDeviceAsync(
                                desktopDeviceId,
                                "desktop",
                                Environment.MachineName,
                                capabilities: new { receiveMobileMessages = true, pushToMobile = true });
                            var messageId = await cloudClient.SendDeviceMessageAsync(
                                desktopDeviceId,
                                "android",
                                "notify",
                                title,
                                message,
                                payload: new
                                {
                                    source = "desktop",
                                    sourceDeviceName = Environment.MachineName,
                                    createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                });
                            HostAssets.AppendLog($"Push to mobile queued by cloud: messageId={messageId}, title={title}.");
                        }
                        catch (Exception ex)
                        {
                            HostAssets.AppendLog($"Push to mobile cloud fallback failed: {ex.Message}");
                        }
                    }
                },
                (message) =>
                {
                    var tcs = new TaskCompletionSource<(bool success, string output)>();
                    window.Dispatcher.Invoke(async () =>
                    {
                        try
                        {
                            var result = await window.HandleMobileDeviceMessageAsync(message);
                            tcs.SetResult((result.success, result.output));
                        }
                        catch (Exception ex)
                        {
                            tcs.SetResult((false, ex.Message));
                        }
                    });
                    return tcs.Task;
                },
                (reason, refreshYanmOverlay) =>
                {
                    window.Dispatcher.Invoke(() => window.NotifyQuickPanelSettingsChanged(reason, refreshYanmOverlay));
                });
            _agentApiServer.Start();

            if (settings.EnableLanSync)
            {
                _lanDiscoveryService = new LanDiscoveryService(settings.AgentApiPort, settings.AgentApiToken);
                _lanDiscoveryService.Start();
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Local Agent API failed to start: {ex.Message}");
        }
    }

    private Forms.NotifyIcon BuildNotifyIcon(MainWindow window)
    {
        var notifyIcon = new Forms.NotifyIcon
        {
            Text = "燕子",
            Visible = true
        };

        notifyIcon.Icon = TryCreateNotifyIcon() ?? SystemIcons.Application;
        
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                window.ShowMousePanel();
            }
        };

        notifyIcon.DoubleClick += (_, _) =>
        {
            ToggleListenerServices();
            window.HideMousePanel();
        };
        
        // 右键弹出 WPF ContextMenu
        notifyIcon.MouseUp += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                _lastTrayForegroundProcess = YarnSelectService.GetForegroundProcessName();
                if (WpfApplication.Current.TryFindResource("TrayContextMenu") is System.Windows.Controls.ContextMenu menu)
                {
                    UpdateTrayMenuState(menu);
                    menu.IsOpen = true;
                    // 激活窗口以确保菜单失去焦点时能自动关闭
                    window.Activate();
                }
            }
        };

        return notifyIcon;
    }



    public void ShowDesktopNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (_notifyIcon == null)
        {
            return;
        }

        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ShowDesktopNotification failed: {ex.Message}");
        }
    }

    // 托盘菜单事件处理器
    private void TrayShow_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.ShowPanel();
    }

    private void TrayAddGlobalBlacklist_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        var initialList = settings.GlobalServiceBlacklistedProcesses ?? new List<string>();
        
        var defaultProcess = _lastTrayForegroundProcess;
        var inputWindow = new ProcessPickerWindow("全局黑名单", "请选择要加入全局服务黑名单的进程：", defaultProcess, initialList);
        if (inputWindow.ShowDialog() == true)
        {
            settings.GlobalServiceBlacklistedProcesses = inputWindow.Blacklist.Select(b => b.ProcessName).ToList();
            foreach (var b in inputWindow.Blacklist)
            {
                if (!string.IsNullOrWhiteSpace(b.ExecutablePath))
                {
                    settings.ProcessExecutablePaths[b.ProcessName] = b.ExecutablePath;
                }
            }
            AppSettingsStore.Save(settings);

            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.RefreshAppSettings();
            }

            CheckForegroundBlacklist();

            ShowDesktopNotification("全局黑名单", $"全局服务黑名单已更新。");
        }
    }

    private void TrayMousePanel_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.ShowMousePanel();
    }

    private void TrayToggleMousePanelService_Click(object sender, RoutedEventArgs e)
    {
        ToggleListenerServices();
    }

    private void TrayHide_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.HideToTray();
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        CurrentApp?.OpenSettingsWindow();
    }

    private void TrayRunningExtensions_Click(object sender, RoutedEventArgs e)
    {
        CurrentApp?.OpenRunningExtensionsWindow();
    }

    private void TrayInputState_Click(object sender, RoutedEventArgs e)
    {
        if (_inputStateWindow is { IsVisible: true })
        {
            _inputStateWindow.Activate();
            _inputStateWindow.RefreshState();
            return;
        }

        _inputStateWindow = new InputStateWindow();
        _inputStateWindow.Closed += (_, _) => _inputStateWindow = null;
        _inputStateWindow.Show();
    }

    private void TrayResetInputState_Click(object sender, RoutedEventArgs e)
    {
        KeyboardDoubleTapService.ResetStuckKeyboardState();
        InputHookService.ResetMouseState();
        YarnSelectService.ResetMouseState();
        _inputStateWindow?.RefreshState();
        ShowDesktopNotification(
            "输入状态已重置",
            "已清理可能卡住的键盘修饰键，以及鼠标面板、燕环、燕幕和燕选的临时鼠标状态。",
            Forms.ToolTipIcon.Info);
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow is MainWindow mw)
        {
            mw.AllowClose = true;
            Shutdown();
        }
    }

    private void TrayMobileInbox_Click(object sender, RoutedEventArgs e)
    {
        (MainWindow as MainWindow)?.ShowMobileInboxWindow();
    }

    private void ToggleListenerServices()
    {
        if (MainWindow is not MainWindow mainWindow || _notifyIcon == null)
        {
            return;
        }

        _listenerServicesPaused = !_listenerServicesPaused;
        ApplyServicePauseState();
    }

    private void ApplyServicePauseState()
    {
        if (MainWindow is not MainWindow mainWindow || _notifyIcon == null)
            return;

        var shouldPause = _listenerServicesPaused || _isAutoPausedByBlacklist;

        if (shouldPause)
        {
            mainWindow.PauseListenerServices();
            
            if (_listenerServicesPaused)
            {
                _notifyIcon.Icon = TryCreateDisabledNotifyIcon() ?? SystemIcons.Application;
                _notifyIcon.Text = "燕子 - 服务已暂停";
            }
            else
            {
                _notifyIcon.Icon = TryCreateDisabledNotifyIcon() ?? SystemIcons.Application;
                _notifyIcon.Text = "燕子 - 自动暂停 (黑名单)";
            }
            HostAssets.AppendLog($"Tray: listener services paused (Manual: {_listenerServicesPaused}, Auto: {_isAutoPausedByBlacklist}).");
        }
        else
        {
            mainWindow.ResumeListenerServices();
            _notifyIcon.Icon = TryCreateNotifyIcon() ?? SystemIcons.Application;
            _notifyIcon.Text = "燕子";
            HostAssets.AppendLog("Tray: listener services resumed.");
        }
    }

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    public void CheckForegroundBlacklist()
    {
        if (_listenerServicesPaused)
            return; // User manually paused, no need to auto-pause/resume logic

        try
        {
            var currentProcess = YarnSelectService.GetForegroundProcessName();
            if (string.IsNullOrWhiteSpace(currentProcess))
                return;

            var settings = AppSettingsStore.Load();
            var blacklist = settings.GlobalServiceBlacklistedProcesses ?? new List<string>();

            bool isInBlacklist = blacklist.Any(p => ProcessHelper.ProcessNameMatches(currentProcess, p));

            if (isInBlacklist && !_isAutoPausedByBlacklist)
            {
                _isAutoPausedByBlacklist = true;
                ApplyServicePauseState();
            }
            else if (!isInBlacklist && _isAutoPausedByBlacklist)
            {
                _isAutoPausedByBlacklist = false;
                ApplyServicePauseState();
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    private void StartForegroundMonitorTimer()
    {
        // 1. 启动时立即检测一次当前前台进程
        CheckForegroundBlacklist();

        // 2. 注册 Windows 系统级 EVENT_SYSTEM_FOREGROUND 事件钩子（实现真正的事件驱动，0ms 延迟，0 CPU 轮询损耗）
        _winEventDelegate = (hHook, eventType, hwnd, idObject, idChild, dwThread, dwTime) =>
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                Dispatcher.BeginInvoke(new Action(CheckForegroundBlacklist));
            }
        };

        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void UpdateTrayMenuState(System.Windows.Controls.ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            if (Equals(item.Tag, "show-searchbox"))
            {
                item.Visibility = MainWindow != null && MainWindow.IsVisible ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }
            else if (Equals(item.Tag, "mobile-chat"))
            {
                item.Visibility = System.Windows.Visibility.Visible;
            }
            else if (Equals(item.Tag, "service-toggle"))
            {
                item.Header = _listenerServicesPaused ? "恢复全部服务" : "暂停全部服务";
            }
            else if (Equals(item.Tag, "mouse-panel"))
            {
                item.IsEnabled = !_listenerServicesPaused;
            }
            else if (Equals(item.Tag, "running-extensions"))
            {
                item.Header = $"正在运行的扩展 ({RunningExtensionRegistry.GetRunningCount()})";
            }
        }
    }

    private static Icon? TryCreateNotifyIcon()
    {
        try
        {
            var resource = WpfApplication.GetResourceStream(new Uri("yanzi.ico", UriKind.Relative));
            if (resource == null)
            {
                return null;
            }

            using var icon = new Icon(resource.Stream);
            return (Icon)icon.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static Icon? TryCreateDisabledNotifyIcon()
    {
        try
        {
            using var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Transparent);
                using var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 96, 96, 96));
                using var border = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 150, 150, 150), 2);
                g.FillEllipse(fill, 4, 4, 24, 24);
                g.DrawEllipse(border, 4, 4, 24, 24);
                using var slash = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 220, 220, 220), 3);
                g.DrawLine(slash, 10, 22, 22, 10);
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }
        catch
        {
            return null;
        }
    }

    public static void EnableSilentLoading(Window window)
    {
        var startupLocation = window.WindowStartupLocation;
        var originalWidth = window.Width;
        var originalHeight = window.Height;
        var originalSizeToContent = window.SizeToContent;
        var originalShowInTaskbar = window.ShowInTaskbar;
        var originalResizeMode = window.ResizeMode;
        var originalAllowsTransparency = window.AllowsTransparency;
        var isFirstRender = true;

        window.ShowInTaskbar = false;
        window.ResizeMode = ResizeMode.NoResize;
        window.Background = new SolidColorBrush(WpfColor.FromRgb(17, 17, 17));

        void RevealWindow()
        {
            if (!isFirstRender)
            {
                return;
            }

            isFirstRender = false;

            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                int disableTransitions = 1;
                DwmSetWindowAttribute(handle, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref disableTransitions, sizeof(int));
            }

            window.SizeToContent = originalSizeToContent;
            if (!double.IsNaN(originalWidth)) window.Width = originalWidth;
            if (!double.IsNaN(originalHeight)) window.Height = originalHeight;

            if (startupLocation == WindowStartupLocation.CenterOwner && window.Owner != null)
            {
                window.Left = window.Owner.Left + (window.Owner.Width - window.Width) / 2;
                window.Top = window.Owner.Top + (window.Owner.Height - window.Height) / 2;
            }
            else if (startupLocation == WindowStartupLocation.CenterScreen)
            {
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                window.Left = (screenWidth - window.Width) / 2;
                window.Top = (screenHeight - window.Height) / 2;
            }

            window.ShowInTaskbar = originalShowInTaskbar;
            window.ResizeMode = originalResizeMode;
            window.AllowsTransparency = originalAllowsTransparency;
            window.Opacity = 1;
            window.Activate();
            window.Focus();

            if (handle != IntPtr.Zero)
            {
                int disableTransitions = 0;
                DwmSetWindowAttribute(handle, 3, ref disableTransitions, sizeof(int));
            }
        }

        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke((Action)RevealWindow, DispatcherPriority.Loaded);
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke((Action)RevealWindow, DispatcherPriority.Render);
        window.Dispatcher.BeginInvoke((Action)RevealWindow, DispatcherPriority.Background);
    }

    public new static App? Current => System.Windows.Application.Current as App;

    private static App? CurrentApp => Current as App;

    public void OpenSettingsWindow(string? sectionKey = null)
    {
        if (MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        try
        {
            HostAssets.AppendLog($"Settings window open requested: section={sectionKey ?? "default"}, existing={_settingsWindow != null && _settingsWindow.IsLoaded}, visible={_settingsWindow?.IsVisible ?? false}, opacity={_settingsWindow?.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}.");
            if (_settingsWindow == null)
            {
                HostAssets.AppendLog("Settings window not cached, creating new instance.");
                _settingsWindow = new SettingsWindow(mainWindow);
                HostAssets.AppendLog("Settings window created.");
            }
            else if (!_settingsWindow.IsLoaded)
            {
                HostAssets.AppendLog($"Settings window exists but not loaded yet. visibility={_settingsWindow.Visibility}, opacity={_settingsWindow.Opacity}, windowState={_settingsWindow.WindowState}.");
            }

            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                HostAssets.AppendLog("Settings window was minimized, restoring to normal.");
                _settingsWindow.WindowState = WindowState.Normal;
            }

            if (!_settingsWindow.IsVisible)
            {
                HostAssets.AppendLog($"Settings window is not visible before Show(). opacity={_settingsWindow.Opacity}, visibility={_settingsWindow.Visibility}.");
                _settingsWindow.Show();
                HostAssets.AppendLog($"Settings window shown. opacity={_settingsWindow.Opacity}, visibility={_settingsWindow.Visibility}.");
            }

            _settingsWindow.NavigateTo(sectionKey);
            _settingsWindow.Activate();
            _settingsWindow.Focus();
            HostAssets.AppendLog($"Settings window activated. opacity={_settingsWindow.Opacity}, active={_settingsWindow.IsActive}.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Settings window open failed: {ex}");
        }
    }

    public void ReloadSettingsWindowIfOpen()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_settingsWindow != null && _settingsWindow.IsLoaded)
                {
                    _settingsWindow.ReloadSettingsFromDisk();
                    HostAssets.AppendLog("Settings window settings reloaded from disk due to external sync.");
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Error reloading settings window: {ex.Message}");
            }
        });
    }

    public void OpenRunningExtensionsWindow()
    {
        if (_runningExtensionsWindow == null || !_runningExtensionsWindow.IsLoaded)
        {
            _runningExtensionsWindow = new RunningExtensionsWindow();
            _runningExtensionsWindow.Closed += (_, _) => _runningExtensionsWindow = null;
        }

        if (!_runningExtensionsWindow.IsVisible)
        {
            _runningExtensionsWindow.Show();
        }

        if (_runningExtensionsWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            _runningExtensionsWindow.WindowState = System.Windows.WindowState.Normal;
        }

        _runningExtensionsWindow.Activate();
        _runningExtensionsWindow.Focus();
    }

    private static string _currentThemeMode = "Dark";

    public static void ApplyTheme(string themeMode)
    {
        _currentThemeMode = themeMode;
        
        bool useLightTheme = false;
        if (string.Equals(themeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            useLightTheme = IsSystemLightTheme();
        }
        else if (string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            useLightTheme = true;
        }

        string themeUri = useLightTheme 
            ? "/Themes/LightTheme.xaml"
            : "/Themes/DarkTheme.xaml";

        var mergedDicts = Current?.Resources?.MergedDictionaries;
        if (mergedDicts == null)
        {
            return;
        }
        
        var existingThemeDict = mergedDicts.FirstOrDefault(static d => 
            d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));

        if (existingThemeDict != null)
        {
            if (existingThemeDict.Source.OriginalString == themeUri)
                return;

            mergedDicts.Remove(existingThemeDict);
        }

        mergedDicts.Insert(0, new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) });
        UpdateAllWindowDwmThemes();
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            if (string.Equals(_currentThemeMode, "System", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ApplyTheme("System"));
            }
        }
    }
}
