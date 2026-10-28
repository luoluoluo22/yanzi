using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yanzi.Core.TemporaryTransfer;

namespace OpenQuickHost;

/// <summary>
/// Opt-in transport adapters. Real credentials, if any, remain in per-device user config.
/// Browser-only providers are advertised by policy, never impersonated as unattended uploads.
/// </summary>
internal static class YanziTemporaryTransferAdapters
{
    public static IReadOnlyList<ITemporaryTransferAdapter> Create() =>
        [new TemporarySftpAdapter(), new TemporaryPublicUploadAdapter("83blue"),
         new TemporaryPublicUploadAdapter("tflink")];

    public static IReadOnlyList<TransferChannel> LoadChannels()
    {
        var stored = HostAssets.ResolveDataDirectoryPath("temporary-transfer/channels.json");
        var defaults = TransferPolicy.BuiltIns.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(stored)) return defaults.Values.ToArray();
        using var document = JsonDocument.Parse(File.ReadAllText(stored));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("temporary_transfer_config_invalid");
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? "";
            if (!defaults.TryGetValue(id, out var profile))
                throw new InvalidDataException("unknown_transfer_channel");
            static string? GetOptional(JsonElement el, string name) =>
                el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            defaults[id] = profile with
            {
                Enabled = item.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True,
                MaxFileBytes = item.TryGetProperty("maxFileBytes", out var max) && max.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(max.GetInt64(), 1L, TransferPolicy.MaximumFileBytes) : profile.MaxFileBytes,
                Host = GetOptional(item, "host"),
                Account = GetOptional(item, "account"),
                RemoteDirectory = GetOptional(item, "remoteDirectory"),
                KnownHostsFile = GetOptional(item, "knownHostsFile"),
                IdentityFile = GetOptional(item, "identityFile")
            };
        }
        return defaults.Values.ToArray();
    }
}

internal sealed class TemporarySftpAdapter : ITemporaryTransferAdapter
{
    public string ChannelId => "sg-sftp";

    private static void ValidateProfile(TransferChannel profile)
    {
        if (profile.Kind != ChannelKind.Sftp ||
            profile.Host is null || !Regex.IsMatch(profile.Host, @"^[a-zA-Z0-9.-]{1,253}$") ||
            profile.Account is null || !Regex.IsMatch(profile.Account, @"^[a-z_][a-z0-9_-]{0,31}$") ||
            profile.RemoteDirectory is null || !Regex.IsMatch(profile.RemoteDirectory, @"^/[a-zA-Z0-9/_-]{1,200}$") ||
            profile.KnownHostsFile is null || !File.Exists(profile.KnownHostsFile) ||
            (profile.IdentityFile != null && !File.Exists(profile.IdentityFile)))
            throw new InvalidOperationException("sftp_profile_not_ready");
    }

    private static async Task RunScpAsync(TransferChannel profile, string origin, string destination,
        CancellationToken token)
    {
        ValidateProfile(profile);
        var psi = new ProcessStartInfo("scp.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var arg in new[] { "-B", "-q", "-o", "BatchMode=yes", "-o", "Compression=no",
            "-o", "ConnectTimeout=12", "-o", "ServerAliveInterval=8", "-o",
            "ServerAliveCountMax=2", "-o", "StrictHostKeyChecking=yes", "-o",
            "UserKnownHostsFile=" + profile.KnownHostsFile })
            psi.ArgumentList.Add(arg);
        if (!string.IsNullOrEmpty(profile.IdentityFile))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(profile.IdentityFile);
        }
        psi.ArgumentList.Add(origin);
        psi.ArgumentList.Add(destination);
        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new IOException("scp_not_started");
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(token);
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            var error = await stderr;
            _ = await stdout;
            if (process.ExitCode != 0) throw new IOException("sftp_exit_" + process.ExitCode +
                (error.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ? "_permission_denied" : ""));
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    public async Task<TransferReceipt> PublishAsync(TransferChannel profile, string localFile,
        string sha256, CancellationToken cancellationToken)
    {
        ValidateProfile(profile);
        var name = Guid.NewGuid().ToString("N") + ".bin";
        var remote = profile.RemoteDirectory!.TrimEnd('/') + "/" + name;
        var destination = profile.Account + "@" + profile.Host + ":" + remote;
        await RunScpAsync(profile, localFile, destination, cancellationToken);
        // Transport checksum is verified on the receiving device; never claim verified before then.
        return new TransferReceipt(profile.Id, Path.GetFileName(localFile), new FileInfo(localFile).Length,
            sha256, name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "sftp_pinned_host");
    }

    public Task DownloadAsync(TransferChannel profile, TransferReceipt ticket,
        string temporaryPath, CancellationToken token)
    {
        ValidateProfile(profile);
        if (!Regex.IsMatch(ticket.Location, @"^[a-f0-9]{32}\.bin$"))
            throw new InvalidDataException("invalid_remote_object_key");
        var source = profile.Account + "@" + profile.Host + ":" + profile.RemoteDirectory!.TrimEnd('/') + "/" + ticket.Location;
        return RunScpAsync(profile, source, temporaryPath, token);
    }
}

