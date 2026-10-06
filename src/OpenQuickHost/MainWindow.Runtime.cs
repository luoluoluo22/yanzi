using System.Text.Json;

namespace OpenQuickHost;

public partial class MainWindow
{
    public object GetRuntimeServiceStatus() => new
    {
        owner = HostRuntimeProfile.IsRuntime ? "runtime" : "shell",
        initialized = _isBackgroundServicesInitialized,
        inputHook = InputHookService.IsRunning,
        keyboardHook = KeyboardDoubleTapService.IsRunning,
        mouseGestures = MouseGestureService.IsRunning,
        scheduler = _extensionScheduler?.IsStarted == true,
        mobileBridge = _mobileMessageBridgeTask is { IsCompleted: false },
        presenceHeartbeat = _desktopPresenceHeartbeatTimer.IsEnabled,
        mobilePolling = _mobileMessagePollTimer.IsEnabled,
        personalSyncTimer = _backgroundWebDavSyncTimer.IsEnabled,
        cloudReconnectTimer = _cloudReconnectTimer.IsEnabled,
        agentApi = (System.Windows.Application.Current as App)?.AgentApiServer?.IsListening == true,
        extensions = RunningExtensionRegistry.GetSnapshot().Count
    };

    internal async Task<object> ExecuteRuntimeCommandAsync(CommandItem command, string? input, string source)
    {
        await ExecuteCommandAsync(command, input, source);
        return new { success = true, message = LastRunMessage };
    }

    internal void ApplyRuntimeCatalog(IReadOnlyList<RuntimeCommandDescriptor> descriptors)
    {
        // Runtime is authoritative for installed apps; Dev UI settings remain in its isolated directory.
        _allCommands.Clear();
        _allCommands.AddRange(descriptors.Select(d => d.ToCommand()));
        _localExtensionIndex.Clear();
        foreach (var command in _allCommands.Where(c => c.Source == CommandSource.LocalExtension))
            _localExtensionIndex[command.ExtensionId] = command;
        ApplyFilter(SearchBox.Text);
        _quickPanel?.LoadSlots();
        RefreshRuntimeRunningState();
    }

    internal void RefreshRuntimeRunningState()
    {
        foreach (var command in _allCommands) command.RefreshRunningState();
    }

    internal void SetRuntimeConnectionStatus(string status) => SyncStatus = status;

    private async Task ExecuteThroughRuntimeAsync(CommandItem command, string? input, string source)
    {
        try
        {
            if (source == "launcher") HideToTray();
            var result = await RuntimeRpc.CallAsync("command.execute", new
            {
                id = command.ExtensionId, input = input ?? BuildScriptInput(command, SearchBox.Text), source,
                preview = command.ExtensionDirectoryPath?.Contains("_temp_run_", StringComparison.Ordinal) == true
                    ? RuntimeCommandDescriptor.FromCommand(command) : null
            });
            LastRunMessage = result.GetProperty("message").GetString() ?? "已提交到 Runtime。";
        }
        catch (Exception ex) { LastRunMessage = "后台执行失败：" + ex.Message; }
    }
}
