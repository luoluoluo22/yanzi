using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Yanzi.Core.TemporaryTransfer;

namespace OpenQuickHost;

/// <summary>
/// Thin control-plane adapter. This assembly does NOT own EasyTier, HTTP file
/// listeners, or durable downloads. They belong to the independent worker.
/// </summary>
internal static class YanziTransferServiceBridge
{
    private static readonly HttpClient Local = new(new HttpClientHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YanziTransferService");
    private static string TokenFile => Path.Combine(Root, "control.token");
    private static string ConfigFile => Path.Combine(Root, "config.json");

    internal static bool Installed => File.Exists(TokenFile) && File.Exists(ConfigFile);

    internal static async Task<JsonElement> CallAsync(
        HttpMethod method, string route, object? body = null,
        CancellationToken cancellationToken = default)
    {
        if (!Installed) throw new IOException("independent_transfer_service_not_installed");
        if (route.Length > 160 || !route.StartsWith("/v1/", StringComparison.Ordinal))
            throw new ArgumentException("invalid_service_route");
        var token = (await File.ReadAllTextAsync(TokenFile, cancellationToken)).Trim();
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new IOException("independent_service_control_token_invalid");
        using var request = new HttpRequestMessage(method, "http://127.0.0.1:39422" + route);
        request.Headers.TryAddWithoutValidation("x-yanzi-service-token", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await Local.SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (!response.IsSuccessStatusCode ||
            !root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var code = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString() : "unavailable";
            throw new IOException("independent_transfer_" + code);
        }
        return root.Clone();
    }

    internal static async Task<object?> ConnectAsync() =>
        (await CallAsync(HttpMethod.Post, "/v1/connect")).Deserialize<object>();

    internal static async Task<object?> StopAsync() =>
        (await CallAsync(HttpMethod.Post, "/v1/tunnel/stop")).Deserialize<object>();

    internal static async Task<object?> StatusAsync()
    {
        try
        {
            var data = await CallAsync(HttpMethod.Get, "/v1/status");
            var tunnel = data.GetProperty("tunnel");
            var direct = tunnel.GetProperty("direct").GetBoolean();
            return new
            {
                managed = tunnel.GetProperty("managed").GetBoolean(),
                connected = direct,
                transport = direct ? "p2p/udp" : "unavailable",
                worker = "yanzi-transfer-service",
                independentFromHost = true,
                channelEnabled = true,
                zeroRelayFallback = true,
                activeJobs = data.GetProperty("jobs").GetInt32()
            };
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException)
        {
            return new { managed = false, connected = false, transport = "unavailable",
                worker = "yanzi-transfer-service", independentFromHost = true,
                workerOffline = true, zeroRelayFallback = true };
        }
    }

    internal static async Task<object?> JobsAsync() =>
        (await CallAsync(HttpMethod.Get, "/v1/jobs")).Deserialize<object>();
}

internal sealed class TemporaryDaemonP2pAdapter : ITemporaryTransferAdapter
{
    public string ChannelId => "easytier-p2p";

    public async Task<TransferReceipt> PublishAsync(TransferChannel channel, string source,
        string sha256, CancellationToken cancellationToken)
    {
        var response = await YanziTransferServiceBridge.CallAsync(HttpMethod.Post, "/v1/shares",
            new { path = source }, cancellationToken);
        var receipt = response.GetProperty("receipt");
        var digest = receipt.GetProperty("sha256").GetString()
            ?? throw new InvalidDataException("daemon_sha256_missing");
        if (!digest.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("daemon_source_changed_while_publishing");
        return new TransferReceipt(ChannelId, receipt.GetProperty("fileName").GetString()!,
            receipt.GetProperty("bytes").GetInt64(), digest,
            receipt.GetProperty("location").GetString()!,
            DateTimeOffset.UtcNow, receipt.GetProperty("expiresAt").GetDateTimeOffset(),
            "independent_p2p_daemon_kcp_no_relay");
    }

    public async Task DownloadAsync(TransferChannel channel, TransferReceipt receipt,
        string temporaryPath, CancellationToken cancellationToken)
    {
        var request = new
        {
            receipt = new
            {
                channelId = receipt.ChannelId, fileName = receipt.FileName,
                bytes = receipt.Bytes, sha256 = receipt.Sha256,
                location = receipt.Location, expiresAt = receipt.ExpiresUtc.ToString("O")
            }
        };
        var started = await YanziTransferServiceBridge.CallAsync(HttpMethod.Post, "/v1/downloads",
            request, cancellationToken);
        var id = started.GetProperty("job").GetProperty("id").GetString()
            ?? throw new InvalidDataException("independent_job_missing_id");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await YanziTransferServiceBridge.CallAsync(HttpMethod.Get,
                "/v1/jobs/" + id, cancellationToken: cancellationToken);
            var job = status.GetProperty("job");
            switch (job.GetProperty("state").GetString())
            {
                case "completed":
                    var file = job.GetProperty("filePath").GetString()
                        ?? throw new InvalidDataException("independent_file_missing");
                    var safeRoot = Path.GetFullPath(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "YanziTransferService", "data", "inbox")) + Path.DirectorySeparatorChar;
                    if (!Path.GetFullPath(file).StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("independent_file_outside_inbox");
                    if (new FileInfo(file).Length != receipt.Bytes)
                        throw new InvalidDataException("independent_file_size_mismatch");
                    await using (var input = new FileStream(file, FileMode.Open,
                        FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous))
                    await using (var output = new FileStream(temporaryPath, FileMode.Create,
                        FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
                        await input.CopyToAsync(output, 131072, cancellationToken);
                    // Final checksum and no-overwrite inbox promotion remain in the
                    // existing TemporaryTransferCoordinator.
                    return;
                case "failed":
                    throw new IOException("independent_p2p_job_failed");
                case "paused":
                    throw new IOException("independent_p2p_job_paused");
            }
            await Task.Delay(750, cancellationToken);
        }
    }
}
