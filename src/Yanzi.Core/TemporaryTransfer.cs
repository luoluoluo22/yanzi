using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Yanzi.Core.TemporaryTransfer;

/// <summary>Directions are explicit: observed performance is often asymmetric.</summary>
public enum TransferEndpoint { Home, Office, Phone, Other }
public enum ChannelKind { Sftp, HttpMultipart, BrowserHandoff, DirectP2p }
public enum ChannelRegion { ChinaMainland, Overseas, Unknown }

public sealed record TransferChannel(
    string Id, ChannelKind Kind, ChannelRegion Region, long MaxFileBytes,
    bool Enabled = false, int Priority = 0, bool SupportsUnattended = true,
    string? Remote = null, string? Host = null, string? Account = null,
    string? RemoteDirectory = null, string? KnownHostsFile = null,
    string? IdentityFile = null, string? Note = null,
    string? P2pCliPath = null, string? P2pPeerName = null, string? P2pRemoteIp = null,
    int P2pRpcPort = 0, int P2pSocksPort = 0, int P2pListenPort = 0);

public sealed record TransferRequest(
    TransferEndpoint Source, TransferEndpoint Destination, long Bytes,
    bool AllowPublicProvider = false, bool DomesticOnly = false,
    string? PreferredChannel = null);

public sealed record TransferCandidate(string Id, bool Eligible, string Reason, int Score);
public sealed record TransferAttempt(string ChannelId, bool Success, string? Error);
public sealed record TransferReceipt(
    string ChannelId, string FileName, long Bytes, string Sha256, string Location,
    DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, string? DownloadHint = null);
public sealed record TransferPublishResult(TransferReceipt Receipt, IReadOnlyList<TransferAttempt> Attempts);
public sealed record TransferReceiveResult(string FilePath, long Bytes, string Sha256, string ChannelId);
public sealed record TransferBatchResult(IReadOnlyList<TransferReceipt> Receipts, IReadOnlyList<TransferAttempt> Attempts);

public static class TransferPolicy
{
    public const long MaximumFileBytes = 5L * 1024 * 1024 * 1024;
    public static readonly IReadOnlyList<TransferChannel> BuiltIns = new[]
    {
        new TransferChannel("easytier-p2p", ChannelKind.DirectP2p, ChannelRegion.ChinaMainland,
            MaximumFileBytes, Priority: 500, Note: "Direct UDP P2P with KCP; opt-in and no silent relay."),
        new TransferChannel("sg-sftp", ChannelKind.Sftp, ChannelRegion.Overseas,
            MaximumFileBytes, Priority: 40, Note: "Key-based SFTP, explicit configuration; verified home→office, office upload unstable."),
        new TransferChannel("83blue", ChannelKind.HttpMultipart, ChannelRegion.Overseas,
            2L * 1024 * 1024 * 1024 * 1024, Priority: 20, Note: "Observed office→home 4 MiB success; throttling and public retention apply."),
        new TransferChannel("tflink", ChannelKind.HttpMultipart, ChannelRegion.Overseas,
            100L * 1024 * 1024, Priority: 5, Note: "Anonymous public link; home upload often times out."),
        new TransferChannel("feichuan-browser", ChannelKind.BrowserHandoff, ChannelRegion.Unknown,
            100L * 1024 * 1024, SupportsUnattended: false, Note: "Web session needed; adapter pending."),
        new TransferChannel("wenshushu-browser", ChannelKind.BrowserHandoff, ChannelRegion.ChinaMainland,
            5L * 1024 * 1024 * 1024, SupportsUnattended: false, Note: "Authenticated browser upload/download required."),
        new TransferChannel("tmplink-browser", ChannelKind.BrowserHandoff, ChannelRegion.Unknown,
            50L * 1024 * 1024 * 1024, SupportsUnattended: false, Note: "Browser handoff required; office connection unstable.")
    };

