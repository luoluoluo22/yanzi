using System.Windows;
using System.Windows.Threading;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class MainWindow
{
    private readonly DispatcherTimer _gitExtensionUpdateTimer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };
    private bool _gitExtensionUpdateRunning;

    private void StartGitExtensionUpdateScheduler()
    {
        if (!HostRuntimeProfile.OwnsBackgroundServices) return;
        _gitExtensionUpdateTimer.Tick += async (_, _) => await CheckGitExtensionUpdatesAsync(onStartup: false);
        Loaded += async (_, _) =>
        {
            _gitExtensionUpdateTimer.Start();
            await CheckGitExtensionUpdatesAsync(onStartup: true);
        };
        Closed += (_, _) => _gitExtensionUpdateTimer.Stop();
    }

    private async Task CheckGitExtensionUpdatesAsync(bool onStartup)
    {
        if (_gitExtensionUpdateRunning || Dispatcher.HasShutdownStarted) return;
        _gitExtensionUpdateRunning = true;
        try
        {
            var policies = GitExtensionUpdatePolicyStore.Load().Values.ToArray();
            var now = DateTimeOffset.UtcNow;
            foreach (var policy in policies)
            {
                if (!GitExtensionUpdateService.IsDue(policy, now, onStartup)) continue;
                var path = SyncPackageSafety.ResolveExtensionDirectory(HostAssets.ExtensionsPath, policy.ExtensionId);
                GitExtensionUpdateOutcome result;
                try
                {
                    result = await GitExtensionUpdateService.CheckAsync(path, policy);
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Git extension update failed: id={policy.ExtensionId}, error={ex.Message}");
                    continue;
                }
                HostAssets.AppendLog($"Git extension update: id={policy.ExtensionId}, status={result.Status}, commit={result.RemoteCommit}");
                // The settings dialog may have changed options during this check.
                var current = GitExtensionUpdatePolicyStore.Load();
                if (current.TryGetValue(policy.ExtensionId, out var latest) &&
                    latest.Mode == policy.Mode && latest.Frequency == policy.Frequency &&
                    latest.Remote == policy.Remote && latest.Branch == policy.Branch)
                {
                    try
                    {
                        GitExtensionUpdatePolicyStore.Save(latest with
                        {
                            LastCheckedUtc = DateTimeOffset.UtcNow,
                            LastSeenCommit = result.RemoteCommit.Length == 0 ? latest.LastSeenCommit : result.RemoteCommit
                        });
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"Git extension update policy save failed: id={policy.ExtensionId}, error={ex.Message}");
                    }
                }
                if (result.NotifyUser &&
                    (result.RemoteCommit.Length == 0 ||
                     !string.Equals(policy.LastSeenCommit, result.RemoteCommit, StringComparison.OrdinalIgnoreCase) ||
                     result.Status == "updated"))
                {
                    if (System.Windows.Application.Current is App app)
                        app.ShowDesktopNotification("Git 小程序更新", $"{policy.ExtensionId}：{result.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Git extension scheduler failed safely: {ex.Message}");
        }
        finally { _gitExtensionUpdateRunning = false; }
    }
}