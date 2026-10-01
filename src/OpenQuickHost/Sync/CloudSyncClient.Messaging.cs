using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed record MobileAttachment(string AttachmentId, string FileName, string ContentType, long Size, string Sha256, string ExpiresAt);

public sealed partial class CloudSyncClient
{
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
                using (var input = File.OpenRead(partial))
                    if (input.Length != metadata.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                    { File.Delete(partial); throw new IOException("附件完整性校验失败。"); }
                File.Move(partial, target, true);
                return target;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { last = ex; }
        }
        throw new IOException("附件下载失败，重试时将继续下载。", last);
    }
}
