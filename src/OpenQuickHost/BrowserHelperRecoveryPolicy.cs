using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

// Reinstall only the exact enrolled package, not an arbitrary changed checkout.
internal static class BrowserHelperRecoveryPolicy
{
    internal static string? ReadManifestVersion(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        try
        {
            using var stream = File.OpenRead(Path.Combine(directory, "manifest.json"));
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.GetProperty("version").GetString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal static bool MayRestorePreviouslyManaged(
        string mode, string? approvedFingerprint, string currentFingerprint,
        string? sourceVersion, string? bundledVersion, bool payloadMatchesBundled)
    {
        if (mode is not ("source" or "stable")
            || !Version.TryParse(sourceVersion, out var source))
            return false;
        if (!string.IsNullOrWhiteSpace(bundledVersion))
        {
            if (!Version.TryParse(bundledVersion, out var bundled) || source < bundled)
                return false;
        }
        if (payloadMatchesBundled) return true;
        return !string.IsNullOrWhiteSpace(approvedFingerprint)
            && string.Equals(approvedFingerprint, currentFingerprint,
                StringComparison.OrdinalIgnoreCase);
    }
}