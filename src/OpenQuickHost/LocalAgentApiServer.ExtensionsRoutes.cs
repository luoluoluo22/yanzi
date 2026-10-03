using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private async Task HandleExtensionsRoute17Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        await WriteJsonAsync(response, 200, new { template = LocalExtensionCatalog.CreateTemplateJson() });
        return;
    }

    private async Task HandleExtensionsRoute18Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var items = LocalExtensionCatalog.LoadCommands().Select(ToDto).ToList();
        await WriteJsonAsync(response, 200, new { items });
        return;
    }

    private async Task HandleExtensionsRoute19Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var items = LocalExtensionCatalog.LoadCommands()
            .Select(command => new
            {
                user_id = "local",
                userId = "local",
                extension_id = command.ExtensionId,
                extensionId = command.ExtensionId,
                installed_version = command.DeclaredVersion,
                installedVersion = command.DeclaredVersion,
                enabled = 1,
                settings_json = string.Empty,
                settingsJson = string.Empty,
                updated_at = string.Empty,
                updatedAt = string.Empty
            })
            .ToList();
        await WriteJsonAsync(response, 200, new
        {
            ok = true,
            userId = "local",
            source = "local-agent-api",
            items
        });
        return;
    }

    private async Task HandleExtensionsRoute26Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        if (!IsTrustedDeploymentRequest(request))
        {
            await WriteJsonAsync(response, 403, new { error = "deployment_origin_or_token_invalid" });
            return;
        }
        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/build".Length]);
        await HandleExtensionBuildAsync(response, id);
        return;
    }

    private async Task HandleExtensionsRoute27Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        if (!IsTrustedDeploymentRequest(request))
        {
            await WriteJsonAsync(response, 403, new { error = "deployment_origin_or_token_invalid" });
            return;
        }
        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/reload".Length]);
        await HandleExtensionReloadAsync(response, id);
        return;
    }

    private async Task HandleExtensionsRoute28Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/run".Length]);
        var payload = await ReadJsonBodyAsync(request);
        var input = GetString(payload, "input");

        var commands = LocalExtensionCatalog.LoadCommands();
        var command = commands.FirstOrDefault(c => string.Equals(c.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            await WriteJsonAsync(response, 404, new { error = "not_found" });
            return;
        }

        EnsureNoActiveUiSession(id);
        if (_deployingExtensions.ContainsKey(id))
        {
            await WriteJsonAsync(response, 409, new { error = "deployment_in_progress" });
            return;
        }

        if (_runningTasks.ContainsKey(id))
        {
            await WriteJsonAsync(response, 400, new { error = "already_running" });
            return;
        }

        // WebView applications share the same authenticated launch endpoint as scripts.
        if (command.App != null)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!AppExtensionWindow.TryActivateExisting(command))
                        new AppExtensionWindow(command, input, "agent-api").Show();
                });
                await WriteJsonAsync(response, 200, new { ok = true, success = true, opened = true });
            }
            catch (Exception ex) { await WriteJsonAsync(response, 500, new { ok = false, error = ex.Message }); }
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _runningTasks[id] = cts;
            ScriptExecutionResult result;
            try
            {
                result = await ScriptExtensionRunner.ExecuteAsync(command, input, "agent-api", cts.Token);
            }
            finally
            {
                _runningTasks.TryRemove(id, out _);
            }

            await WriteJsonAsync(response, 200, new {
                ok = result.Success,
                success = result.Success,
                output = result.Output,
                message = result.Success ? result.Error : string.Empty,
                error = result.Success ? string.Empty : result.Error,
                exitCode = result.ExitCode
            });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = ex.Message });
        }
        return;
    }

    private async Task HandleExtensionsRoute29Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/logs".Length]);
        var command = FindExtension(id);
        if (command == null)
        {
            await WriteJsonAsync(response, 404, new { error = "extension_not_found" });
            return;
        }
        int maxLines = int.TryParse(request.QueryString["maxLines"], out var requested)
            ? Math.Clamp(requested, 1, 500) : 100;
        var logPath = Path.Combine(command.ExtensionDirectoryPath!, "debug.log");
        var lines = File.Exists(logPath) ? File.ReadLines(logPath).TakeLast(maxLines).ToArray() : [];
        await WriteJsonAsync(response, 200, new { ok = true, id, logs = lines });
        return;
    }

    private async Task HandleExtensionsRoute30Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/status".Length]);
        var command = FindExtension(id);
        if (command == null)
        {
            await WriteJsonAsync(response, 404, new { error = "extension_not_found" });
            return;
        }
        var instances = RunningExtensionRegistry.GetSnapshot()
            .Where(x => string.Equals(x.ExtensionId, id, StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.InstanceId, x.Runtime, x.StartedAt, x.LaunchSource })
            .ToArray();
        bool apiTaskActive = _runningTasks.ContainsKey(id);
        await WriteJsonAsync(response, 200, new
        {
            ok = true, id, installed = true, version = command.DeclaredVersion,
            runtime = command.Runtime, isRunning = apiTaskActive || instances.Length > 0,
            apiTaskActive, instanceCount = instances.Length, instances,
            isDeploying = _deployingExtensions.ContainsKey(id),
            uiSession = UiSessions.Values.Where(s => string.Equals(
                s.ExtensionId, id, StringComparison.OrdinalIgnoreCase))
                .Select(SessionDto).FirstOrDefault()
        });
        return;
    }

    private async Task HandleExtensionsRoute31Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/stop".Length]);
        if (_deployingExtensions.ContainsKey(id))
        {
            await WriteJsonAsync(response, 409, new { error = "deployment_in_progress" });
            return;
        }
        EndUiSessionsForExtension(id, "extension_stopped");
        var stoppedAny = false;

        // 1. 尝试停止轻量级后台Task
        if (_runningTasks.TryGetValue(id, out var cts))
        {
            try { cts.Cancel(); } catch { }
            // Keep the task registered until its execution finally block runs.
            stoppedAny = true;
        }

        // 2. 尝试停止常驻的托管扩展或进程 (来自 RunningExtensionRegistry)
        var runningInstances = RunningExtensionRegistry.GetSnapshot()
            .Where(x => string.Equals(x.ExtensionId, id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var instance in runningInstances)
        {
            if (RunningExtensionRegistry.TryTerminate(instance.InstanceId, out _))
            {
                stoppedAny = true;
            }
        }

        if (stoppedAny)
        {
            bool stillRunning = _runningTasks.ContainsKey(id) || RunningExtensionRegistry.IsRunning(id);
            await WriteJsonAsync(response, 200, new { ok = true, stopped = !stillRunning, stopRequested = true, isRunning = stillRunning });
        }
        else
        {
            await WriteJsonAsync(response, 200, new { ok = true, stopped = false, reason = "not_running" });
        }
        return;
    }

    private async Task HandleExtensionsRoute32Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/webview/".Length..^"/execute".Length]);
        var payload = await ReadJsonBodyAsync(request);
        var script = GetString(payload, "script");
        if (string.IsNullOrWhiteSpace(script))
        {
            await WriteJsonAsync(response, 400, new { error = "script_required" });
            return;
        }

        if (!HostObjectRegistry.TryGetObject(id, out var obj))
        {
            await WriteJsonAsync(response, 404, new { error = "object_not_found" });
            return;
        }

        if (obj is not Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            await WriteJsonAsync(response, 400, new {
                error = "type_mismatch",
                expected = typeof(Microsoft.Web.WebView2.Wpf.WebView2).AssemblyQualifiedName,
                actual = obj?.GetType().AssemblyQualifiedName
            });
            return;
        }

        try
        {
            var resultJson = await webView.Dispatcher.InvokeAsync(async () =>
            {
                return await webView.ExecuteScriptAsync(script);
            }).Task.Unwrap();

            var result = System.Text.Json.JsonSerializer.Deserialize<string>(resultJson);
            await WriteJsonAsync(response, 200, new { ok = true, result });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = "execution_failed", detail = ex.Message });
        }
        return;
    }

    private async Task HandleExtensionsRoute36Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var extensionId = Uri.UnescapeDataString(path["/v1/account-storage/".Length..]);
        var accountId = new OpenQuickHost.Sync.CloudSyncClient(OpenQuickHost.Sync.SyncConfigLoader.Load()).CurrentUserId;
        if (request.HttpMethod == "GET")
        {
            var key = GetQueryString(request, "key");
            if (string.IsNullOrWhiteSpace(key)) { await WriteJsonAsync(response, 400, new { error = "key_required" }); return; }
            var value = await OpenQuickHost.Sync.AccountExtensionDataStore.TryReadAsync(extensionId, key);
            await WriteJsonAsync(response, value.Available ? 200 : 503,
                new { ok = value.Available, exists = value.Exists, revision = value.Revision, content = value.Content, accountId });
            return;
        }
        if (request.HttpMethod == "PUT")
        {
            var payload = await ReadJsonBodyAsync(request);
            var key = GetString(payload, "key");
            if (string.IsNullOrWhiteSpace(key) || !payload.TryGetProperty("expectedRevision", out var rev)
                || !rev.TryGetInt64(out var expectedRevision) || expectedRevision < 0)
            { await WriteJsonAsync(response, 400, new { error = "key_and_revision_required" }); return; }
            var content = GetString(payload, "content") ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(content) > 262144)
            { await WriteJsonAsync(response, 413, new { error = "content_too_large" }); return; }
            try
            {
                var expectedAccount = GetString(payload, "accountId");
                if (string.IsNullOrWhiteSpace(expectedAccount) || expectedAccount != accountId)
                { await WriteJsonAsync(response, 409, new { ok = false, error = "account_changed" }); return; }
                var value = await OpenQuickHost.Sync.AccountExtensionDataStore.WriteAsync(extensionId, key, content, expectedRevision,
                    expectedAccountId: expectedAccount);
                await WriteJsonAsync(response, value.Available ? 200 : 503, new { ok = value.Available, revision = value.Revision, accountId });
            }
            catch (OpenQuickHost.Sync.CloudSyncRevisionConflictException conflict)
            { await WriteJsonAsync(response, 409, new { ok = false, conflict = true, currentRevision = conflict.CurrentRevision }); }
            return;
        }
        await WriteJsonAsync(response, 405, new { error = "method_not_allowed" }); return;
    }

    private async Task HandleExtensionsRoute37Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..]);
        var key = GetQueryString(request, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            await WriteJsonAsync(response, 400, new { error = "key_required" });
            return;
        }

        var scope = GetQueryString(request, "scope");
        var result = await ExtensionStorageService.ReadTextAsync(extensionId, key, scope);
        await WriteJsonAsync(response, 200, new
        {
            found = result.Found,
            content = result.Content,
            source = result.Source,
            localPath = result.LocalPath
        });
        return;
    }

    private async Task HandleExtensionsRoute38Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..]);
        var key = GetQueryString(request, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            await WriteJsonAsync(response, 400, new { error = "key_required" });
            return;
        }
        var scope = GetQueryString(request, "scope");
        var result = await ExtensionStorageService.DeleteTextAsync(extensionId, key, scope);
        await WriteJsonAsync(response, 200, new
        {
            ok = true,
            localPath = result.LocalPath,
            cloudSaved = result.CloudSaved,
            result.Scope,
            cloudMessage = result.CloudMessage
        });
        return;
    }

    private async Task HandleExtensionsRoute39Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
        var manifest = LocalExtensionCatalog.LoadManifestJson(id);
        var command = LocalExtensionCatalog.LoadCommands()
            .FirstOrDefault(item => string.Equals(item.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
        var accentHex = command?.AccentBrush is System.Windows.Media.SolidColorBrush accentBrush
            ? accentBrush.Color.ToString()
            : string.Empty;
        await WriteJsonAsync(response, 200, new
        {
            id,
            extension_id = id,
            extensionId = id,
            manifest,
            display_name = command?.Title ?? id,
            displayName = command?.Title ?? id,
            name = command?.Title ?? id,
            description = command?.Subtitle ?? string.Empty,
            icon = command?.IconReference ?? string.Empty,
            accent_hex = accentHex,
            accentHex = accentHex,
            version = command?.DeclaredVersion ?? string.Empty
        });
        return;
    }

    private async Task HandleExtensionsRoute40Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var manifest = GetString(payload, "manifest");
        if (string.IsNullOrWhiteSpace(manifest))
        {
            await WriteJsonAsync(response, 400, new { error = "manifest_required" });
            return;
        }

        var command = LocalExtensionCatalog.SaveJsonExtension(manifest, forceNewSystemId: true);
        _onMutated(command.ExtensionId);
        MainWindow.QueueCSharpPrebuild(command, "api-add");
        await WriteJsonAsync(response, 201, new { item = ToDto(command) });
        return;
    }

    private async Task HandleExtensionsRoute41Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..^"/sync".Length]);
        _ = Task.Run(() => ExtensionStorageService.SyncLocalDirectoryToCloudAsync(extensionId, _cts.Token), _cts.Token);
        await WriteJsonAsync(response, 200, new { ok = true, message = $"sync started for extension data: {extensionId}" });
        return;
    }

    private async Task HandleExtensionsRoute42Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..]);
        var payload = await ReadJsonBodyAsync(request);
        var key = GetString(payload, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            await WriteJsonAsync(response, 400, new { error = "key_required" });
            return;
        }

        var content = GetString(payload, "content") ?? string.Empty;
        var scope = GetString(payload, "scope");
        var result = await ExtensionStorageService.WriteTextAsync(extensionId, key, content, scope);
        await WriteJsonAsync(response, 200, new
        {
            ok = true,
            localPath = result.LocalPath,
            cloudSaved = result.CloudSaved,
            result.Scope,
            cloudMessage = result.CloudMessage
        });
        return;
    }

    private async Task HandleExtensionsRoute43Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
        var payload = await ReadJsonBodyAsync(request);
        var manifest = GetString(payload, "manifest");
        if (string.IsNullOrWhiteSpace(manifest))
        {
            await WriteJsonAsync(response, 400, new { error = "manifest_required" });
            return;
        }

        using var document = JsonDocument.Parse(manifest);
        if (!document.RootElement.TryGetProperty("id", out var idElement) ||
            !string.Equals(idElement.GetString(), id, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, 400, new { error = "id_mismatch" });
            return;
        }

        var command = LocalExtensionCatalog.SaveJsonExtension(manifest, forceNewSystemId: false);
        _onMutated(command.ExtensionId);
        MainWindow.QueueCSharpPrebuild(command, "api-edit");
        await WriteJsonAsync(response, 200, new { item = ToDto(command) });
        return;
    }

    private async Task HandleExtensionsRoute46Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
        var commands = LocalExtensionCatalog.LoadCommands();
        var command = commands.FirstOrDefault(c => string.Equals(c.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
        ExtensionRecycleBinService.MoveToRecycleBin(id, command?.ExtensionDirectoryPath);
        _onMutated(id);
        await WriteJsonAsync(response, 200, new { ok = true, id });
        return;
    }

    private async Task HandleExtensionsRoute47Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/publish".Length]);
        if (_onPublishExtension == null)
        {
            await WriteJsonAsync(response, 400, new { error = "publish_not_supported" });
            return;
        }

        var (ok, message) = await _onPublishExtension(id);
        if (ok)
        {
            await WriteJsonAsync(response, 200, new { ok = true, message });
        }
        else
        {
            await WriteJsonAsync(response, 400, new { error = "publish_failed", detail = message });
        }
        return;
    }

    private async Task HandleExtensionsRoute48Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/unpublish".Length]);
        if (_onUnpublishExtension == null)
        {
            await WriteJsonAsync(response, 400, new { error = "unpublish_not_supported" });
            return;
        }

        var (ok, message) = await _onUnpublishExtension(id);
        if (ok)
        {
            await WriteJsonAsync(response, 200, new { ok = true, message });
        }
        else
        {
            await WriteJsonAsync(response, 400, new { error = "unpublish_failed", detail = message });
        }
        return;
    }

    private async Task HandleExtensionsRoute49Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var items = ExtensionRecycleBinService.LoadEntries().Select(x => new
        {
            itemId = x.ItemId,
            extensionId = x.ExtensionId,
            title = x.Title,
            deletedAt = x.DeletedAtUtc,
            category = x.Category
        }).ToList();
        await WriteJsonAsync(response, 200, new { items });
        return;
    }

    private async Task HandleExtensionsRoute50Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/recycle-bin/".Length..^"/restore".Length]);
        var restored = ExtensionRecycleBinService.RestoreFromRecycleBin(id);
        _onMutated(null);
        await WriteJsonAsync(response, 200, new { ok = true, id });
        return;
    }

    private async Task HandleExtensionsRoute51Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/extensions/recycle-bin/".Length..]);
        var deleted = ExtensionRecycleBinService.DeletePermanently(id);
        _onMutated(null);
        await WriteJsonAsync(response, 200, new { ok = true, id });
        return;
    }

    private async Task HandleExtensionsRoute52Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var id = Uri.UnescapeDataString(path["/v1/store/extensions/".Length..^"/install".Length]);
        if (_onInstallExtension == null)
        {
            await WriteJsonAsync(response, 400, new { error = "install_not_supported" });
            return;
        }

        var (ok, message) = await _onInstallExtension(id);
        if (ok)
        {
            await WriteJsonAsync(response, 200, new { ok = true, message });
        }
        else
        {
            await WriteJsonAsync(response, 400, new { error = "install_failed", detail = message });
        }
        return;
    }
}
