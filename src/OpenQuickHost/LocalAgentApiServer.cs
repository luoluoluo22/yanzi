using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed class LocalAgentApiServer : IDisposable
{
    private readonly string _prefix;
    private readonly string _token;
    private readonly Action _onMutated;
    private readonly Action? _onTriggerSync;
    private readonly Func<string, Task<(bool ok, string message)>>? _onPublishExtension;
    private readonly Func<string, Task<(bool ok, string message)>>? _onUnpublishExtension;
    private readonly Func<string, Task<(bool ok, string message)>>? _onInstallExtension;
    private readonly Func<Task<AuthMeResponse?>>? _onGetMe;
    private readonly Func<string, string, Task>? _onShowNotification;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new(StringComparer.OrdinalIgnoreCase);

    public LocalAgentApiServer(
        string prefix, 
        string token, 
        Action onMutated, 
        Action? onTriggerSync = null,
        Func<string, Task<(bool ok, string message)>>? onPublishExtension = null,
        Func<string, Task<(bool ok, string message)>>? onUnpublishExtension = null,
        Func<string, Task<(bool ok, string message)>>? onInstallExtension = null,
        Func<Task<AuthMeResponse?>>? onGetMe = null,
        Func<string, string, Task>? onShowNotification = null)
    {
        _prefix = prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
        _token = token;
        _onMutated = onMutated;
        _onTriggerSync = onTriggerSync;
        _onPublishExtension = onPublishExtension;
        _onUnpublishExtension = onUnpublishExtension;
        _onInstallExtension = onInstallExtension;
        _onGetMe = onGetMe;
        _onShowNotification = onShowNotification;
        _listener.Prefixes.Add(_prefix);
    }

    public void Start()
    {
        _listener.Start();
        _loopTask = Task.Run(ListenLoopAsync);
        HostAssets.AppendLog($"Local Agent API started at {_prefix}");
    }

    public void Stop()
    {
        _cts.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore graceful shutdown errors.
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
            }
            catch (HttpListenerException)
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Local Agent API listen error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Yanzi-Token";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";

        try
        {
            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/store/extensions/status")
            {
                var ids = ParseIds(GetQueryString(request, "ids"));
                var commands = LocalExtensionCatalog.LoadCommands()
                    .Where(command => ids.Count == 0 || ids.Contains(command.ExtensionId))
                    .Select(command => new
                    {
                        extensionId = command.ExtensionId,
                        installed = true,
                        version = command.DeclaredVersion,
                        title = command.Title
                    })
                    .ToList();
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    items = commands
                });
                return;
            }

            if (!IsAuthorized(request))
            {
                await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonAsync(response, 200, new { ok = true, service = "yanzi-local-agent-api" });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/logs")
            {
                var maxLinesStr = request.QueryString["maxLines"];
                if (!int.TryParse(maxLinesStr, out var maxLines) || maxLines <= 0)
                {
                    maxLines = 1000;
                }
                var lines = HostAssets.ReadHostLogTailLines(1024 * 512, maxLines);
                await WriteJsonAsync(response, 200, new { ok = true, logs = lines });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/extensions/template")
            {
                await WriteJsonAsync(response, 200, new { template = LocalExtensionCatalog.CreateTemplateJson() });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/extensions")
            {
                var items = LocalExtensionCatalog.LoadCommands().Select(ToDto).ToList();
                await WriteJsonAsync(response, 200, new { items });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/quickpanel/groups")
            {
                var settings = AppSettingsStore.Load();
                await WriteJsonAsync(response, 200, new { 
                    selectedGroupId = settings.SelectedQuickPanelGlobalGroupId,
                    groups = settings.QuickPanelGlobalGroups 
                });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/quickpanel/groups")
            {
                var payload = await ReadJsonBodyAsync(request);
                var id = GetString(payload, "id") ?? Guid.NewGuid().ToString("N");
                var name = GetString(payload, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    await WriteJsonAsync(response, 400, new { error = "name_required" });
                    return;
                }

                var settings = AppSettingsStore.Load();
                settings.QuickPanelGlobalGroups.Add(new QuickPanelGroupSettings
                {
                    Id = id,
                    Name = name,
                    Slots = Enumerable.Repeat<string?>(null, 12).ToList(),
                    SlotItems = Enumerable.Repeat<QuickPanelSlotItem?>(null, 12).ToList()
                });
                AppSettingsStore.Save(settings);
                _onMutated();
                await WriteJsonAsync(response, 200, new { ok = true, id });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/quickpanel/add")
            {
                var payload = await ReadJsonBodyAsync(request);
                var extensionId = GetString(payload, "extensionId");
                var groupId = GetString(payload, "groupId");
                if (string.IsNullOrWhiteSpace(extensionId))
                {
                    await WriteJsonAsync(response, 400, new { error = "extensionId_required" });
                    return;
                }

                var settings = AppSettingsStore.Load();
                var group = !string.IsNullOrWhiteSpace(groupId) 
                    ? settings.QuickPanelGlobalGroups.FirstOrDefault(item => string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase))
                    : settings.QuickPanelGlobalGroups.FirstOrDefault(item => string.Equals(item.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
                      ?? settings.QuickPanelGlobalGroups.FirstOrDefault();

                if (group == null)
                {
                    await WriteJsonAsync(response, 400, new { error = "no_quickpanel_group" });
                    return;
                }

                group.SlotItems ??= new List<QuickPanelSlotItem?>();
                while (group.SlotItems.Count < 12)
                {
                    group.SlotItems.Add(null);
                }

                if (group.SlotItems.Any(slot =>
                        slot != null &&
                        ((!slot.IsFolder && string.Equals(slot.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase)) ||
                         (slot.IsFolder && slot.FolderExtensionIds != null && slot.FolderExtensionIds.Any(id => string.Equals(id, extensionId, StringComparison.OrdinalIgnoreCase))))))
                {
                    await WriteJsonAsync(response, 200, new { ok = true, message = "already_exists" });
                    return;
                }

                var index = group.SlotItems.FindIndex(item => item == null);
                if (index >= 0)
                {
                    group.SlotItems[index] = new QuickPanelSlotItem { ExtensionId = extensionId };
                    group.Slots = group.SlotItems.Select(item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
                    
                    if (string.Equals(group.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
                    {
                        settings.QuickPanelSlots ??= new List<string?>();
                        while (settings.QuickPanelSlots.Count <= index)
                        {
                            settings.QuickPanelSlots.Add(null);
                        }
                        settings.QuickPanelSlots[index] = extensionId;
                    }

                    AppSettingsStore.Save(settings);
                    _onMutated();
                    await WriteJsonAsync(response, 200, new { ok = true, index, groupId = group.Id });
                    return;
                }

                await WriteJsonAsync(response, 400, new { error = "quickpanel_full" });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/app/notify")
            {
                var payload = await ReadJsonBodyAsync(request);
                var title = GetString(payload, "title") ?? "Yanzi";
                var message = GetString(payload, "message") ?? string.Empty;

                if (_onShowNotification != null)
                {
                    await _onShowNotification(title, message);
                }

                await WriteJsonAsync(response, 200, new { ok = true });
                return;
            }

            if (request.HttpMethod == "POST" && path.EndsWith("/run", StringComparison.Ordinal) && path.StartsWith("/v1/extensions/", StringComparison.Ordinal))
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

                if (_runningTasks.ContainsKey(id))
                {
                    await WriteJsonAsync(response, 400, new { error = "already_running" });
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
                        ok = true, 
                        success = result.Success, 
                        output = result.Output, 
                        error = result.Error,
                        exitCode = result.ExitCode 
                    });
                }
                catch (Exception ex)
                {
                    await WriteJsonAsync(response, 500, new { error = ex.Message });
                }
                return;
            }

            if (request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/status".Length]);
                var isRunning = _runningTasks.ContainsKey(id);
                await WriteJsonAsync(response, 200, new { ok = true, isRunning });
                return;
            }

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/stop", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/stop".Length]);
                var stoppedAny = false;

                // 1. 尝试停止轻量级后台Task
                if (_runningTasks.TryGetValue(id, out var cts))
                {
                    try { cts.Cancel(); } catch { }
                    _runningTasks.TryRemove(id, out _);
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
                    await WriteJsonAsync(response, 200, new { ok = true, stopped = true });
                }
                else
                {
                    await WriteJsonAsync(response, 200, new { ok = true, stopped = false, reason = "not_running" });
                }
                return;
            }

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/webview/", StringComparison.Ordinal) && path.EndsWith("/execute", StringComparison.Ordinal))
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

            if (request.HttpMethod == "GET" && path == "/v1/sync/webdav-config")
            {
                await WriteJsonAsync(response, 200, GetWebDavConfigDto());
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/sync/trigger")
            {
                if (_onTriggerSync != null)
                {
                    _onTriggerSync();
                }

                await WriteJsonAsync(response, 200, new { ok = true, message = "sync triggered" });
                return;
            }

            if (request.HttpMethod == "GET" && path.StartsWith("/v1/storage/", StringComparison.Ordinal))
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

            if (request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && !path.StartsWith("/v1/extensions/recycle-bin", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
                var manifest = LocalExtensionCatalog.LoadManifestJson(id);
                await WriteJsonAsync(response, 200, new { id, manifest });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/extensions")
            {
                var payload = await ReadJsonBodyAsync(request);
                var manifest = GetString(payload, "manifest");
                if (string.IsNullOrWhiteSpace(manifest))
                {
                    await WriteJsonAsync(response, 400, new { error = "manifest_required" });
                    return;
                }

                var command = LocalExtensionCatalog.SaveJsonExtension(manifest);
                _onMutated();
                await WriteJsonAsync(response, 201, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/storage/", StringComparison.Ordinal) && path.EndsWith("/sync", StringComparison.Ordinal))
            {
                var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..^"/sync".Length]);
                _ = Task.Run(() => ExtensionStorageService.SyncLocalDirectoryToCloudAsync(extensionId, _cts.Token), _cts.Token);
                await WriteJsonAsync(response, 200, new { ok = true, message = $"sync started for extension data: {extensionId}" });
                return;
            }

            if (request.HttpMethod == "PUT" && path.StartsWith("/v1/storage/", StringComparison.Ordinal))
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

            if (request.HttpMethod == "PUT" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal))
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

                var command = LocalExtensionCatalog.SaveJsonExtension(manifest);
                _onMutated();
                await WriteJsonAsync(response, 200, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "PATCH" && path.EndsWith("/rename", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/rename".Length]);
                var payload = await ReadJsonBodyAsync(request);
                var name = GetString(payload, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    await WriteJsonAsync(response, 400, new { error = "name_required" });
                    return;
                }

                var command = LocalExtensionCatalog.RenameExtension(id, name);
                _onMutated();
                await WriteJsonAsync(response, 200, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "PATCH" && path.EndsWith("/shortcut", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/shortcut".Length]);
                var payload = await ReadJsonBodyAsync(request);
                var shortcut = GetString(payload, "shortcut");
                var command = LocalExtensionCatalog.SetGlobalShortcut(id, shortcut);
                _onMutated();
                await WriteJsonAsync(response, 200, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "DELETE" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && !path.StartsWith("/v1/extensions/recycle-bin", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
                var commands = LocalExtensionCatalog.LoadCommands();
                var command = commands.FirstOrDefault(c => string.Equals(c.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
                ExtensionRecycleBinService.MoveToRecycleBin(id, command?.ExtensionDirectoryPath);
                _onMutated();
                await WriteJsonAsync(response, 200, new { ok = true, id });
                return;
            }

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/publish", StringComparison.Ordinal))
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

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/unpublish", StringComparison.Ordinal))
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

            if (request.HttpMethod == "GET" && path == "/v1/extensions/recycle-bin")
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

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/recycle-bin/", StringComparison.Ordinal) && path.EndsWith("/restore", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/recycle-bin/".Length..^"/restore".Length]);
                var restored = ExtensionRecycleBinService.RestoreFromRecycleBin(id);
                _onMutated();
                await WriteJsonAsync(response, 200, new { ok = true, id });
                return;
            }

            if (request.HttpMethod == "DELETE" && path.StartsWith("/v1/extensions/recycle-bin/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/recycle-bin/".Length..]);
                var deleted = ExtensionRecycleBinService.DeletePermanently(id);
                _onMutated();
                await WriteJsonAsync(response, 200, new { ok = true, id });
                return;
            }

            if (request.HttpMethod == "POST" && path.StartsWith("/v1/store/extensions/", StringComparison.Ordinal) && path.EndsWith("/install", StringComparison.Ordinal))
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

            if (request.HttpMethod == "GET" && path == "/v1/user/me")
            {
                if (_onGetMe == null)
                {
                    await WriteJsonAsync(response, 400, new { error = "auth_not_supported" });
                    return;
                }
                var me = await _onGetMe();
                await WriteJsonAsync(response, 200, new { ok = true, user = me });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/settings")
            {
                var settings = AppSettingsStore.Load();
                await WriteJsonAsync(response, 200, new { ok = true, settings });
                return;
            }

            if (request.HttpMethod == "PATCH" && path == "/v1/settings")
            {
                var payload = await ReadJsonBodyAsync(request);
                var settings = AppSettingsStore.Load();
                if (payload.TryGetProperty("themeMode", out var themeEl) && themeEl.ValueKind == JsonValueKind.String)
                {
                    settings.ThemeMode = themeEl.GetString()!;
                }
                if (payload.TryGetProperty("launchAtStartup", out var launchEl) && (launchEl.ValueKind == JsonValueKind.True || launchEl.ValueKind == JsonValueKind.False))
                {
                    settings.LaunchAtStartup = launchEl.GetBoolean();
                }
                AppSettingsStore.Save(settings);
                _onMutated();
                await WriteJsonAsync(response, 200, new { ok = true, settings });
                return;
            }

            if (request.HttpMethod == "DELETE" && path == "/v1/quickpanel/remove")
            {
                var extensionId = request.QueryString["extensionId"];
                if (string.IsNullOrWhiteSpace(extensionId))
                {
                    await WriteJsonAsync(response, 400, new { error = "missing_extensionId" });
                    return;
                }
                
                var settings = AppSettingsStore.Load();
                var removed = false;
                foreach (var group in settings.QuickPanelGlobalGroups)
                {
                    if (group.Slots != null)
                    {
                        for (int i = 0; i < group.Slots.Count; i++)
                        {
                            if (string.Equals(group.Slots[i], extensionId, StringComparison.OrdinalIgnoreCase))
                            {
                                group.Slots[i] = null;
                                if (group.SlotItems != null && i < group.SlotItems.Count)
                                {
                                    group.SlotItems[i] = null;
                                }
                                removed = true;
                            }
                        }
                    }
                }
                if (removed)
                {
                    AppSettingsStore.Save(settings);
                    _onMutated();
                }
                await WriteJsonAsync(response, 200, new { ok = true, removed });
                return;
            }

            if (request.HttpMethod == "PUT" && path == "/v1/quickpanel/reorder")
            {
                var payload = await ReadJsonBodyAsync(request);
                var groupId = GetString(payload, "groupId");
                var items = payload.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array 
                    ? itemsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                    : new List<string>();
                
                if (string.IsNullOrWhiteSpace(groupId))
                {
                    await WriteJsonAsync(response, 400, new { error = "missing_groupId" });
                    return;
                }

                var settings = AppSettingsStore.Load();
                var group = settings.QuickPanelGlobalGroups.FirstOrDefault(g => g.Id == groupId);
                if (group != null)
                {
                    group.Slots = items.Select(x => string.IsNullOrEmpty(x) ? null : x).ToList();
                    group.SlotItems = new List<QuickPanelSlotItem?>(new QuickPanelSlotItem?[group.Slots.Count]);
                    AppSettingsStore.Save(settings);
                    _onMutated();
                    await WriteJsonAsync(response, 200, new { ok = true });
                }
                else
                {
                    await WriteJsonAsync(response, 404, new { error = "group_not_found" });
                }
                return;
            }

            await WriteJsonAsync(response, 404, new { error = "not_found" });
        }
        catch (FileNotFoundException ex)
        {
            await WriteJsonAsync(response, 404, new { error = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            await WriteJsonAsync(response, 404, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            await WriteJsonAsync(response, 400, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Local Agent API request error: {ex.Message}");
            await WriteJsonAsync(response, 500, new { error = "internal_error", detail = ex.Message });
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            return true;
        }

        var bearer = request.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(bearer) &&
            bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(bearer["Bearer ".Length..].Trim(), _token, StringComparison.Ordinal))
        {
            return true;
        }

        var incoming = request.Headers["X-Yanzi-Token"];
        return string.Equals(incoming, _token, StringComparison.Ordinal);
    }

    private static WebDavConfigDto GetWebDavConfigDto()
    {
        var settings = AppSettingsStore.Load();
        var credential = WebDavCredentialStore.Load();

        return new WebDavConfigDto
        {
            Enabled = settings.EnableWebDavSync,
            ServerUrl = settings.WebDavServerUrl,
            RootPath = settings.WebDavRootPath,
            Username = settings.WebDavUsername,
            Password = string.IsNullOrWhiteSpace(credential?.Password) ? null : credential.Password
        };
    }

    private static object ToDto(CommandItem x)
    {
        return new
        {
            id = x.ExtensionId,
            title = x.Title,
            subtitle = x.Subtitle,
            category = x.Category,
            source = x.Source.ToString(),
            version = x.DeclaredVersion,
            globalShortcut = x.GlobalShortcut,
            runtime = x.Runtime,
            entry = x.EntryPoint,
            permissions = x.Permissions
        };
    }

    private static async Task<JsonElement> ReadJsonBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? GetQueryString(HttpListenerRequest request, string key)
    {
        return request.QueryString[key];
    }

    private static HashSet<string> ParseIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        response.StatusCode = statusCode;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
