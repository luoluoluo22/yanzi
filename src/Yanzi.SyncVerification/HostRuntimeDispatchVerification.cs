using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OpenQuickHost;
using OpenQuickHost.Sync;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

internal static class HostRuntimeDispatchVerification
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Host runtime QA failed: " + name);
        Console.WriteLine("PASS " + name);
    }

    internal static async Task RunAsync()
    {
        // Read-only real Win32 sampling; no network/device configuration is changed.
        var execution = new LocalExtensionHostExecutionManifest
        {
            Conditions = new LocalExtensionHostConditionsManifest
            {
                MaxCpuPercent = 100,
                MinIdleMinutes = 0,
                RequireOnline = true,
                RequireAcPower = true
            }
        };
        Check(HostWebViewEventBridge.IsBackgroundLaunchSource("idle") &&
            HostWebViewEventBridge.IsBackgroundLaunchSource("scheduler") &&
            HostWebViewEventBridge.IsBackgroundLaunchSource("network.online") &&
            !HostWebViewEventBridge.IsBackgroundLaunchSource("launcher"),
            "background launch sources include idle and Cron without affecting manual launch");
        Check(HostWebViewEventBridge.EventNameForLaunchSource("idle") == "host.idle" &&
            HostWebViewEventBridge.EventNameForLaunchSource("scheduler") == "host.schedule",
            "legacy idle/Cron launch sources map to host events");
        var before = HostResourceSampler.Read(execution);
        await Task.Delay(1400);
        var after = HostResourceSampler.Read(execution);
        Check(after.CpuPercent is >= 0 and <= 100, "real Windows CPU delta is valid");
        Check(after.IdleMinutes is >= 0 and <= 100000, "real Windows idle sampling is valid");
        Check(after.AcPower.HasValue && after.Online.HasValue, "real power and network interfaces were sampled");
        Console.WriteLine($"HOST_RUNTIME_SAMPLING ac={after.AcPower} network={after.Online} cpu={after.CpuPercent:F1} idleMinutes={after.IdleMinutes:F2}");

        var root = Path.Combine(Path.GetTempPath(), "yanzi-host-webview-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var web = new WebView2();
                var window = new Window
                {
                    Content = web, ShowInTaskbar = false, ShowActivated = false,
                    WindowStyle = WindowStyle.None, Width = 320, Height = 240,
                    Left = -12000, Top = -12000, Title = "Yanzi isolated host event QA"
                };
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        await web.EnsureCoreWebView2Async(
                            await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "webview2-profile")));
                        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        web.CoreWebView2.NavigationCompleted += (_, e) => navDone.TrySetResult(e.IsSuccess);
                        web.CoreWebView2.NavigateToString("""
                            <!doctype html><meta charset="utf-8">
                            <script>
                            window.yanzi = {
                              onHostEvent: async detail => {
                                await new Promise(resolve => setTimeout(resolve, 40));
                                window.received = detail.eventName;
                              },
                              beforeExit: async () => {
                                await new Promise(resolve => setTimeout(resolve, 65));
                                window.savedCount = (window.savedCount || 0) + 1;
                                return true;
                              }
                            };
                            window.addEventListener('yanzi:host-event', e => {
                              window.domReceived = e.detail.eventName;
                            });
                            </script>
                            """);
                        Check(await navDone.Task.WaitAsync(TimeSpan.FromSeconds(15)), "isolated WebView2 navigated");
                        Check(await HostWebViewEventBridge.NotifyAsync(web.CoreWebView2, "host.resume"),
                            "host.resume delivery acknowledged by real WebView2");
                        var notified = await web.CoreWebView2.ExecuteScriptAsync(
                            "window.received==='host.resume' && window.domReceived==='host.resume'");
                        Check(notified == "true", "both DOM listener and async host handler observed event");

                        Check(await HostWebViewEventBridge.PrepareForExitAsync(web.CoreWebView2, TimeSpan.FromSeconds(2)),
                            "WebView2 asynchronous exit save resolved");
                        Check(await web.CoreWebView2.ExecuteScriptAsync("window.savedCount") == "1",
                            "saved checkpoint exists after acknowledgement");

                        await web.CoreWebView2.ExecuteScriptAsync(
                            "window.yanzi.beforeExit=async()=>false");
                        Check(!await HostWebViewEventBridge.PrepareForExitAsync(web.CoreWebView2, TimeSpan.FromSeconds(2)),
                            "explicit false is not a save acknowledgement");

                        await web.CoreWebView2.ExecuteScriptAsync(
                            "delete window.yanzi.beforeExit");
                        Check(!await HostWebViewEventBridge.PrepareForExitAsync(web.CoreWebView2, TimeSpan.FromSeconds(2)),
                            "missing handler fails closed");

                        await web.CoreWebView2.ExecuteScriptAsync(
                            "window.yanzi.beforeExit=async()=>{throw new Error('intentional fixture rejection')}");
                        Check(!await HostWebViewEventBridge.PrepareForExitAsync(web.CoreWebView2, TimeSpan.FromSeconds(2)),
                            "rejected Promise fails closed");

                        await web.CoreWebView2.ExecuteScriptAsync(
                            "window.yanzi.beforeExit=()=>new Promise(()=>{})");
                        try
                        {
                            await HostWebViewEventBridge.PrepareForExitAsync(
                                web.CoreWebView2, TimeSpan.FromMilliseconds(250));
                            throw new InvalidOperationException("Unresolved promise wrongly acknowledged");
                        }
                        catch (TimeoutException)
                        {
                            Console.WriteLine("PASS unresolved save Promise is bounded");
                        }
                        finished.TrySetResult();
                    }
                    catch (Exception ex) { finished.TrySetException(ex); }
                    finally
                    {
                        web.Dispose();
                        window.Close();
                        app.Shutdown();
                    }
                };
                app.Run(window);
            }
            catch (Exception ex) { finished.TrySetException(ex); }
        })
        {
            Name = "YanziHostWebViewQaSTA",
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(55));
            Check(thread.Join(TimeSpan.FromSeconds(5)), "isolated WPF/WebView2 fixture terminated");
            Console.WriteLine("HOST_RUNTIME_WEBVIEW_QA_PASS");
        }
        finally
        {
            // WebView2 browser subprocesses may release their profile handles
            // shortly after the isolated WPF thread terminates.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            for (var attempt = 0; attempt < 12 && Directory.Exists(root); attempt++)
            {
                try { Directory.Delete(root, recursive: true); }
                catch (IOException) { await Task.Delay(300); }
                catch (UnauthorizedAccessException) { await Task.Delay(300); }
            }
            if (Directory.Exists(root)) Console.WriteLine("HOST_RUNTIME_PROFILE_CLEANUP_DEFERRED");
            else Console.WriteLine("PASS isolated WebView2 profile removed");
        }
    }
}
