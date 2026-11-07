using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class ScheduleSchedulerE2E
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    public static int Run(App app)
    {
        string root = Path.Combine(Path.GetTempPath(), "yanzi-pr180-schedule-e2e-" + Guid.NewGuid().ToString("N"));
        string id = "qa-schedule-isolated-" + Guid.NewGuid().ToString("N");
        string marker = Path.Combine(root, "actual-scheduler-dispatch-marker.txt");
        MainWindow? host = null;
        ExtensionSchedulerService? scheduler = null;
        IDisposable? scope = null;
        string stage = "start";
        DateTime startedAt = DateTime.Now;
        try
        {
            if (!HostRuntimeProfile.IsDevelopment)
                throw new InvalidOperationException("Must run using --dev to disable production global listeners");
            scope = (IDisposable)typeof(HostAssets).GetMethod(
                "UseIsolatedDataRootForVerification",
                BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { root })!;
            Directory.CreateDirectory(root);
            string script = "param([string]$InputText = '', [string]$ContextPath = '')\n" +
                "Add-Content -LiteralPath '" + marker.Replace("'", "''") +
                "' -Value ([DateTimeOffset]::UtcNow.ToString('O'))\n" +
                "Write-Output 'ISOLATED_SCHEDULE_PROBE_EXECUTED'\n";
            var manifest = new
            {
                id,
                name = "Isolated Scheduler QA",
                version = "0.0.1",
                category = "QA",
                description = "Synthetic fixture restricted to an isolated verification data root",
                runtime = "powershell",
                entryMode = "inline",
                script = new { source = script }
            };
            stage = "save synthetic manifest";
            var cmd = LocalExtensionCatalog.SaveJsonExtension(JsonSerializer.Serialize(manifest));
            Assert(cmd.ExtensionId == id, "synthetic extension ID mismatch");
            Console.WriteLine("ISOLATED_ROOT=" + root);
            Console.WriteLine("ISOLATED_EXTENSION=" + id);

            // The test executable runs next to host dependencies but its own .deps.json
            // does not enumerate transitive packages used only by native MainWindow XAML.
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var filename = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                return File.Exists(filename) ? ctx.LoadFromAssemblyPath(filename) : null;
            };
            stage = "construct native host";
            host = new MainWindow
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Opacity = 0.01,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2000,
                Top = -2000,
                AllowClose = true
            };
            host.Show();
            Assert(HostAssets.RootPath == root, "host escaped isolated data root");
            Assert(host.GetAllCommands().Any(x => x.ExtensionId == id), "fixture absent from real host command catalog");

            var nextMinute = DateTime.Now.AddMinutes(1);
            string expectedCron = nextMinute.Minute + " " + nextMinute.Hour + " * * *";

            stage = "show schedule dialog and confirm";
            var dialog = new ScheduleConfigWindow("") { Owner = host, ShowActivated = false };
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                ((CheckBox)dialog.FindName("EnableToggle")!).IsChecked = true;
                var cronChoice = (ScheduleChoiceBorder)dialog.FindName("ModeCronBtn")!;
                cronChoice.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = cronChoice
                });
                ((TextBox)dialog.FindName("CronBox")!).Text = expectedCron;
                var confirm = VisualButtons(dialog).Single(b => Equals(b.Content, "确认"));
                confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, confirm));
            }));
            Assert(dialog.ShowDialog() == true && dialog.ResultSchedule == expectedCron,
                "schedule dialog did not confirm next-minute cron");

            stage = "commit schedule through real host settings API";
            var changed = host.UpdateExtensionStartupFromSettingsAsync(id, null, dialog.ResultSchedule).GetAwaiter().GetResult();
            Assert(changed.ok && changed.updated?.Startup?.Schedule == expectedCron,
                "host settings API did not commit schedule");
            string manifestPath = Path.Combine(HostAssets.ExtensionsPath, id, "manifest.json");
            string readback = LocalExtensionCatalog.LoadManifestJson(id);
            Assert(readback.Contains(expectedCron, StringComparison.Ordinal), "real catalog manifest readback failed");
            Console.WriteLine("MANIFEST_SAVED_CRON=" + expectedCron);

            stage = "cancel a second dialog and check persisted schedule";
            var cancel = new ScheduleConfigWindow(expectedCron) { Owner = host, ShowActivated = false };
            cancel.Loaded += (_, _) => cancel.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                ((CheckBox)cancel.FindName("EnableToggle")!).IsChecked = false;
                var button = VisualButtons(cancel).Single(b => Equals(b.Content, "取消"));
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            }));
            Assert(cancel.ShowDialog() == false, "cancel did not return false");
            Assert(cancel.ResultSchedule == "", "cancel should not return edited cron");
            Assert(LocalExtensionCatalog.LoadManifestJson(id).Contains(expectedCron, StringComparison.Ordinal),
                "cancel unexpectedly modified on-disk schedule");
            Console.WriteLine("CANCEL_PRESERVED_MANIFEST=TRUE");

            stage = "start real scheduler dispatcher timer in isolated harness";
            scheduler = new ExtensionSchedulerService(host);
            // --dev disables all production background listeners. Only this isolated
            // timer is enabled; the original production scheduler implementation
            // and host execution dispatcher are used without mocks.
            typeof(ExtensionSchedulerService).GetField("_started", PrivateInstance)!.SetValue(scheduler, true);
            typeof(MainWindow).GetField("_extensionScheduler", PrivateInstance)!.SetValue(host, scheduler);
            scheduler.RefreshSchedules();
            var nextRuns = scheduler.NextRunsSnapshot();
            Assert(nextRuns.TryGetValue(id, out var due), "real scheduler failed to parse persisted cron");
            Assert(due > DateTime.Now && (due - DateTime.Now) <= TimeSpan.FromMinutes(2),
                "scheduler next-run timestamp is outside expected next-minute window");
            Console.WriteLine("SCHEDULED_AT=" + due.ToString("O"));
            var timer = (DispatcherTimer)typeof(ExtensionSchedulerService).GetField("_checkTimer", PrivateInstance)!.GetValue(scheduler)!;
            timer.Interval = TimeSpan.FromMilliseconds(350);
            timer.Start();

            stage = "await actual cron minute and real scheduled extension dispatch";
            var frame = new DispatcherFrame();
            var watcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            DateTime deadline = DateTime.Now.AddSeconds(100);
            watcher.Tick += (_, _) =>
            {
                if (File.Exists(marker) || DateTime.Now >= deadline)
                {
                    watcher.Stop();
                    frame.Continue = false;
                }
            };
            watcher.Start();
            Dispatcher.PushFrame(frame);
            Assert(File.Exists(marker), "scheduled PowerShell extension did not execute before deadline");
            var lines = File.ReadAllLines(marker);
            Assert(lines.Length == 1, "expected exactly one scheduled execution, got " + lines.Length);
            Assert(DateTimeOffset.TryParse(lines[0], out var dispatchedUtc), "synthetic execution timestamp absent");
            Assert(dispatchedUtc.LocalDateTime >= due && dispatchedUtc.LocalDateTime <= DateTime.Now.AddSeconds(3),
                "scheduled execution timestamp does not match due time");
            Console.WriteLine("REAL_SCHEDULER_DISPATCH_MARKER=" + marker);
            Console.WriteLine("ACTUAL_DISPATCH_UTC=" + dispatchedUtc.ToString("O"));

            stage = "disable schedule and check it is removed from scheduler";
            var disabled = host.UpdateExtensionStartupFromSettingsAsync(id, null, null).GetAwaiter().GetResult();
            Assert(disabled.ok && !scheduler.NextRunsSnapshot().ContainsKey(id),
                "disable operation failed to remove scheduled trigger");
            Assert(!LocalExtensionCatalog.LoadManifestJson(id).Contains(expectedCron, StringComparison.Ordinal),
                "disabled schedule remains in manifest");
            Console.WriteLine("DISABLE_REMOVED_SCHEDULE=TRUE");
            var json = JsonSerializer.Serialize(new {
                result = "passed",
                machine = Environment.MachineName,
                startedAt = startedAt.ToString("O"),
                finishedAt = DateTime.Now.ToString("O"),
                root, id, cron = expectedCron,
                due = due.ToString("O"),
                dispatchedAtUtc = dispatchedUtc.ToString("O"),
                actualDispatchCount = lines.Length,
                isolated = true,
                verified = new [] {
                    "real WPF dialog confirm and cancel",
                    "real MainWindow startup settings save API",
                    "manifest disk readback",
                    "ExtensionSchedulerService cron calculation and DispatcherTimer",
                    "HostDispatchJournal and PowerShell extension execution",
                    "disable schedule updates and removes queued next-run"
                }
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(root, "e2e-result.json"), json);
            Console.WriteLine("SCHEDULER_END_TO_END_RESULT=PASS");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("SCHEDULER_END_TO_END_RESULT=FAIL STAGE=" + stage + " EXCEPTION=" + e);
            try {
                if (Directory.Exists(root))
                    File.WriteAllText(Path.Combine(root,"e2e-error.log"), stage + "\n" + e);
            } catch { }
            return 1;
        }
        finally
        {
            try { scheduler?.Stop(); } catch { }
            try { if (host != null) { host.AllowClose = true; host.Close(); } } catch { }
            try { scope?.Dispose(); } catch { }
            app.Shutdown();
        }
    }

    private static System.Collections.Generic.IEnumerable<Button> VisualButtons(DependencyObject parent)
    {
        for (int i=0; i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);
            if (child is Button b) yield return b;
            foreach(var sub in VisualButtons(child)) yield return sub;
        }
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
