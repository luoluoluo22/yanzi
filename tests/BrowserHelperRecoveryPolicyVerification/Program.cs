using OpenQuickHost;

var approved = "ABCDEF1234";
void Check(bool ok, string label)
{
    if (!ok) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS " + label);
}
bool Can(string mode, string? oldHash, string currentHash,
    string? source, string? bundled, bool equalBundle)
    => BrowserHelperRecoveryPolicy.MayRestorePreviouslyManaged(
        mode, oldHash, currentHash, source, bundled, equalBundle);

Check(Can("source", "old", "new", "0.5.92", "0.5.92", true),
    "matching bundled payload restores");
Check(Can("source", approved, approved.ToLowerInvariant(), "0.5.92", "0.5.92", false),
    "previously approved different source channel restores without changing ID");
Check(Can("stable", approved, approved, "0.5.93", "0.5.92", false),
    "approved newer stable channel restores");
Check(!Can("source", approved, "new", "0.5.92", "0.5.92", false),
    "unapproved changed source blocked");
Check(!Can("source", "", approved, "0.5.92", "0.5.92", false),
    "missing approved fingerprint blocked");
Check(!Can("source", approved, approved, "0.5.91", "0.5.92", false),
    "older source blocked even if fingerprint was approved");
Check(!Can("source", approved, approved, "invalid", "0.5.92", false),
    "invalid source version blocked");
Check(!Can("source", approved, approved, "0.5.92", "invalid", false),
    "invalid bundled version blocked");
Check(!Can("unmanaged", approved, approved, "0.5.92", "0.5.92", true),
    "unmanaged extension blocked");
Check(Can("source", approved, approved, "0.5.92", null, false),
    "approved source recovers when bundled source absent");
var root = Path.Combine(Path.GetTempPath(), "yanzi-policy-test-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"version\":\"0.5.92\"}");
    Check(BrowserHelperRecoveryPolicy.ReadManifestVersion(root) == "0.5.92",
        "manifest version detected");
    File.WriteAllText(Path.Combine(root, "manifest.json"), "invalid");
    Check(BrowserHelperRecoveryPolicy.ReadManifestVersion(root) == null,
        "invalid manifest fails closed");
}
finally
{
    Directory.Delete(root, true);
}
Console.WriteLine("EGO_MANAGED_MISSING_RECOVERY=PASSED");