    public static IReadOnlyList<TransferCandidate> Rank(TransferRequest request, IEnumerable<TransferChannel> channels)
    {
        if (request.Bytes < 1 || request.Bytes > MaximumFileBytes || request.Source == request.Destination)
            throw new ArgumentOutOfRangeException(nameof(request), "invalid_direction_or_size");
        var domestic = request.DomesticOnly || request.Destination == TransferEndpoint.Phone;
        return channels.Select(c =>
        {
            string? reason = null;
            if (!c.Enabled) reason = "not_enabled";
            else if (c.MaxFileBytes < request.Bytes) reason = "capacity_exceeded";
            else if (!c.SupportsUnattended) reason = "browser_handoff_required";
            else if (domestic && c.Region != ChannelRegion.ChinaMainland) reason = "domestic_route_required";
            else if (c.Kind == ChannelKind.DirectP2p &&
                (string.IsNullOrWhiteSpace(c.P2pCliPath) || string.IsNullOrWhiteSpace(c.P2pPeerName) ||
                 string.IsNullOrWhiteSpace(c.P2pRemoteIp) || c.P2pRpcPort < 1024 ||
                 c.P2pSocksPort < 1024 || c.P2pListenPort < 1024)) reason = "p2p_not_configured";
            else if (c.Kind == ChannelKind.HttpMultipart && !request.AllowPublicProvider) reason = "public_upload_not_authorized";
            else if (c.Kind == ChannelKind.Sftp && (string.IsNullOrWhiteSpace(c.Host) || string.IsNullOrWhiteSpace(c.Account)
                         || string.IsNullOrWhiteSpace(c.RemoteDirectory) || string.IsNullOrWhiteSpace(c.KnownHostsFile)))
                reason = "sftp_not_configured";
            int score = c.Priority + (request.PreferredChannel != null && c.Id.Equals(request.PreferredChannel, StringComparison.OrdinalIgnoreCase) ? 1000 : 0);
            bool homeOffice = request.Source == TransferEndpoint.Home && request.Destination == TransferEndpoint.Office;
            bool officeHome = request.Source == TransferEndpoint.Office && request.Destination == TransferEndpoint.Home;
            if (homeOffice && c.Id == "sg-sftp") score += 100;
            if (officeHome && c.Id == "83blue") score += 110;
            if (officeHome && c.Id == "sg-sftp") score -= 80;
            if (request.Bytes > 50L * 1024 * 1024 && c.Id == "83blue") score -= 65;
            return new TransferCandidate(c.Id, reason is null, reason ?? "ready", score);
        }).OrderByDescending(c => c.Eligible).ThenByDescending(c => c.Score).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray();
    }

