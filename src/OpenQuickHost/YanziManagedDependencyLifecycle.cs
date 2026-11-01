using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Ownership-bound cleanup of optional programs installed by Yanzi miniapps.
/// Never treats a pre-existing RustDesk executable, a shared runtime directory,
/// or the user's RustDesk profile as Yanzi-owned.
/// </summary>
internal static class YanziManagedDependencyLifecycle
{
    private const string RemoteExtensionId = "yanzi-remote-desktop";
    private const string ExpectedOrigin = "github-rustdesk-signed";

    private sealed class Ownership
    {
        public int SchemaVersion { get; set; }
        public string? OwnerExtensionId { get; set; }
        public string? ExecutableSha256 { get; set; }
        public string? Origin { get; set; }
        public string? Version { get; set; }
    }

    private static string RuntimeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "ManagedDependencies", RemoteExtensionId);

    public static void OnYanziUninstall()
    {
        TryCleanupForExtension(RemoteExtensionId);
    }

    public static void TryCleanupForExtension(string? extensionId)
    {
        if (!string.Equals(extensionId, RemoteExtensionId, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            var result = CleanupOwnedDirectory(RuntimeDirectory);
            HostAssets.AppendLog($"Managed dependency cleanup: extension={RemoteExtensionId}, result={result}.");
        }
        catch (Exception error)
        {
            // An uninstall hook must never delete a non-owned program or abort Yanzi removal.
            HostAssets.AppendLog($"Managed dependency cleanup deferred: type={error.GetType().Name}.");
        }
    }

    internal static string CleanupOwnedDirectory(string root)
    {
        if (!Directory.Exists(root)) return "absent";
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            return "refused-reparse-directory";
        var receipt = Path.Combine(root, "ownership.json");
        var exe = Path.Combine(root, "rustdesk.exe");
        if (!File.Exists(receipt) || !File.Exists(exe)) return "unowned-or-incomplete";
        if ((File.GetAttributes(receipt) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(exe) & FileAttributes.ReparsePoint) != 0)
            return "refused-reparse-file";
        Ownership? record;
        try { record = JsonSerializer.Deserialize<Ownership>(File.ReadAllText(receipt)); }
        catch { return "invalid-receipt"; }
        if (record == null || record.SchemaVersion != 1 ||
            !string.Equals(record.OwnerExtensionId, RemoteExtensionId, StringComparison.Ordinal) ||
            !string.Equals(record.Origin, ExpectedOrigin, StringComparison.Ordinal) ||
            record.ExecutableSha256?.Length != 64 ||
            !record.ExecutableSha256.All(Uri.IsHexDigit))
            return "not-owned-by-yanzi";
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));
        if (!string.Equals(actualHash, record.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            return "modified-binary-refused";

        foreach (var process in Process.GetProcessesByName("rustdesk"))
        {
            try
            {
                var runningPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(runningPath)) continue;
                if (!string.Equals(Path.GetFullPath(runningPath ?? ""), Path.GetFullPath(exe),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (process.CloseMainWindow() && process.WaitForExit(1200)) continue;
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(3000))
                    return "owned-process-still-running";
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { return "owned-process-access-denied"; }
            finally { process.Dispose(); }
        }

        File.Delete(exe);
        File.Delete(receipt);
        var partial = Path.Combine(root, "rustdesk.exe.partial");
        if (File.Exists(partial) && (File.GetAttributes(partial) & FileAttributes.ReparsePoint) == 0)
            File.Delete(partial);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        return "removed-owned-runtime";
    }
}
