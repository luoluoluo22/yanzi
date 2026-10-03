using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed record MobileAttachment(string AttachmentId, string FileName, string ContentType, long Size, string Sha256, string ExpiresAt);

public sealed partial class CloudSyncClient
{
    public async Task SyncAccountLanLinksAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var account = _session!.UserId;
        using var request = CreateRequest(HttpMethod.Post, "/v1/me/devices/lan-links", true);
        request.Content = JsonContent.Create(new { deviceId });
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (json.RootElement.GetProperty("userId").GetString() != account) throw new IOException("account_link_mismatch");
        var links = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        foreach (var link in links) YanziLanPairing.ImportAccountLink(link, account, deviceId);
        var enabled = links.Select(x => x.GetProperty("desktopDeviceId").GetString()).ToHashSet();
        foreach (var old in YanziLanPairing.List().Where(x => x.OwnerAccount == account && x.Scopes.Contains("account.owner") && !enabled.Contains(x.DeviceId)))
        {
            YanziLanPairing.Revoke(old.PairId);
            YanziPeerRegistry.Remove(old.DeviceId);
        }
    }
    public async Task<DeviceMessageRecord?> GetDeviceMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/me/mobile/messages/" + Uri.EscapeDataString(messageId), true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<DeviceMessageRecord>(response, cancellationToken);
    }
    public async Task<IReadOnlyList<PeerDeviceInfo>> ListPeerDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/me/devices", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("items").EnumerateArray().Select(x => new PeerDeviceInfo(x.GetProperty("deviceId").GetString()!,
            x.GetProperty("displayName").GetString() ?? "", x.GetProperty("platform").GetString()!,
            x.TryGetProperty("online", out var online) && online.ValueKind == JsonValueKind.True,
            x.TryGetProperty("lastSeenAt", out var seen) ? seen.GetString() : null,
            x.TryGetProperty("createdAt", out var created) ? created.GetString() : null,
            x.TryGetProperty("lastLocation", out var location) ? location.GetString() : null)).ToArray();
    }
    public async Task<JsonElement?> GetPeerDescriptorAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/me/devices", true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        foreach (var peer in json.RootElement.GetProperty("items").EnumerateArray())
            if (peer.GetProperty("deviceId").GetString() == deviceId) return peer.Clone();
        return null;
    }
    public async Task RemovePeerDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Delete, "/v1/me/devices/" + Uri.EscapeDataString(deviceId), true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }
    public async Task<ClientWebSocket> ConnectDeviceRelayAsync(string deviceId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var endpoint = new UriBuilder(new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"),
            "v1/me/mobile/messages/ws?deviceId=" + Uri.EscapeDataString(deviceId)));
        endpoint.Scheme = endpoint.Scheme == "https" ? "wss" : "ws";
        Exception? failure = null;
        foreach (var direct in new[] { true, false })
        {
            var socket = new ClientWebSocket();
            if (direct) socket.Options.Proxy = null;
            socket.Options.SetRequestHeader("Authorization", "Bearer " + _session!.AccessToken);
            socket.Options.SetRequestHeader("User-Agent", "YanziClient-Desktop/realtime");
            socket.Options.SetRequestHeader("X-Yanzi-Client", "desktop");
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(endpoint.Uri, timeout.Token);
                return socket;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { failure = ex; socket.Dispose(); }
        }
        throw new IOException("实时连接暂不可用。", failure);
    }

    public async Task<MobileAttachment> UploadMobileAttachmentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        if (info.Length <= 0 || info.Length > 30L * 1024 * 1024) throw new InvalidOperationException("附件大小需为 1 字节至 30 MB。");
        await EnsureAuthenticatedAsync(cancellationToken);
        string hash;
        using (var input = File.OpenRead(filePath)) hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
        Exception? last = null;
        foreach (var client in new[] { _directLargeTransferHttpClient, _largeTransferHttpClient })
        {
            using var request = CreateRequest(HttpMethod.Post, "/v1/me/mobile/attachments?name=" + Uri.EscapeDataString(info.Name), true);
            request.Headers.Add("X-Content-Sha256", hash);
            request.Content = new StreamContent(File.OpenRead(filePath));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(filePath));
            request.Content.Headers.ContentLength = info.Length;
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                await EnsureSuccessAsync(response, cancellationToken);
                var attachment = await ReadAsync<MobileAttachment>(response, cancellationToken) ?? throw new IOException("附件响应为空。");
                if (attachment.Size != info.Length || attachment.Sha256 != hash) throw new IOException("附件校验失败。");
                return attachment;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { last = ex; }
        }
        throw new IOException("附件上传失败，可重试。", last);
    }

    public async Task<string> DownloadMobileAttachmentAsync(string attachmentId, string directory, CancellationToken cancellationToken = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(attachmentId, "^att_[a-f0-9]{32}$")) throw new IOException("附件编号无效。");
        await EnsureAuthenticatedAsync(cancellationToken);
        using var metadataRequest = CreateRequest(HttpMethod.Get, "/v1/me/mobile/attachments/" + attachmentId, true);
        using var metadataResponse = await SendAsyncWithFallback(metadataRequest, cancellationToken);
        await EnsureSuccessAsync(metadataResponse, cancellationToken);
        var metadata = await ReadAsync<MobileAttachment>(metadataResponse, cancellationToken) ?? throw new IOException("附件不存在。");
        if (metadata.Size <= 0 || metadata.Size > 30L * 1024 * 1024) throw new IOException("附件大小超限。");
        Directory.CreateDirectory(directory);
        foreach (var old in Directory.EnumerateFiles(directory, "att_*"))
            if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(old.EndsWith(".part", StringComparison.Ordinal) ? -1 : -30)) File.Delete(old);
        var name = Path.GetFileName(metadata.FileName.Replace('\\', '/'));
        name = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        var target = Path.Combine(directory, attachmentId + "-" + name);
        var partial = target + ".part";
        if (File.Exists(target) && new FileInfo(target).Length == metadata.Size)
        {
            using var saved = File.OpenRead(target);
            if (Convert.ToHexString(await SHA256.HashDataAsync(saved, cancellationToken)).Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase)) return target;
        }
        var existingBytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existingBytes >= metadata.Size) { File.Delete(partial); existingBytes = 0; }
        if (Directory.EnumerateFiles(directory, "att_*").Sum(x => new FileInfo(x).Length) + metadata.Size - existingBytes > 300L * 1024 * 1024)
            throw new IOException("transfer_quota_exceeded");
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace < metadata.Size - existingBytes + 10L * 1024 * 1024)
            throw new IOException("storage_exhausted");
        Exception? last = null;
        foreach (var client in new[] { _directLargeTransferHttpClient, _largeTransferHttpClient })
        {
            var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset >= metadata.Size) { File.Delete(partial); offset = 0; }
            using var request = CreateRequest(HttpMethod.Get, "/v1/me/mobile/attachments/" + attachmentId + "/content", true);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await EnsureSuccessAsync(response, cancellationToken);
                if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) offset = 0;
                using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        if (output.Length + count > metadata.Size) throw new IOException("附件响应超过声明大小。");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    }
                }
                bool invalidChecksum;
                using (var input = File.OpenRead(partial))
                {
                    if (input.Length < metadata.Size) throw new IOException("附件传输中断，已保留下载进度。");
                    invalidChecksum = input.Length != metadata.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase);
                }
                if (invalidChecksum) { File.Delete(partial); throw new IOException("附件完整性校验失败。"); }
                File.Move(partial, target, true);
                return target;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { last = ex; }
        }
        throw new IOException("附件下载失败，重试时将继续下载。", last);
    }
}

public sealed record PeerDeviceInfo(string DeviceId, string DisplayName, string Platform,
    bool Online = false, string? LastSeenAt = null, string? CreatedAt = null, string? LastLocation = null);