    public static bool IsSafeFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 180 &&
        name is not ("." or "..") && name == Path.GetFileName(name) &&
        name.All(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c) && c is not '/' and not '\\');

    public static string VerifySha256(string value)
    {
        if (value.Length != 64 || !Regex.IsMatch(value, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("invalid_sha256");
        return value.ToUpperInvariant();
    }
}

public interface ITemporaryTransferAdapter
{
    string ChannelId { get; }
    Task<TransferReceipt> PublishAsync(TransferChannel channel, string localFile, string sha256, CancellationToken cancellationToken);
    Task DownloadAsync(TransferChannel channel, TransferReceipt receipt, string temporaryPath, CancellationToken cancellationToken);
}

public sealed class TemporaryTransferCoordinator
{
    private readonly IReadOnlyList<TransferChannel> _channels;
    private readonly IReadOnlyDictionary<string, ITemporaryTransferAdapter> _adapters;
    private readonly Dictionary<string, int> _failures = new(StringComparer.OrdinalIgnoreCase);

    public TemporaryTransferCoordinator(IEnumerable<TransferChannel> channels, IEnumerable<ITemporaryTransferAdapter> adapters)
    {
        _channels = channels.ToArray();
        if (_channels.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new ArgumentException("duplicate_channel_id");
        _adapters = adapters.ToDictionary(a => a.ChannelId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<TransferCandidate> Plan(TransferRequest request) =>
        TransferPolicy.Rank(request, _channels).OrderByDescending(c => c.Eligible)
            .ThenByDescending(c => c.Score - (_failures.GetValueOrDefault(c.Id) * 35))
            .ToArray();

    public async Task<TransferPublishResult> PublishAsync(string path, TransferRequest request, CancellationToken cancellationToken, IReadOnlySet<string>? excludedChannels = null)
    {
        var source = Path.GetFullPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("source_missing", source);
        var info = new FileInfo(source);
        if (info.Length != request.Bytes || !TransferPolicy.IsSafeFileName(info.Name))
            throw new InvalidDataException("file_metadata_invalid");
        var attempts = new List<TransferAttempt>();
        string digest;
        await using (var input = File.OpenRead(source))
            digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        foreach (var choice in Plan(request).Where(c => c.Eligible && (excludedChannels is null || !excludedChannels.Contains(c.Id))
                     && (request.PreferredChannel != "easytier-p2p" || c.Id == "easytier-p2p")))
        {
            if (!_adapters.TryGetValue(choice.Id, out var adapter))
            {
                attempts.Add(new TransferAttempt(choice.Id, false, "adapter_unavailable"));
                if (choice.Id == "easytier-p2p") break;
                continue;
            }
            var channel = _channels.First(c => c.Id.Equals(choice.Id, StringComparison.OrdinalIgnoreCase));
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                var receipt = await adapter.PublishAsync(channel, source, digest, timeout.Token);
                if (receipt.ChannelId != channel.Id || receipt.Bytes != info.Length ||
                    TransferPolicy.VerifySha256(receipt.Sha256) != digest || receipt.ExpiresUtc <= DateTimeOffset.UtcNow)
                    throw new InvalidDataException("invalid_provider_receipt");
                attempts.Add(new TransferAttempt(choice.Id, true, null));
                return new TransferPublishResult(receipt, attempts);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _failures[choice.Id] = _failures.GetValueOrDefault(choice.Id) + 1;
                attempts.Add(new TransferAttempt(choice.Id, false, ex is OperationCanceledException ? "timeout" : ex.GetType().Name));
                if (choice.Id == "easytier-p2p") break; // Never silently upload via another route.
            }
        }
        throw new IOException("temporary_transfer_unavailable: " + string.Join(", ", attempts.Select(a => a.ChannelId + ":" + a.Error)));
    }

    /// <summary>
    /// Optionally pre-stage a second or third provider. A receiver can then switch
    /// without requiring the sender to be online again. Extra uploads are opt-in.
    /// </summary>
    public async Task<TransferBatchResult> PublishWithBackupsAsync(string path, TransferRequest request,
        int backupCount, CancellationToken cancellationToken)
    {
        if (backupCount is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(backupCount));
        var uploaded = new List<TransferReceipt>();
        var attempts = new List<TransferAttempt>();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= backupCount; i++)
        {
            if (!Plan(request).Any(x => x.Eligible && !excluded.Contains(x.Id))) break;
            try
            {
                var result = await PublishAsync(path, request, cancellationToken, excluded);
                uploaded.Add(result.Receipt);
                attempts.AddRange(result.Attempts);
                foreach (var at in result.Attempts) excluded.Add(at.ChannelId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (IOException)
            {
                if (uploaded.Count == 0) throw;
                break;
            }
        }
        if (uploaded.Count == 0) throw new IOException("temporary_transfer_no_available_provider");
        return new TransferBatchResult(uploaded, attempts);
    }

    public async Task<TransferReceiveResult> ReceiveFromCandidatesAsync(
        IReadOnlyList<TransferReceipt> receipts, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (receipts.Count is < 1 or > 3) throw new InvalidDataException("invalid_receipt_count");
        var first = receipts[0];
        var expectedHash = TransferPolicy.VerifySha256(first.Sha256);
        if (receipts.Any(r => r.FileName != first.FileName || r.Bytes != first.Bytes ||
                              TransferPolicy.VerifySha256(r.Sha256) != expectedHash))
            throw new InvalidDataException("fallback_receipt_metadata_mismatch");
        var failures = new List<string>();
        foreach (var receipt in receipts)
        {
            try { return await ReceiveAsync(receipt, destinationDirectory, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { failures.Add(receipt.ChannelId + ":" + ex.GetType().Name); }
        }
        throw new IOException("all_temporary_downloads_failed: " + string.Join(",", failures));
    }
    public async Task<TransferReceiveResult> ReceiveAsync(TransferReceipt receipt, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (!TransferPolicy.IsSafeFileName(receipt.FileName) || receipt.Bytes < 1 ||
            receipt.Bytes > TransferPolicy.MaximumFileBytes || receipt.ExpiresUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("invalid_or_expired_receipt");
        var hash = TransferPolicy.VerifySha256(receipt.Sha256);
        var channel = _channels.FirstOrDefault(c => c.Id.Equals(receipt.ChannelId, StringComparison.OrdinalIgnoreCase));
        if (channel is null || !channel.Enabled || !_adapters.TryGetValue(channel.Id, out var adapter))
            throw new InvalidOperationException("channel_unavailable");
        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, receipt.FileName);
        if (File.Exists(target)) throw new IOException("target_already_exists");
        var resumable = channel.Kind == ChannelKind.DirectP2p;
        // Name depends on this exact signed receipt, but does not expose the bearer secret.
        var resumeKey = resumable ? Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(receipt.Location + "|" + receipt.Sha256 + "|" + receipt.FileName)))
            .ToLowerInvariant() : Guid.NewGuid().ToString("N");
        var part = Path.Combine(root, (resumable ? ".p2p-" : ".") + resumeKey + ".part");
        bool preservePartial = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(channel.Kind == ChannelKind.DirectP2p ? TimeSpan.FromMinutes(45) : TimeSpan.FromMinutes(5));
            await adapter.DownloadAsync(channel, receipt, part, timeout.Token);
            var info = new FileInfo(part);
            if (!info.Exists || info.Length != receipt.Bytes) throw new InvalidDataException("size_mismatch");
            await using (var stream = File.OpenRead(part))
                if (Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)) != hash)
                    throw new InvalidDataException("sha256_mismatch");
            File.Move(part, target);  // atomic, never silently overwrite
            return new TransferReceiveResult(target, info.Length, hash, receipt.ChannelId);
        }
        catch (Exception ex) when (resumable &&
            (ex is IOException or HttpRequestException or OperationCanceledException))
        {
            if (File.Exists(part))
            {
                var size = new FileInfo(part).Length;
                preservePartial = size > 0 && size < receipt.Bytes;
                // A fully received file is retained only if the final hash really matches.
                if (size == receipt.Bytes)
                {
                    await using var existing = File.OpenRead(part);
                    preservePartial = Convert.ToHexString(await SHA256.HashDataAsync(existing,
                        CancellationToken.None)) == hash;
                }
            }
            throw;
        }
        finally
        {
            if (!preservePartial && File.Exists(part)) File.Delete(part);
        }
    }
}
