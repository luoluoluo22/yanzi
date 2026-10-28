using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // The deployment endpoints are higher-privilege: reject empty tokens and
    // cross-site browser requests even if generic API CORS is permissive.
    private bool IsTrustedDeploymentRequest(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_token)) return false;
        var origin = request.Headers["Origin"];
        return string.IsNullOrEmpty(origin) ||
               (request.Url != null && string.Equals(origin,
                   request.Url.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));
    }

    private static CommandItem? FindExtension(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return LocalExtensionCatalog.LoadCommands()
            .FirstOrDefault(command => string.Equals(command.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCSharpExtension(CommandItem command) =>
        command.Runtime is not null &&
        (command.Runtime.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
         command.Runtime.Equals("cs", StringComparison.OrdinalIgnoreCase) ||
         command.Runtime.Equals("c#", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> GetSourceHashAsync(CommandItem command, CancellationToken cancellationToken)
    {
        byte[] content = string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetBytes(command.InlineScriptSource ?? string.Empty)
            : await File.ReadAllBytesAsync(
                Path.Combine(command.ExtensionDirectoryPath!, command.EntryPoint!), cancellationToken);
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private static async Task<(ScriptExecutionResult Result, string Hash, bool SourceChanged)> PreflightAsync(
        CommandItem command, CancellationToken cancellationToken)
    {
        var before = await GetSourceHashAsync(command, cancellationToken);
        var build = await ScriptExtensionRunner.PreparePortableAssetsAsync(command, cancellationToken);
        var after = await GetSourceHashAsync(command, cancellationToken);
        return (build, after, !string.Equals(before, after, StringComparison.Ordinal));
    }

    private async Task HandleExtensionBuildAsync(HttpListenerResponse response, string id)
    {
        var command = FindExtension(id);
        if (command == null)
        {
            await WriteJsonAsync(response, 404, new { error = "extension_not_found" });
            return;
        }
        if (!IsCSharpExtension(command) || !ScriptExtensionRunner.CanExecute(command))
        {
            await WriteJsonAsync(response, 400, new { error = "csharp_source_required" });
            return;
        }
        if (!_deployingExtensions.TryAdd(id, 0))
        {
            await WriteJsonAsync(response, 409, new { error = "deployment_in_progress" });
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var (build, hash, changed) = await PreflightAsync(command, _cts.Token);
            if (changed)
            {
                await WriteJsonAsync(response, 409, new { error = "source_changed_during_build", sourceSha256 = hash });
                return;
            }
            if (!build.Success)
            {
                await WriteJsonAsync(response, 422, new { ok = false, error = "compilation_failed", diagnostics = build.Error });
                return;
            }
            await WriteJsonAsync(response, 200, new
            {
                ok = true, id, sourceSha256 = hash, artifact = build.Output,
                elapsedMs = stopwatch.ElapsedMilliseconds, runningInstanceUntouched = true
            });
        }
        finally
        {
            _deployingExtensions.TryRemove(id, out _);
        }
    }

    private async Task HandleExtensionReloadAsync(HttpListenerResponse response, string id)
    {
        // A shutdown countdown is safety-critical: reject before even spending
        // time compiling, and never automatically restart an active one.
        if (id.Equals("shutdown-timer", StringComparison.OrdinalIgnoreCase) &&
            (_runningTasks.ContainsKey(id) || RunningExtensionRegistry.IsRunning(id)))
        {
            await WriteJsonAsync(response, 409, new { error = "active_shutdown_timer_stop_before_reload" });
            return;
        }
        // An active UI session owns the target window until explicitly closed.
        EnsureNoActiveUiSession(id);
        var command = FindExtension(id);
        if (command == null)
        {
            await WriteJsonAsync(response, 404, new { error = "extension_not_found" });
            return;
        }
        if (!IsCSharpExtension(command) || !ScriptExtensionRunner.CanExecute(command))
        {
            await WriteJsonAsync(response, 400, new { error = "csharp_source_required" });
            return;
        }
        if (!_deployingExtensions.TryAdd(id, 0))
        {
            await WriteJsonAsync(response, 409, new { error = "deployment_in_progress" });
            return;
        }

        try
        {
            // Compile before stopping the current instance. A compile failure leaves it running.
            var (build, hash, changed) = await PreflightAsync(command, _cts.Token);
            if (changed)
            {
                await WriteJsonAsync(response, 409, new { error = "source_changed_during_build" });
                return;
            }
            if (!build.Success)
            {
                await WriteJsonAsync(response, 422, new { ok = false, error = "compilation_failed",
                    diagnostics = build.Error, oldInstanceUntouched = true });
                return;
            }
            if (!string.Equals(hash, await GetSourceHashAsync(command, _cts.Token), StringComparison.Ordinal))
            {
                await WriteJsonAsync(response, 409, new { error = "source_changed_before_reload" });
                return;
            }

            bool wasRunning = _runningTasks.ContainsKey(id) || RunningExtensionRegistry.IsRunning(id);
            if (!wasRunning)
            {
                // Reloading the catalog must NOT launch an idle small program.
                // Running shutdown-timer here could trigger a real OS shutdown.
                _onMutated(null);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true, id, sourceSha256 = hash,
                    catalogRefreshed = true, wasRunning = false,
                    isRunning = false, restartPerformed = false
                });
                return;
            }
            if (id.Equals("shutdown-timer", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, 409, new
                {
                    error = "active_shutdown_timer_stop_before_reload",
                    detail = "Stop or cancel the countdown manually before reloading."
                });
                return;
            }
            if (_runningTasks.TryGetValue(id, out var task))
            {
                task.Cancel();
            }
            var instances = RunningExtensionRegistry.GetSnapshot()
                .Where(item => string.Equals(item.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
            foreach (var instance in instances)
            {
                RunningExtensionRegistry.TryTerminate(instance.InstanceId, out _);
            }

            // The registry retains managed instances until their threads actually exit.
            for (int attempt = 0; attempt < 50 &&
                (_runningTasks.ContainsKey(id) || RunningExtensionRegistry.IsRunning(id)); attempt++)
            {
                await Task.Delay(100, _cts.Token);
            }
            if (_runningTasks.ContainsKey(id) || RunningExtensionRegistry.IsRunning(id))
            {
                await WriteJsonAsync(response, 409, new { error = "stop_timeout",
                    detail = "Previous instance has not fully exited; new version was not started." });
                return;
            }

            // A source change while waiting for shutdown must not start unverified code.
            if (!string.Equals(hash, await GetSourceHashAsync(command, _cts.Token), StringComparison.Ordinal))
            {
                await WriteJsonAsync(response, 409, new { error = "source_changed_after_stop",
                    oldInstanceStopped = wasRunning, recoveryRequired = wasRunning });
                return;
            }

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            startupTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            var result = await ScriptExtensionRunner.ExecuteAsync(
                command, null, "agent-api-reload", startupTimeout.Token);
            bool running = RunningExtensionRegistry.IsRunning(id) || _runningTasks.ContainsKey(id);
            if (result.Success)
            {
                // Runtime reload alone does not refresh radial-menu and pinned
                // CommandItem caches. Refresh every host UI view after compiling.
                _onMutated(null);
            }
            HostAssets.AppendLog($"Local Agent API reload: id={id}, success={result.Success}, running={running}");
            await WriteJsonAsync(response, result.Success ? 200 : 500, new
            {
                ok = result.Success, id, sourceSha256 = hash, wasRunning, isRunning = running,
                output = result.Output, error = result.Success ? string.Empty : result.Error,
                message = result.Success ? result.Error : string.Empty,
                recoveryRequired = !result.Success && wasRunning,
                rollbackAvailable = false
            });
        }
        finally
        {
            _deployingExtensions.TryRemove(id, out _);
        }
    }
}