internal sealed class TemporaryPublicUploadAdapter : ITemporaryTransferAdapter
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(3)
    };
    public string ChannelId { get; }
    public TemporaryPublicUploadAdapter(string channelId)
    {
        if (channelId is not ("83blue" or "tflink")) throw new ArgumentException(nameof(channelId));
        ChannelId = channelId;
    }

    private Uri UploadEndpoint => new(ChannelId == "83blue"
        ? "https://upload.83blue.com/api/upload" : "https://tmpfile.link/api/upload");

    private bool AllowedDownload(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return false;
        var name = uri.Host.ToLowerInvariant();
        return ChannelId == "83blue"
            ? name == "upload.83blue.com"
            : (name == "tmpfile.link" || name.EndsWith(".tempfilestorage.link", StringComparison.Ordinal));
    }

    public async Task<TransferReceipt> PublishAsync(TransferChannel channel, string localFile,
        string sha256, CancellationToken token)
    {
        if (channel.Id != ChannelId) throw new InvalidOperationException("provider_mismatch");
        await using var file = File.OpenRead(localFile);
        using var form = new MultipartFormDataContent();
        using var payload = new StreamContent(file);
        payload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(payload, "file", Path.GetFileName(localFile));
        using var response = await Http.PostAsync(UploadEndpoint, form, token);
        response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var obj = data.RootElement;
        string location;
        if (ChannelId == "83blue")
        {
            if (!obj.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidDataException("provider_rejected_upload");
            var declared = obj.GetProperty("sha256").GetString();
            if (!sha256.Equals(declared, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("provider_checksum_mismatch");
            location = obj.GetProperty("download").GetProperty("api").GetString()
                       ?? throw new InvalidDataException("download_url_missing");
        }
        else
        {
            location = obj.TryGetProperty("downloadLinkEncoded", out var l)
                ? l.GetString() ?? "" : obj.GetProperty("downloadLink").GetString() ?? "";
        }
        if (!Uri.TryCreate(location, UriKind.Absolute, out var u) || !AllowedDownload(u))
            throw new InvalidDataException("untrusted_provider_download_url");
        var ttl = ChannelId == "83blue" ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
        return new TransferReceipt(ChannelId, Path.GetFileName(localFile), new FileInfo(localFile).Length,
            sha256, location, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl), "public_anonymous_url");
    }

    public async Task DownloadAsync(TransferChannel profile, TransferReceipt receipt,
        string temporaryPath, CancellationToken token)
    {
        if (profile.Id != ChannelId || !Uri.TryCreate(receipt.Location, UriKind.Absolute, out var uri))
            throw new InvalidDataException("invalid_download_location");
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (!AllowedDownload(uri)) throw new InvalidDataException("untrusted_download_redirect");
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException("redirect_without_location");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location.ToString());
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } len && len != receipt.Bytes)
                throw new InvalidDataException("remote_length_mismatch");
            await using var file = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await using var data = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[65536];
            long total = 0;
            int count;
            while ((count = await data.ReadAsync(buffer, token)) != 0)
            {
                total += count;
                if (total > receipt.Bytes) throw new InvalidDataException("download_exceeds_expected_size");
                await file.WriteAsync(buffer.AsMemory(0, count), token);
            }
            return;
        }
        throw new InvalidDataException("too_many_download_redirects");
    }
}
