using System.IO;
using System.IO.Compression;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class MainWindow
{
    private static readonly object MobileMessageBridgeLock = new();
    private bool _deviceRegistered;
    private DateTimeOffset _lastDesktopPresenceHeartbeatErrorLogAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAccountLanRefreshAt = DateTimeOffset.MinValue;
    private int _desktopPresenceHeartbeatFailureCount;

    private void StartMobileMessageBridge(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            HostAssets.AppendLog($"Mobile bridge skipped: reason={reason}, hasClient={_cloudSyncClient != null}, hasCredential={_cloudSyncClient?.HasCredential == true}.");
            return;
        }

        _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        lock (MobileMessageBridgeLock)
        {
            _deviceRegistered = false;
            if (_mobileMessageBridgeTask is not { IsCompleted: false })
            {
                try
                {
                    _mobileMessageBridgeCts?.Cancel();
                }
                catch
                {
                    // Ignore cancel exceptions
                }
                _mobileMessageBridgeCts = new CancellationTokenSource();
                _mobileMessageBridgeTask = Task.Run(() => MobileMessageBridgeLoopAsync(_mobileMessageBridgeCts.Token));
            }
        }

        HostAssets.AppendLog($"Mobile bridge started: reason={reason}, deviceId={_desktopDeviceId}.");
        StartDesktopPresenceHeartbeat(reason);
        _mobileMessagePollTimer.Start();
        _ = PollMobileMessagesSafeAsync($"start-{reason}");
    }

    private void StartDesktopPresenceHeartbeat(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            return;
        }

        _desktopPresenceHeartbeatFailureCount = 0;

        if (!_desktopPresenceHeartbeatTimer.IsEnabled)
        {
            _desktopPresenceHeartbeatTimer.Start();
            HostAssets.AppendLog($"Desktop presence heartbeat started: reason={reason}.");
        }

        _ = SendDesktopPresenceHeartbeatSafeAsync($"start-{reason}");
    }

    private async Task SendDesktopPresenceHeartbeatSafeAsync(string reason)
    {
        if (_desktopPresenceHeartbeatRunning || _cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            return;
        }

        _desktopPresenceHeartbeatRunning = true;
        try
        {
            using var heartbeatTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();
            await _cloudSyncClient.RegisterDeviceAsync(
                _desktopDeviceId,
                "desktop",
                DeviceIdentityStore.GetDesktopDisplayName(),
                BuildDesktopDeviceCapabilities(),
                cancellationToken: heartbeatTimeout.Token);
            if (reason.StartsWith("start-", StringComparison.OrdinalIgnoreCase) ||
                DateTimeOffset.UtcNow - _lastAccountLanRefreshAt >= TimeSpan.FromMinutes(5))
            {
                try
                {
                    await _cloudSyncClient.SyncAccountLanLinksAsync(_desktopDeviceId, heartbeatTimeout.Token);
                    _lastAccountLanRefreshAt = DateTimeOffset.UtcNow;
                }
                catch (Exception error) { HostAssets.AppendLog("Account LAN refresh deferred: " + error.GetType().Name); }
            }
            _deviceRegistered = true;
            _desktopPresenceHeartbeatFailureCount = 0;
            _desktopPresenceHeartbeatTimer.Interval = TimeSpan.FromSeconds(60);

            if (reason.StartsWith("start-", StringComparison.OrdinalIgnoreCase))
            {
                HostAssets.AppendLog($"Desktop presence heartbeat ok: reason={reason}, deviceId={_desktopDeviceId}.");
            }
        }
        catch (Exception ex)
        {
            _desktopPresenceHeartbeatFailureCount = Math.Min(_desktopPresenceHeartbeatFailureCount + 1, 5);
            if (_desktopPresenceHeartbeatFailureCount >= 5)
            {
                _desktopPresenceHeartbeatTimer.Interval = TimeSpan.FromSeconds(120);
                HostAssets.AppendDebug("Desktop presence heartbeat retry slowed after repeated transport failures.");
            }

            if (DateTimeOffset.UtcNow - _lastDesktopPresenceHeartbeatErrorLogAt > TimeSpan.FromMinutes(1))
            {
                _lastDesktopPresenceHeartbeatErrorLogAt = DateTimeOffset.UtcNow;
                HostAssets.AppendLog($"Desktop presence heartbeat failed: reason={reason}, {FormatExceptionMessage(ex)}");
            }
        }
        finally
        {
            _desktopPresenceHeartbeatRunning = false;
        }
    }

    private static object BuildDesktopDeviceCapabilities()
    {
        return new
        {
            app = "yanzi-desktop",
            os = Environment.OSVersion.VersionString,
            receiveMobileMessages = true,
            receiveAccountChat = true,
            deviceMessageProtocolVersions = new[] { YanziDeviceMessageProtocol.Version },
            receiveLanAttachments = true,
            autoAccountLan = true,
            lanPort = AppSettingsStore.LoadCached().AgentApiPort,
            maxAttachmentBytes = 30 * 1024 * 1024,
            pushToMobile = true
        };
    }

    private async Task MobileMessageBridgeLoopAsync(CancellationToken cancellationToken)
    {
        HostAssets.AppendLog("Mobile bridge background loop started with SSE streaming.");
        _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();

        try
        {
            await PollMobileMessagesSafeAsync("sse-sync");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile bridge SSE sync error during startup: {ex.Message}");
        }

        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset? connectedAtUtc = null;
            try
            {
                try { await RunMobileWebSocketAsync(cancellationToken); consecutiveFailures = 0; continue; }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                { HostAssets.AppendDebug($"Mobile realtime unavailable; SSE fallback: {ex.GetType().Name}"); }
                HostAssets.AppendDebug("Mobile bridge establishing SSE connection to cloud...");
                using var fallbackLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                fallbackLifetime.CancelAfter(TimeSpan.FromMinutes(2));
                using var response = await _cloudSyncClient!.GetMobileMessagesEventsStreamAsync(_desktopDeviceId, fallbackLifetime.Token);
                using var stream = await response.Content.ReadAsStreamAsync(fallbackLifetime.Token);
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                connectedAtUtc = DateTimeOffset.UtcNow;
                HostAssets.AppendDebug("Mobile bridge SSE connection established successfully.");

                while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(fallbackLifetime.Token);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var dataJson = line["data:".Length..].Trim();
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(dataJson);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "messages")
                            {
                                if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    var itemsText = itemsProp.GetRawText();
                                    var messages = System.Text.Json.JsonSerializer.Deserialize<List<DeviceMessageRecord>>(itemsText, new System.Text.Json.JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive = true
                                    });

                                    if (messages != null && messages.Count > 0)
                                    {
                                        HostAssets.AppendLog($"Mobile bridge SSE pushed messages: count={messages.Count}.");

                                        foreach (var message in messages)
                                        {
                                            var res = await HandleMobileDeviceMessageAsync(message);
                                            if (res.hasResult)
                                            {
                                                await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, res.success, res.output, cancellationToken);
                                            }
                                            else
                                            {
                                                await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, cancellationToken: cancellationToken);
                                            }
                                            HostAssets.AppendLog($"Mobile bridge SSE acked message: id={message.MessageId}, deviceId={_desktopDeviceId}.");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            HostAssets.AppendLog($"Mobile bridge SSE message parse failed: {parseEx.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                HostAssets.AppendDebug("Mobile SSE compatibility interval ended; retrying realtime connection.");
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var stableConnection = connectedAtUtc.HasValue &&
                    DateTimeOffset.UtcNow - connectedAtUtc.Value >= TimeSpan.FromMinutes(1);
                var message = $"Mobile bridge SSE connection error: {FormatExceptionMessage(ex)}";
                if (stableConnection && message.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase))
                {
                    HostAssets.AppendDebug(message);
                }
                else
                {
                    HostAssets.AppendLog(message);
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var stableConnection = connectedAtUtc.HasValue &&
                    DateTimeOffset.UtcNow - connectedAtUtc.Value >= TimeSpan.FromMinutes(1);
                if (stableConnection)
                {
                    consecutiveFailures = 0;
                }

                consecutiveFailures++;
                if (consecutiveFailures >= 5)
                {
                    consecutiveFailures = 5;
                }

                var retrySeconds = Math.Min(5 * (1 << (consecutiveFailures - 1)), 60);
                var retryMessage = $"Mobile bridge SSE disconnected. Retrying in {retrySeconds} seconds ({consecutiveFailures}/5)...";
                if (stableConnection) HostAssets.AppendDebug(retryMessage);
                else HostAssets.AppendLog(retryMessage);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(retrySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        HostAssets.AppendLog("Mobile bridge background loop stopped.");
    }

    private async void MobileMessagePollTimer_Tick(object? sender, EventArgs e)
    {
        await PollMobileMessagesSafeAsync("timer");
    }

    private async Task<int> PollMobileMessagesSafeAsync(string reason)
    {
        if (_mobileMessagePollRunning || _cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            if (_mobileMessagePollRunning)
            {
                HostAssets.AppendLog($"Mobile bridge poll skipped: reason={reason}, previous poll still running.");
            }
            return 0;
        }

        _mobileMessagePollRunning = true;
        try
        {
            using var pollTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();

            if (!_deviceRegistered)
            {
                await _cloudSyncClient.RegisterDeviceAsync(
                    _desktopDeviceId,
                    "desktop",
                    DeviceIdentityStore.GetDesktopDisplayName(),
                    BuildDesktopDeviceCapabilities(),
                    cancellationToken: pollTimeout.Token);
                _deviceRegistered = true;
            }

            var replayClient = _cloudSyncClient;
            var replayToken = _mobileMessageBridgeCts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () => {
                try { await replayClient.ReplayDeviceOutboxAsync(replayToken); }
                catch (Exception error) { HostAssets.AppendLog("Device outbox retry deferred: " + error.GetType().Name); }
            });
            var accountId = _cloudSyncClient.CurrentUserId ?? throw new InvalidOperationException("Cloud account is not authenticated.");
            var cursor = DeviceMessageCursorStore.Read(accountId, _desktopDeviceId);
            var total = 0;
            for (var pageIndex = 0; pageIndex < 20; pageIndex++)
            {
                var page = await _cloudSyncClient.GetPendingDeviceMessagePageAsync(
                    _desktopDeviceId, cursor, limit: 20, cancellationToken: pollTimeout.Token);
                if (page.Items.Count > 0)
                {
                    HostAssets.AppendLog($"Mobile bridge received messages: reason={reason}, count={page.Items.Count}, cursor={cursor}->{page.NextCursor}.");
                }
                else if (DateTimeOffset.UtcNow - _lastMobileMessageEmptyLogAt > TimeSpan.FromMinutes(1))
                {
                    _lastMobileMessageEmptyLogAt = DateTimeOffset.UtcNow;
                    HostAssets.AppendLog($"Mobile bridge poll ok: reason={reason}, count=0, cursor={cursor}->{page.NextCursor}, deviceId={_desktopDeviceId}.");
                }

                foreach (var message in page.Items)
                {
                    var res = await HandleMobileDeviceMessageAsync(message);
                    if (res.hasResult)
                    {
                        await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, res.success, res.output, cancellationToken: pollTimeout.Token);
                    }
                    else
                    {
                        await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, cancellationToken: pollTimeout.Token);
                    }
                    total++;
                    HostAssets.AppendLog($"Mobile bridge acked message: id={message.MessageId}, deviceId={_desktopDeviceId}.");
                }

                if (page.NextCursor != cursor)
                {
                    cursor = page.NextCursor;
                    DeviceMessageCursorStore.Write(accountId, _desktopDeviceId, cursor);
                }
                if (!page.HasMore) break;
                if (page.Items.Count == 0) throw new InvalidDataException("Message cursor page reported hasMore without items.");
            }
            return total;
        }
        catch (OperationCanceledException ex)
        {
            HostAssets.AppendLog($"Mobile bridge poll timed out: reason={reason}, {FormatExceptionMessage(ex)}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile bridge poll failed: reason={reason}, {FormatExceptionMessage(ex)}");
        }
        finally
        {
            _mobileMessagePollRunning = false;
        }
        return 0;
    }

    private readonly SemaphoreSlim _mobileMessageExecutionLock = new(1, 1);
    private readonly Dictionary<string, (bool hasResult, bool success, string output)> _mobileMessageResults = new();

    internal async Task<(bool hasResult, bool success, string output)> HandleMobileDeviceMessageAsync(DeviceMessageRecord message)
    {
        await _mobileMessageExecutionLock.WaitAsync();
        try
        {
            using var trace = YanziOperationTrace.Push(message.TraceId);
            var stableId = GetPayloadString(message, "clientOperationId");
            if (string.IsNullOrEmpty(stableId) && message.Kind is "photo" or "file")
                stableId = GetPayloadString(message, "clientTransferId");
            var key = $"{SyncSessionStore.Load()?.UserId}:{message.SourceDeviceId}:{(Guid.TryParse(stableId, out _) ? stableId : message.MessageId)}";
            if (_mobileMessageResults.TryGetValue(key, out var cached)) return cached;
            var command = YanziDeviceMessageProtocol.IsExecution(message.Kind);
            var receiptPath = command || Guid.TryParse(stableId, out _) ? GetMobileExecutionReceiptPath(key) : null;
            if (receiptPath != null && File.Exists(receiptPath)) {
                var saved = JsonSerializer.Deserialize<MobileExecutionReceipt>(File.ReadAllText(receiptPath))!;
                return (true, saved.Completed && saved.Success, saved.Completed ? saved.Output : "execution_result_unknown: 上次执行被中断，未自动重复执行。请确认结果后重新发起。");
            }
            if (command)
            {
                var created = DateTimeOffset.TryParse(message.CreatedAt, out var timestamp) ? timestamp :
                    long.TryParse(message.CreatedAt, out var millis) ? DateTimeOffset.FromUnixTimeMilliseconds(millis) : DateTimeOffset.MinValue;
                var deadline = DateTimeOffset.TryParse(message.ExpiresAt, out var supplied) ? supplied : created.AddMinutes(2);
                if (deadline <= DateTimeOffset.UtcNow || message.Status is "cancelled" or "expired")
                    return (true, false, "message_expired: 执行期限已过或请求已取消，未执行。");
                // Only upgraded cloud envelopes have claims. Legacy server and LAN retain local durable deduplication.
                if (message.MessageId.StartsWith("msg_", StringComparison.Ordinal) && message.Payload.ContainsKey("messageContext"))
                {
                    if (_cloudSyncClient == null || !await _cloudSyncClient.ClaimDeviceMessageAsync(message.MessageId,
                        DeviceIdentityStore.GetOrCreateDesktopDeviceId()))
                        throw new InvalidOperationException("execution_result_unknown: 未取得执行权，禁止自动重做。");
                }
            }
            if (receiptPath != null) SaveMobileExecutionReceipt(receiptPath, new(false, false, ""));
            var result = await ExecuteMobileDeviceMessageAsync(message);
            if (receiptPath != null) SaveMobileExecutionReceipt(receiptPath, new(true, result.success, result.output));
            if (_mobileMessageResults.Count >= 2048) _mobileMessageResults.Remove(_mobileMessageResults.Keys.First());
            _mobileMessageResults[key] = result;
            return result;
        }
        finally { _mobileMessageExecutionLock.Release(); }
    }

    private async Task<(bool hasResult, bool success, string output)> ExecuteMobileDeviceMessageAsync(DeviceMessageRecord message)
    {
        if (message.Kind == "extension.handoff")
        {
            if (GetPayloadString(message, "accountId") != SyncSessionStore.Load()?.UserId)
                return (true, false, "permission_denied: 接续打开仅允许同账号设备");
            if (message.Payload.TryGetValue("authorization", out var grant))
            {
                if (grant.GetProperty("type").GetString() != "account-owner") return (true, false, "permission_denied: 接续打开仅允许账号所有者");
            }
            else
            {
                // Legacy cloud does not add grants. Verify ownership through authenticated /me message lookup.
                var verified = _cloudSyncClient == null ? null : await _cloudSyncClient.GetDeviceMessageAsync(message.MessageId);
                if (verified == null || verified.Kind != "extension.handoff" || verified.SourceDeviceId != message.SourceDeviceId ||
                    verified.TargetDeviceId != DeviceIdentityStore.GetOrCreateDesktopDeviceId() ||
                    GetPayloadString(verified, "accountId") != SyncSessionStore.Load()?.UserId ||
                    GetPayloadString(verified, "extensionId") != GetPayloadString(message, "extensionId") ||
                    GetPayloadString(verified, "input") != GetPayloadString(message, "input"))
                    return (true, false, "permission_denied: 未能确认请求属于当前账号");
            }
            if (DateTimeOffset.TryParse(message.ExpiresAt, out var expires) && expires <= DateTimeOffset.UtcNow)
                return (true, false, "请求已过期");
            var extensionId = GetPayloadString(message, "extensionId") ?? "";
            var input = GetPayloadString(message, "input") ?? "";
            if (input.Length > 4096 || !_localExtensionIndex.TryGetValue(extensionId, out var command) || command.App == null || !IsExtensionEnabled(extensionId) || !command.App.BridgeApis.Contains("handoff"))
                return (true, false, "目标电脑未安装或未启用支持接续的小程序");
            try { var output = await (await Dispatcher.InvokeAsync(() => AppExtensionWindow.OpenResourceAsync(command, input))); return (true, true, output); }
            catch (Exception error) { return (true, false, error.Message); }
        }
        if (message.Kind == "extension-storage.changed")
        {
            var extensionId = GetPayloadString(message, "extensionId");
            var key = GetPayloadString(message, "key");
            if (string.IsNullOrWhiteSpace(extensionId) || string.IsNullOrWhiteSpace(key))
                return (true, false, "invalid_storage_hint");
            await Dispatcher.InvokeAsync(() => AppExtensionWindow.NotifyStorageChanged(extensionId, key));
            return (true, true, "storage_hint_delivered");
        }
        var title = string.IsNullOrWhiteSpace(message.Title) ? "手机发来消息" : message.Title.Trim();
        var text = string.IsNullOrWhiteSpace(message.Text) ? $"消息类型：{message.Kind}" : message.Text.Trim();
        var sourceLabel = GetMobileSourceLabel(message);
        var screenshotDataUrl = GetPayloadString(message, "screenshotDataUrl");
        var mobileAttachmentFilePath = await TryDownloadMobileScreenshotFromWebDavAsync(message);

        var screenshotFilePath = mobileAttachmentFilePath;
        if (string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var payloadKeys = message.Payload.Count == 0
                ? "(empty)"
                : string.Join(",", message.Payload.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
            HostAssets.AppendLog(
                $"Mobile screenshot payload: id={message.MessageId}, keys={payloadKeys}, hasDataUrl={!string.IsNullOrWhiteSpace(screenshotDataUrl)}, webDavPath={GetPayloadString(message, "webDavPath") ?? "(none)"}, localFile={mobileAttachmentFilePath ?? "(none)"}.");
        }
        HostAssets.AppendLog(
            $"Mobile bridge message: id={message.MessageId}, trace={YanziOperationTrace.Current}, source={sourceLabel}, kind={message.Kind}, text={trimForLog(text)}");

        if (message.Kind == "capability.invoke")
        {
            var name = GetPayloadString(message, "name") ?? "";
            if (!YanziCapabilityRegistry.TryGet(name, out var definition) || definition == null)
                return (true, false, "capability_not_found");
            if (!message.Payload.TryGetValue("authorization", out var authorization)) return (true, false, "permission_denied");
            var type = authorization.GetProperty("type").GetString();
            if (type != "account-owner" && !(type == "device-grant" && authorization.GetProperty("scopes").EnumerateArray().Any(x => x.GetString() == "capability.invoke:" + name)) &&
                !(type == "lan-pair" && authorization.GetProperty("capability").GetString() == name)) return (true, false, "permission_denied");
            var result = await YanziCapabilityInvocationService.InvokeAsync(name,
                message.Payload.TryGetValue("payload", out var input) ? input.Clone() : null,
                new YanziCapabilityCaller("device:" + message.SourceDeviceId, definition.Permissions));
            return (true, result.Success, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        if (string.Equals(message.Kind, "run-extension", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Payload.TryGetValue("extensionId", out var extensionElement))
            {
                var extensionId = extensionElement.ValueKind == JsonValueKind.String
                    ? extensionElement.GetString()
                    : extensionElement.ToString();
                if (!string.IsNullOrWhiteSpace(extensionId) && _localExtensionIndex.TryGetValue(extensionId, out var command))
                {
                    var execResult = await RunMobileExtensionAsync(command, text);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LastRunMessage = execResult.success ? $"已执行手机端请求：{command.Title}" : $"执行手机端请求失败：{command.Title}";
                        SyncStatus = execResult.success ? "手机端请求执行成功。" : $"手机端请求执行失败：{execResult.output}";
                    });
                    return (true, execResult.success, execResult.output);
                }
                else
                {
                    return (true, false, $"手机请求的扩展不存在：{extensionId}");
                }
            }
            return (true, false, "缺少 extensionId 参数");
        }

        if (string.Equals(message.Kind, "run-powershell", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Kind, "run-shell", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = text;
            if (string.IsNullOrWhiteSpace(cmd) && message.Payload.TryGetValue("command", out var cmdElem))
            {
                cmd = cmdElem.ValueKind == JsonValueKind.String ? cmdElem.GetString() : cmdElem.ToString();
            }

            var execResult = await ExecuteMobilePowerShellCommandAsync(cmd ?? string.Empty);
            var responsePayload = JsonSerializer.Serialize(new
            {
                output = execResult.output,
                exitCode = execResult.exitCode
            });
            return (true, execResult.success, responsePayload);
        }

        if (string.Equals(message.Kind, "fs-list", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Kind, "fs-read", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message.Kind, "fs-write", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Payload.TryGetValue("authorization", out var fileAuthorization) && fileAuthorization.GetProperty("type").GetString() == "device-grant")
            {
                var roots = fileAuthorization.TryGetProperty("fileRoots", out var rootArray) ? rootArray.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];
                if (!YanziDeviceResourceAuthorization.AllowsFile(GetPayloadString(message, "path") ?? "", roots))
                    return (true, false, "permission_denied: 目标文件或目录链接越出授权目录。");
            }
            var jsonPayload = JsonSerializer.Serialize(message.Payload);
            using var doc = JsonDocument.Parse(jsonPayload);
            var fsResult = ExecuteMobileFsOperation(message.Kind.ToLowerInvariant(), doc.RootElement);
            return (true, fsResult.success, fsResult.jsonOutput);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            LastRunMessage = $"{title}：{text}";
            var clipboardMessage = CopyMobileMessageToClipboard(message, text, screenshotDataUrl, mobileAttachmentFilePath);

            SyncStatus = string.IsNullOrWhiteSpace(clipboardMessage)

                ? "已收到手机端消息。"

                : $"已收到手机端消息，{clipboardMessage}。";
            SaveMobileInboxMessage(message, title, text, sourceLabel, screenshotDataUrl, screenshotFilePath);
            ShowMobileMessageToast(title, text, sourceLabel, screenshotDataUrl, screenshotFilePath, message.SourceDeviceId);
        });

        return (false, true, string.Empty);
    }

    private static async Task<(bool success, string output, int exitCode)> ExecuteMobilePowerShellCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return (false, "命令不能为空。", 1);
        }

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            var prependedCommand = "$ProgressPreference = 'SilentlyContinue';\r\n" + command;
            var bytes = Encoding.Unicode.GetBytes(prependedCommand);
            var base64 = Convert.ToBase64String(bytes);

            process.StartInfo.Arguments = $"-NoProfile -NonInteractive -EncodedCommand {base64}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                await process.WaitForExitAsync();
                return (false, "命令执行超时（15 秒），进程已停止。", -1);
            }

            var outText = outputBuilder.ToString().Trim();
            var errText = errorBuilder.ToString().Trim();

            var combined = string.IsNullOrWhiteSpace(errText)
                ? outText
                : (string.IsNullOrWhiteSpace(outText) ? errText : $"{outText}\n{errText}");

            return (process.ExitCode == 0, combined, process.ExitCode);
        }
        catch (Exception ex)
        {
            return (false, $"执行 PowerShell 命令异常: {ex.Message}", 1);
        }
    }

    private static string ResolveFsPath(string? path)
    {
        return PathHelper.ResolveFsPath(path);
    }

    private static (bool success, string jsonOutput) ExecuteMobileFsOperation(string kind, JsonElement payload)
    {
        try
        {
            if (kind == "fs-list")
            {
                var rawPath = payload.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                var path = ResolveFsPath(rawPath);

                if (!Directory.Exists(path) && !File.Exists(path))
                {
                    return (false, JsonSerializer.Serialize(new { error = $"路径不存在: {path}" }));
                }

                var items = new List<object>();
                if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    foreach (var dir in dirInfo.GetDirectories())
                    {
                        items.Add(new { name = dir.Name, isDir = true, size = 0L, lastModified = new DateTimeOffset(dir.LastWriteTimeUtc).ToUnixTimeMilliseconds() });
                    }
                    foreach (var file in dirInfo.GetFiles())
                    {
                        items.Add(new { name = file.Name, isDir = false, size = file.Length, lastModified = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds() });
                    }
                }

                return (true, JsonSerializer.Serialize(new { ok = true, currentPath = path, items }));
            }
            else if (kind == "fs-read")
            {
                var path = ResolveFsPath(payload.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty);
                if (!File.Exists(path))
                {
                    return (false, JsonSerializer.Serialize(new { error = $"文件不存在: {path}" }));
                }
                if (new FileInfo(path).Length > 10 * 1024 * 1024)
                    return (false, JsonSerializer.Serialize(new { error = "文件过大，读取上限为 10 MB" }));
                var ext = Path.GetExtension(path).ToLowerInvariant();
                bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp" || ext == ".bmp" || ext == ".ico";
                if (isImage)
                {
                    var bytes = File.ReadAllBytes(path);
                    var base64 = Convert.ToBase64String(bytes);
                    return (true, JsonSerializer.Serialize(new { ok = true, path, content = base64, isBase64 = true, ext }));
                }
                var content = File.ReadAllText(path);
                return (true, JsonSerializer.Serialize(new { ok = true, path, content, isBase64 = false, ext }));
            }
            else if (kind == "fs-write")
            {
                var path = ResolveFsPath(payload.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty);
                var content = payload.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return (false, JsonSerializer.Serialize(new { error = "路径不能为空" }));
                }
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (payload.TryGetProperty("base64", out var encoded) && encoded.ValueKind == JsonValueKind.True)
                    File.WriteAllBytes(path, Convert.FromBase64String(content));
                else File.WriteAllText(path, content);
                return (true, JsonSerializer.Serialize(new { path, ok = true }));
            }
        }
        catch (Exception ex)
        {
            return (false, JsonSerializer.Serialize(new { error = ex.Message }));
        }

        return (false, JsonSerializer.Serialize(new { error = "未知文件操作" }));
    }

    private static string CopyMobileMessageToClipboard(DeviceMessageRecord message, string text, string? screenshotDataUrl, string? localFilePath)

    {

        try

        {

            if (IsMobileScreenshotMessage(message))

            {

                var bitmap = TryCreateClipboardBitmap(screenshotDataUrl, localFilePath);

                if (bitmap != null)

                {

                    var dataObject = new System.Windows.DataObject();

                    dataObject.SetImage(bitmap);

                    ClipboardService.SetDataObject(dataObject, true);

                    HostAssets.AppendLog($"Mobile bridge clipboard copied image: id={message.MessageId}.");

                    return "已复制图片到剪贴板";

                }

            }



            var filePaths = ResolveMobileClipboardFilePaths(message, localFilePath);

            if (filePaths.Count > 0)

            {

                CopyFileDropListToClipboard(filePaths);

                HostAssets.AppendLog($"Mobile bridge clipboard copied files: id={message.MessageId}, count={filePaths.Count}.");

                return filePaths.Count == 1 ? "已复制文件到剪贴板" : $"已复制 {filePaths.Count} 个文件到剪贴板";

            }



            if (!string.IsNullOrWhiteSpace(text))

            {

                ClipboardService.SetText(text);

                HostAssets.AppendLog($"Mobile bridge clipboard copied text: id={message.MessageId}, length={text.Length}.");

                return "已复制文本到剪贴板";

            }

        }

        catch (Exception ex)

        {

            HostAssets.AppendLog($"Mobile bridge clipboard copy failed: id={message.MessageId}, {FormatExceptionMessage(ex)}");

            return "剪贴板写入失败";

        }



        return string.Empty;

    }



    private static BitmapSource? TryCreateClipboardBitmap(string? dataUrl, string? localFilePath)

    {

        byte[] bytes;

        if (!string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath))

        {

            bytes = File.ReadAllBytes(localFilePath);

        }

        else if (!TryDecodeDataUrl(dataUrl, out bytes))

        {

            return null;

        }



        var bitmap = new BitmapImage();

        bitmap.BeginInit();

        bitmap.CacheOption = BitmapCacheOption.OnLoad;

        bitmap.StreamSource = new MemoryStream(bytes);

        bitmap.EndInit();

        bitmap.Freeze();

        return bitmap;

    }



    private static bool TryDecodeDataUrl(string? dataUrl, out byte[] bytes)

    {

        bytes = [];

        if (string.IsNullOrWhiteSpace(dataUrl))

        {

            return false;

        }



        const string marker = "base64,";

        var index = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index < 0)

        {

            return false;

        }



        bytes = Convert.FromBase64String(dataUrl[(index + marker.Length)..]);

        return bytes.Length > 0;

    }



    private static IReadOnlyList<string> ResolveMobileClipboardFilePaths(DeviceMessageRecord message, string? localFilePath)

    {

        var paths = new List<string>();

        AddClipboardFilePath(paths, localFilePath);



        foreach (var key in new[] { "localFilePath", "filePath", "path", "downloadedFilePath", "attachmentPath" })

        {

            AddClipboardFilePath(paths, GetPayloadString(message, key));

        }



        foreach (var key in new[] { "localFilePaths", "filePaths", "paths", "attachments", "files" })

        {

            AddPayloadFilePaths(paths, message, key);

        }



        return paths

            .Where(static path => File.Exists(path) || Directory.Exists(path))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToArray();

    }



    private static void AddPayloadFilePaths(List<string> paths, DeviceMessageRecord message, string key)

    {

        if (!message.Payload.TryGetValue(key, out var element))

        {

            return;

        }



        if (element.ValueKind == JsonValueKind.String)

        {

            AddClipboardFilePath(paths, element.GetString());

            return;

        }



        if (element.ValueKind == JsonValueKind.Array)

        {

            foreach (var item in element.EnumerateArray())

            {

                if (item.ValueKind == JsonValueKind.String)

                {

                    AddClipboardFilePath(paths, item.GetString());

                }

                else if (item.ValueKind == JsonValueKind.Object)

                {

                    AddClipboardFilePath(paths, ReadPayloadObjectString(item, "localFilePath"));

                    AddClipboardFilePath(paths, ReadPayloadObjectString(item, "filePath"));

                    AddClipboardFilePath(paths, ReadPayloadObjectString(item, "path"));

                }

            }

            return;

        }



        if (element.ValueKind == JsonValueKind.Object)

        {

            AddClipboardFilePath(paths, ReadPayloadObjectString(element, "localFilePath"));

            AddClipboardFilePath(paths, ReadPayloadObjectString(element, "filePath"));

            AddClipboardFilePath(paths, ReadPayloadObjectString(element, "path"));

        }

    }



    private static string? ReadPayloadObjectString(JsonElement element, string key)

    {

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value))

        {

            return null;

        }



        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();

    }



    private static void AddClipboardFilePath(List<string> paths, string? path)

    {

        if (string.IsNullOrWhiteSpace(path))

        {

            return;

        }



        var normalized = path.Trim().Trim('"');

        if (!string.IsNullOrWhiteSpace(normalized))

        {

            paths.Add(normalized);

        }

    }



    private static void CopyFileDropListToClipboard(IReadOnlyList<string> filePaths)

    {

        var files = new StringCollection();

        foreach (var filePath in filePaths)

        {

            files.Add(filePath);

        }



        var dataObject = new System.Windows.DataObject();

        dataObject.SetFileDropList(files);

        using var stream = new MemoryStream(new byte[] { 5, 0, 0, 0 });

        dataObject.SetData("Preferred DropEffect", stream);

        ClipboardService.SetDataObject(dataObject, true);

    }



    private static bool IsMobileScreenshotMessage(DeviceMessageRecord message)

    {

        return string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(message.Kind, "photo", StringComparison.OrdinalIgnoreCase);

    }



    private async Task<(bool success, string output)> RunMobileExtensionAsync(CommandItem runnable, string inputText)
    {
        var hasExternalInput = !string.IsNullOrWhiteSpace(inputText);
        var command = ResolveRunnableCommand(runnable);

        if (command.App != null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (AppExtensionWindow.TryActivateExisting(command))
                {
                    return;
                }
                var window = new AppExtensionWindow(command, inputText, "mobile")
                {
                    ShowInTaskbar = true
                };
                window.Show();
            });
            return (true, "已成功在电脑端打开应用扩展。");
        }

        if (command.HostedView != null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                ShowPanel();
                OpenHostedView(command, inputText);
            });
            return (true, "已成功在电脑端打开视图界面。");
        }

        if (command.SupportsQueryArgument == false && IsInternalCommand(command))
        {
            var internalSuccess = false;
            await Dispatcher.InvokeAsync(() =>
            {
                internalSuccess = HandleInternalCommand(command);
            });
            return (internalSuccess, internalSuccess ? "已成功执行内置指令。" : "执行内置指令失败。");
        }

        if (ScriptExtensionRunner.CanExecute(command))
        {
            var result = await ScriptExtensionRunner.ExecuteAsync(command, inputText, "mobile");
            if (result.Success)
            {
                return (true, string.IsNullOrWhiteSpace(result.Output) ? "执行成功，无输出。" : result.Output);
            }
            else
            {
                return (false, string.IsNullOrWhiteSpace(result.Error) ? $"执行失败，退出代码: {result.ExitCode}" : result.Error);
            }
        }

        var executionTarget = BuildExecutionTarget(command, inputText, allowRawQuery: hasExternalInput);
        if (executionTarget is { Length: > 0 })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executionTarget,
                    Arguments = command.LaunchArguments ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? string.Empty : command.WorkingDirectory,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return (true, $"已运行命令：{command.Title}");
            }
            catch (Exception ex)
            {
                return (false, $"无法启动程序：{ex.Message}");
            }
        }

        return (false, "不支持的扩展执行方式。");
    }

    private async Task<string?> TryDownloadMobileScreenshotFromWebDavAsync(DeviceMessageRecord message)
    {
        var lanAttachmentId = GetPayloadString(message, "lanAttachmentId");
        if (!string.IsNullOrEmpty(lanAttachmentId)) return YanziLanTransferStore.Resolve(lanAttachmentId);
        var attachmentId = GetPayloadString(message, "attachmentId");
        if (!string.IsNullOrEmpty(attachmentId))
            return await _cloudSyncClient!.DownloadMobileAttachmentAsync(attachmentId, HostAssets.ResolveDataDirectoryPath("mobile-attachments"));
        var remotePath = GetPayloadString(message, "webDavPath");
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            if (string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase))
            {
                HostAssets.AppendLog(
                    $"Mobile screenshot WebDAV skipped: payload has no webDavPath, hasDataUrl={!string.IsNullOrWhiteSpace(GetPayloadString(message, "screenshotDataUrl"))}.");
            }
            return null;
        }

        try
        {
            var settings = AppSettingsStore.Load();
            var service = new WebDavSyncService(settings);
            var credential = WebDavCredentialStore.Load();
            bool hasCredentials = Uri.TryCreate(settings.WebDavServerUrl, UriKind.Absolute, out _) &&
                                 !string.IsNullOrWhiteSpace(settings.WebDavUsername) &&
                                 !string.IsNullOrWhiteSpace(credential?.Password);
            if (!hasCredentials)
            {
                HostAssets.AppendLog($"Mobile screenshot WebDAV skipped: not configured, path={remotePath}.");
                return null;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var bytes = await service.TryReadTemporaryFileAsync(remotePath, timeout.Token);
            if (bytes is not { Length: > 0 })
            {
                HostAssets.AppendLog($"Mobile screenshot WebDAV missing: path={remotePath}.");
                return null;
            }

            var filePath = BuildMobileAttachmentDownloadPath(message, remotePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, bytes, timeout.Token);
            HostAssets.AppendLog($"Mobile attachment WebDAV downloaded: kind={message.Kind}, path={remotePath}, local={filePath}, bytes={bytes.Length}.");
            return filePath;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile screenshot WebDAV download failed: path={remotePath}, {FormatExceptionMessage(ex)}");
            return null;
        }
    }

    private static string BuildMobileAttachmentDownloadPath(DeviceMessageRecord message, string remotePath)

    {

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        var fileName = FirstNonEmpty(

            GetPayloadString(message, "fileName"),

            GetPayloadString(message, "name"),

            Path.GetFileName(remotePath.Replace('/', Path.DirectorySeparatorChar)));



        if (string.IsNullOrWhiteSpace(fileName))

        {

            fileName = IsMobileScreenshotMessage(message)

                ? $"yanzi-mobile-screenshot-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.jpg"

                : $"yanzi-mobile-file-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}";

        }



        fileName = SanitizeFileName(fileName);

        var path = Path.Combine(downloads, fileName);

        if (!File.Exists(path) && !Directory.Exists(path))

        {

            return path;

        }



        var stem = Path.GetFileNameWithoutExtension(fileName);

        var extension = Path.GetExtension(fileName);

        return Path.Combine(downloads, $"{stem}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}{extension}");

    }



    private static string SanitizeFileName(string fileName)

    {

        var invalidChars = Path.GetInvalidFileNameChars();

        var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? $"yanzi-mobile-file-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}" : sanitized;

    }



    private static string? GetPayloadString(DeviceMessageRecord message, string key)
    {
        if (!message.Payload.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    private static string GetMobileSourceLabel(DeviceMessageRecord message)
    {
        var label = FirstNonEmpty(
            message.SourceDeviceDisplayName,
            message.SourceDeviceName,
            GetPayloadString(message, "sourceDeviceDisplayName"),
            GetPayloadString(message, "sourceDeviceName"),
            GetPayloadString(message, "deviceName"),
            GetPayloadString(message, "displayName"));
        return MobileDeviceNameNormalizer.Normalize(label, message.SourceDeviceId);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static void SaveMobileInboxMessage(DeviceMessageRecord message, string title, string text, string sourceLabel, string? screenshotDataUrl, string? screenshotFilePath)
    {
        try
        {
            var record = new
            {
                messageId = message.MessageId,
                sourceDeviceId = message.SourceDeviceId,
                sourceDeviceName = sourceLabel,
                kind = message.Kind,
                title,
                text,
                payload = message.Payload,
                screenshotDataUrl = string.IsNullOrWhiteSpace(screenshotFilePath) ? screenshotDataUrl : null,
                localFilePath = screenshotFilePath,
                receivedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                createdAt = message.CreatedAt
            };
            File.AppendAllText(
                HostAssets.MobileInboxPath,
                JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox save failed: id={message.MessageId}, {FormatExceptionMessage(ex)}");
        }
    }

    public void ShowMobileInboxWindow()
    {
        try
        {
            if (_mobileMessageToastWindow is { IsVisible: true })
            {
                _mobileMessageToastWindow.LoadInboxHistory();
                _mobileMessageToastWindow.Activate();
                return;
            }

            _mobileMessageToastWindow = new MobileMessageToastWindow();
            _mobileMessageToastWindow.Closed += (_, _) => _mobileMessageToastWindow = null;
            _mobileMessageToastWindow.ShowActivated = true;
            _mobileMessageToastWindow.Show();
            _mobileMessageToastWindow.Activate();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox window failed: {FormatExceptionMessage(ex)}");
        }
    }

    private void ShowMobileMessageToast(string title, string text, string sourceDeviceId, string? screenshotDataUrl = null, string? screenshotFilePath = null, string? replyDeviceId = null)
    {
        try
        {
            var sourceLabel = MobileDeviceNameNormalizer.Normalize(sourceDeviceId);
            if (_mobileMessageToastWindow is { IsVisible: true })
            {
                _mobileMessageToastWindow.AppendMessage(title, text, sourceDeviceId, DateTimeOffset.Now, screenshotDataUrl, screenshotFilePath, replyDeviceId);

                if (!_mobileMessageToastWindow.IsActive)
                {
                    QuickPanelWindow.HasUnreadMessages = true;
                }
                return;
            }

            QuickPanelWindow.HasUnreadMessages = true;

            var notificationCard = new MobileMessageNotificationCard(sourceLabel, text);
            notificationCard.Show();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile message toast failed: {FormatExceptionMessage(ex)}");
        }
    }



    private static string trimForLog(string value)
    {
        var text = value.ReplaceLineEndings(" ").Trim();
        return text.Length <= 160 ? text : $"{text[..160]}...";
    }

}
