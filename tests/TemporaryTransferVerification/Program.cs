using System.Security.Cryptography;
using Yanzi.Core.TemporaryTransfer;

var errors = new List<string>();
void Check(string name, bool condition)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition) errors.Add(name);
}
var folder = Path.Combine(Path.GetTempPath(), "yanzi-temporary-verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var source = Path.Combine(folder, "random.bin");
    await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(8192));
    var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));
    var sg = TransferPolicy.BuiltIns.First(c => c.Id == "sg-sftp") with
    { Enabled = true, Host = "example.net", Account = "tester", RemoteDirectory = "/data",
        KnownHostsFile = "pinned-hosts", Priority = 10 };
    var blue = TransferPolicy.BuiltIns.First(c => c.Id == "83blue") with { Enabled = true };
    var tf = TransferPolicy.BuiltIns.First(c => c.Id == "tflink") with { Enabled = true };
    var wen = TransferPolicy.BuiltIns.First(c => c.Id == "wenshushu-browser") with { Enabled = true };
    var items = new[] { blue, sg, tf, wen };
    var homeToOffice = new TransferRequest(TransferEndpoint.Home, TransferEndpoint.Office, 8192,
        AllowPublicProvider: true);
    var sorted = TransferPolicy.Rank(homeToOffice, items);
    Check("home-to-office prefers SG", sorted.First().Id == "sg-sftp");
    Check("browser adapter disabled unattended", sorted.Single(c => c.Id == wen.Id).Reason == "browser_handoff_required");
    var pref = TransferPolicy.Rank(homeToOffice with { PreferredChannel = "83blue" }, items);
    Check("preferred ranked first", pref.First().Id == "83blue");
    Check("preferred still has fallback", pref.Count(x => x.Eligible) == 3);
    var noPublic = TransferPolicy.Rank(homeToOffice with { AllowPublicProvider = false }, items);
    Check("public upload requires permission", noPublic.Single(c => c.Id == "83blue").Reason == "public_upload_not_authorized");
    var phone = TransferPolicy.Rank(new TransferRequest(TransferEndpoint.Home, TransferEndpoint.Phone, 8192, true), items);
    Check("phone forbids overseas providers", !phone.Any(c => c.Eligible));
    var cap = TransferPolicy.Rank(homeToOffice with { Bytes = 200L * 1024 * 1024 }, items);
    Check("capacity excludes tflink", cap.Single(c => c.Id == "tflink").Reason == "capacity_exceeded");
    Check("reject path traversal names", !TransferPolicy.IsSafeFileName("../bad.bin"));
    var fail = new FakeAdapter("sg-sftp", alwaysFail: true);
    var ok = new FakeAdapter("83blue", sourcePath: source);
    var engine = new TemporaryTransferCoordinator(items, [fail, ok]);
    var published = await engine.PublishAsync(source, homeToOffice, CancellationToken.None);
    Check("failed upload switches channel", published.Receipt.ChannelId == "83blue"
        && published.Attempts.Count == 2 && !published.Attempts[0].Success);
    Check("published size/hash", published.Receipt.Bytes == 8192 && published.Receipt.Sha256 == hash);
    var inbox = Path.Combine(folder, "inbox");
    var received = await engine.ReceiveAsync(published.Receipt, inbox, CancellationToken.None);
    Check("receive atomic and sha256", File.Exists(received.FilePath) && received.Sha256 == hash);
    Check("existing file never overwritten", await FailsAsync(() => engine.ReceiveAsync(published.Receipt, inbox, CancellationToken.None)));
    var corrupt = new FakeAdapter("83blue", sourcePath: source, corruptDownload: true);
    var rejectEngine = new TemporaryTransferCoordinator(items, [corrupt]);
    var badInbox = Path.Combine(folder, "bad-inbox");
    Check("corrupt download rejected", await FailsAsync(() => rejectEngine.ReceiveAsync(published.Receipt, badInbox, CancellationToken.None)));
    Check("corrupt file removed", !File.Exists(Path.Combine(badInbox, "random.bin"))
        && !Directory.EnumerateFiles(badInbox).Any());
    Check("expired receipt rejected", await FailsAsync(() => engine.ReceiveAsync(
        published.Receipt with { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-5) },
        Path.Combine(folder, "expired"), CancellationToken.None)));
    Check("SHA-256 spoof rejected", await FailsAsync(() => engine.ReceiveAsync(
        published.Receipt with { Sha256 = new string('0', 64) },
        Path.Combine(folder, "spoof"), CancellationToken.None)));
    // Receiver failover uses backup receipts: a bad first channel cannot poison the inbox.
    var primaryBad = new FakeAdapter("sg-sftp", sourcePath: source, corruptDownload: true);
    var backupOk = new FakeAdapter("83blue", sourcePath: source);
    var backupEngine = new TemporaryTransferCoordinator(items, [primaryBad, backupOk]);
    var bundle = await backupEngine.PublishWithBackupsAsync(source, homeToOffice, 1, CancellationToken.None);
    Check("backup receipt published on alternate channel", bundle.Receipts.Count == 2
        && bundle.Receipts[0].ChannelId == "sg-sftp" && bundle.Receipts[1].ChannelId == "83blue");
    var backupInbox = Path.Combine(folder, "backup-inbox");
    var backupResult = await backupEngine.ReceiveFromCandidatesAsync(bundle.Receipts, backupInbox, CancellationToken.None);
    Check("bad primary download switches to backup", backupResult.ChannelId == "83blue"
        && backupResult.Sha256 == hash && Directory.EnumerateFiles(backupInbox).Count() == 1);
    Check("fallback metadata mismatch rejected", await FailsAsync(() => backupEngine.ReceiveFromCandidatesAsync(
        [bundle.Receipts[0], bundle.Receipts[1] with { Bytes = 4 }],
        Path.Combine(folder, "fallback-bad-metadata"), CancellationToken.None)));
}
finally
{
    Directory.Delete(folder, true);
}
if (errors.Count > 0) { Console.Error.WriteLine(string.Join("; ", errors)); Environment.Exit(1); }
Console.WriteLine("TEMPORARY_TRANSFER_VERIFICATION_OK");

static async Task<bool> FailsAsync(Func<Task> action)
{
    try { await action(); return false; } catch (Exception) { return true; }
}

sealed class FakeAdapter(string id, bool alwaysFail = false, string? sourcePath = null,
    bool corruptDownload = false) : ITemporaryTransferAdapter
{
    public string ChannelId => id;
    public Task<TransferReceipt> PublishAsync(TransferChannel channel, string localFile,
        string sha256, CancellationToken cancellationToken)
    {
        if (alwaysFail) throw new IOException("simulated_failure");
        return Task.FromResult(new TransferReceipt(id, Path.GetFileName(localFile), new FileInfo(localFile).Length,
            sha256, "mock", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
    }
    public async Task DownloadAsync(TransferChannel channel, TransferReceipt receipt,
        string temporaryPath, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath!, token);
        if (corruptDownload) bytes[0] ^= 1;
        await File.WriteAllBytesAsync(temporaryPath, bytes, token);
    }
}
