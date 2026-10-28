using OpenQuickHost;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
var localRoot = Path.Combine(Path.GetTempPath(), "yanzi-retention-test-" + Guid.NewGuid().ToString("N"));
var install = Path.Combine(localRoot, "Yanzi");
Directory.CreateDirectory(Path.Combine(install, "current"));
File.WriteAllText(Path.Combine(install, "current", "Yanzi.exe"), "fake");
var now = DateTime.UtcNow;
string MakeBackup(string name, int ageDays, int length)
{
    var p = Path.Combine(install, name);
    Directory.CreateDirectory(p);
    File.WriteAllText(Path.Combine(p, "file.bin"), new string('x', length));
    var at = now.AddDays(-ageDays);
    Directory.SetCreationTimeUtc(p, at);
    Directory.SetLastWriteTimeUtc(p, at);
    return p;
}
var primary = MakeBackup("rollback-new", 1, 20);
var secondary = MakeBackup("backup-second", 2, 20);
var oldThird = MakeBackup("backup-third", 5, 20);
var oldest = MakeBackup("failed-oldest", 20, 20);
var userData = Path.Combine(install, "my-personal-files");
Directory.CreateDirectory(userData);
File.WriteAllText(Path.Combine(userData, "personal.txt"), "keep");
var protectedPackages = Path.Combine(install, "packages");
Directory.CreateDirectory(protectedPackages);
File.WriteAllText(Path.Combine(protectedPackages, "update.nupkg"), "keep");

try
{
    var audit = YanziArtifactRetentionService.Sweep(install, now, delete: false, maxBytes: 50);
    Ensure(audit.DeletedCount == 2, "Expected two old snapshots to be selected in audit");
    Ensure(Directory.Exists(oldThird) && Directory.Exists(oldest), "Dry-run unexpectedly deleted data");
    var actual = YanziArtifactRetentionService.Sweep(install, now, delete: true, maxBytes: 50);
    Ensure(actual.DeletedCount == 2 && actual.FreedBytes == 40, "Actual cleanup count/bytes mismatch");
    Ensure(Directory.Exists(primary) && Directory.Exists(secondary), "Protected current rollback got deleted");
    Ensure(!Directory.Exists(oldThird) && !Directory.Exists(oldest), "Old snapshots were not removed");
    Ensure(File.Exists(Path.Combine(userData, "personal.txt")), "User data deleted");
    Ensure(File.Exists(Path.Combine(protectedPackages, "update.nupkg")), "Updater cache deleted");

    // Disk-budget cleanup is independent of the maximum snapshot count:
    // with only two snapshots, delete the older one when old enough to be safe.
    var overBudgetAge = now.AddDays(-4);
    Directory.SetCreationTimeUtc(secondary, overBudgetAge);
    Directory.SetLastWriteTimeUtc(secondary, overBudgetAge);
    var budget = YanziArtifactRetentionService.Sweep(install, now, delete: true, maxBytes: 25);
    Ensure(budget.DeletedCount == 1 && !Directory.Exists(secondary) &&
           Directory.Exists(primary), "Disk-budget cleanup failed to keep newest rollback");
    // Unknown installation directories are never candidates, regardless of age/size.
    Ensure(Directory.Exists(userData) && Directory.Exists(protectedPackages),
           "Budget cleanup affected unrelated folders");

    var legacy = Path.Combine(localRoot, "YanziRuntime");
    var versions = Path.Combine(legacy, "versions");
    Directory.CreateDirectory(versions);
    var olderLegacy = Path.Combine(versions, "20261001-older");
    var newLegacy = Path.Combine(versions, "20261014-current");
    Directory.CreateDirectory(olderLegacy);
    Directory.CreateDirectory(newLegacy);
    File.WriteAllText(Path.Combine(olderLegacy, "Yanzi.Runtime.exe"), "old runtime");
    File.WriteAllText(Path.Combine(newLegacy, "Yanzi.Runtime.exe"), "latest runtime");
    Directory.SetLastWriteTimeUtc(olderLegacy, now.AddDays(-30));
    Directory.SetLastWriteTimeUtc(newLegacy, now.AddDays(-5));
    File.WriteAllText(Path.Combine(legacy, "runtime.json"), "{}");
    var unobserved = YanziArtifactRetentionService.SweepLegacyRuntime(install, now, delete: true);
    Ensure(unobserved.DeletedCount == 0, "Legacy retention removed binaries before first observation");
    Ensure(Directory.Exists(olderLegacy), "Legacy grace did not preserve files");
    var marker = Path.Combine(install, ".legacy-runtime-retention-since");
    Ensure(File.Exists(marker), "Legacy observation marker missing");
    var grace = YanziArtifactRetentionService.SweepLegacyRuntime(install, now.AddDays(10), delete: true);
    Ensure(grace.DeletedCount == 0, "Legacy retention ignored 14-day grace");
    File.WriteAllText(Path.Combine(legacy, "runtime.json"),
        System.Text.Json.JsonSerializer.Serialize(new { executable = Path.Combine(olderLegacy, "Yanzi.Runtime.exe") }));
    var linked = YanziArtifactRetentionService.SweepLegacyRuntime(install, now.AddDays(15), delete: true);
    Ensure(linked.DeletedCount == 0 && Directory.Exists(olderLegacy),
        "Active legacy rollback pointer was not protected");
    File.WriteAllText(Path.Combine(legacy, "runtime.json"), "{}");
    var legacyCleanup = YanziArtifactRetentionService.SweepLegacyRuntime(install, now.AddDays(15), delete: true);
    Ensure(legacyCleanup.DeletedCount == 1, "Expected exactly one old legacy snapshot deletion");
    Ensure(!Directory.Exists(olderLegacy) && Directory.Exists(newLegacy), "Legacy newest snapshot not preserved");
    Ensure(File.Exists(Path.Combine(legacy, "runtime.json")), "Legacy pointer JSON was deleted");
    Console.WriteLine($"RETENTION_VERIFIED backupDeleted={actual.DeletedCount} freedBytes={actual.FreedBytes} legacyDeleted={legacyCleanup.DeletedCount}; usersDataSafe=true; rollbackSafe=true");
}
finally
{
    if (Directory.Exists(localRoot)) Directory.Delete(localRoot, recursive: true);
}