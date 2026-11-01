using OpenQuickHost.Sync;

static void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

var start = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
var memo = new AccountExtensionConflictMemo(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
const string id = "fixture-extension";
const string directory = "C:\\isolated\\fixture";
var gen = memo.CaptureGeneration(id);
Check(AccountExtensionPullPolicy.Decide(true, "1.0.0", "1.0.0", "LOCAL", "REMOTE")
      == AccountExtensionPullDecision.SameVersionConflict, "same-version mismatch preserves local source");
memo.RememberSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory, gen,
    AccountExtensionPullDecision.SameVersionConflict, start);
Check(memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(3), out var decision) && decision == AccountExtensionPullDecision.SameVersionConflict,
    "same fingerprint reuses safe keep-local conflict decision");
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE2", 42, directory,
    start.AddMinutes(3), out _), "new cloud hash invalidates memo");
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 43, directory,
    start.AddMinutes(3), out _), "new cloud revision invalidates memo");
Check(!memo.TryGetSameVersionDecision(id, "1.0.1", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(3), out _), "new local version invalidates memo");
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory + "2",
    start.AddMinutes(3), out _), "new local directory invalidates memo");
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(5), out _), "TTL forces periodic content rehash");
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", null, 42, directory,
    start.AddMinutes(1), out _), "unknown remote hash is never cached");
Check(memo.ShouldLogConflict(id, "1.0.0", "1.0.0", "REMOTE", 42, "LOCAL", start),
    "first conflict is reported");
Check(!memo.ShouldLogConflict(id, "1.0.0", "1.0.0", "REMOTE", 42, "LOCAL", start.AddMinutes(3)),
    "identical conflict log is suppressed");
Check(memo.ShouldLogConflict(id, "1.0.0", "1.0.0", "REMOTE", 43, "LOCAL", start.AddMinutes(3)),
    "new cloud revision reports conflict");
Check(memo.ShouldLogConflict(id, "1.0.0", "1.0.0", "REMOTE", 43, "LOCAL", start.AddMinutes(18)),
    "unchanged conflict is reminded after 15 minutes");
memo.Invalidate(id);
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(1), out _), "filesystem event invalidates cached decision");
Check(memo.ShouldLogConflict(id, "1.0.0", "1.0.0", "REMOTE", 43, "LOCAL", start.AddMinutes(4)),
    "filesystem change allows fresh conflict report");
memo.RememberSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory, gen,
    AccountExtensionPullDecision.SameVersionConflict, start.AddMinutes(1));
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(2), out _), "watcher generation prevents in-flight stale hash from being cached");
var gen2 = memo.CaptureGeneration(id);
memo.RememberSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory, gen2,
    AccountExtensionPullDecision.InstallNewerVersion, start.AddMinutes(1));
Check(!memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(2), out _), "newer-version install decisions are never cached");
memo.RememberSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory, gen2,
    AccountExtensionPullDecision.KeepLocal, start.AddMinutes(1));
Check(memo.TryGetSameVersionDecision(id, "1.0.0", "1.0.0", "REMOTE", 42, directory,
    start.AddMinutes(2), out decision) && decision == AccountExtensionPullDecision.KeepLocal,
    "identical same-version package can skip repeat hashing");
Check(AccountExtensionPullPolicy.Decide(true, "1.0.0", "1.0.1", "LOCAL", "REMOTE")
      == AccountExtensionPullDecision.InstallNewerVersion, "newer cloud version still follows guarded installation");
// Model four identical pulls spaced three minutes apart, matching the
// observed repeated-conflict pattern. This is NOT a real cloud request count.
var repeatMemo = new AccountExtensionConflictMemo(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
var zipBuilds = 0;
var conflictLogs = 0;
for (var i = 0; i < 4; i++)
{
    var at = start.AddMinutes(i * 3);
    if (repeatMemo.TryGetSameVersionDecision("repeated-extension", "1.0.0", "1.0.0",
        "REMOTE", 42, directory, at, out _))
        continue;

    zipBuilds++;
    var generation = repeatMemo.CaptureGeneration("repeated-extension");
    repeatMemo.RememberSameVersionDecision("repeated-extension", "1.0.0", "1.0.0",
        "REMOTE", 42, directory, generation, AccountExtensionPullDecision.SameVersionConflict, at);
    if (repeatMemo.ShouldLogConflict("repeated-extension", "1.0.0", "1.0.0",
        "REMOTE", 42, "LOCAL", at))
        conflictLogs++;
}
Check(zipBuilds == 2 && conflictLogs == 1,
    "modeled four unchanged pulls need two ZIP hashes and one conflict log (previously four each)");
Console.WriteLine("ACCOUNT_EXTENSION_CONFLICT_MEMO_PASS");
