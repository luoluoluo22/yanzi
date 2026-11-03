using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Read-only detection of OpenCLI's official Chrome Web Store extension.
/// No management permission, browser data modification, or automatic install.
/// </summary>
internal static class OpenCliExtensionOnboarding
{
    internal const string ExtensionId = "ildkmabpimmkaediidaifkhjpohdnifk";
    internal const string StoreUrl = "https://chromewebstore.google.com/detail/opencli/ildkmabpimmkaediidaifkhjpohdnifk";

    internal sealed record Inspection(string Status, string StoreUrl, string Message, int ProfilesChecked)
    {
        internal bool IsMissing => Status == "missing";
    }

    internal static Inspection Inspect(string? invokingExtensionId = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var roots = new[]
        {
            Path.Combine(local, "Citro Labs", "ego lite", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(local, "Vivaldi", "User Data"),
            Path.Combine(roaming, "Opera Software", "Opera Stable")
        };
        var files = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in new[] { Path.Combine(root, "Default"), root })
                AddProfileFiles(path, files);
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(root, "Profile *"))
                    AddProfileFiles(sub, files);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return InspectForCaller(files, invokingExtensionId);
    }

    internal static Inspection InspectForCaller(IEnumerable<string> candidateFiles, string? invokingExtensionId)
    {
        var files = candidateFiles.ToArray();
        // Scan only the browser profile that owns the invoking Yanzi extension,
        // not an unrelated Chrome/Edge profile with a connected OpenCLI bridge.
        if (!string.IsNullOrEmpty(invokingExtensionId) &&
            System.Text.RegularExpressions.Regex.IsMatch(invokingExtensionId,
                @"\A[a-p]{32}\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var grouped = files.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase);
            var ownedFiles = grouped
                .Where(profile => profile.Any(file => HasExtensionId(file, invokingExtensionId)))
                .SelectMany(profile => profile).ToArray();
            if (ownedFiles.Length == 0)
                return new("unknown", StoreUrl,
                    "无法确定当前浏览器配置，请手动查看扩展管理页。", 0);
            return InspectFiles(ownedFiles);
        }
        return InspectFiles(files);
    }

    private static void AddProfileFiles(string profileDirectory, List<string> files)
    {
        if (!Directory.Exists(profileDirectory)) return;
        foreach (var file in new[] { "Secure Preferences", "Preferences" })
        {
            var path = Path.Combine(profileDirectory, file);
            if (File.Exists(path)) files.Add(path);
        }
    }

    private static bool HasExtensionId(string file, string id)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 32 * 1024 * 1024) return false;
            using var input = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(input);
            return doc.RootElement.TryGetProperty("extensions", out var ext) &&
                ext.TryGetProperty("settings", out var settings) &&
                settings.ValueKind == JsonValueKind.Object &&
                settings.TryGetProperty(id, out _);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Statuses: installed, disabled, missing, unknown. If a registered extension
    /// appears in any profile, never claim it is missing or open a new store tab.
    /// </summary>
    internal static Inspection InspectFiles(IEnumerable<string> preferencePaths)
    {
        var profilesChecked = 0;
        var anyDisabled = false;
        foreach (var path in preferencePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 32 * 1024 * 1024) continue;
                using var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(input);
                profilesChecked++;
                if (!document.RootElement.TryGetProperty("extensions", out var exts) ||
                    !exts.TryGetProperty("settings", out var settings) ||
                    settings.ValueKind != JsonValueKind.Object ||
                    !settings.TryGetProperty(ExtensionId, out var entry) ||
                    entry.ValueKind != JsonValueKind.Object) continue;
                bool disabled = (entry.TryGetProperty("state", out var state) &&
                                 state.ValueKind == JsonValueKind.Number &&
                                 state.TryGetInt32(out var stateNum) && stateNum != 1) ||
                                (entry.TryGetProperty("disable_reasons", out var reasons) &&
                                 reasons.ValueKind == JsonValueKind.Number &&
                                 reasons.TryGetInt32(out var reasonNum) && reasonNum != 0);
                if (!disabled)
                    return new("installed", StoreUrl, "OpenCLI 扩展已登记。若无法连接，请检查 Daemon、浏览器与扩展状态。", profilesChecked);
                anyDisabled = true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        if (anyDisabled)
            return new("disabled", StoreUrl, "OpenCLI 扩展已安装但已禁用，请在浏览器扩展管理页启用，勿重复安装。", profilesChecked);
        return profilesChecked > 0
            ? new("missing", StoreUrl, "未发现 OpenCLI Browser Bridge 扩展，已准备打开官方商店引导安装。", profilesChecked)
            : new("unknown", StoreUrl, "无法检查当前浏览器扩展登记。请手动检查扩展管理页，必要时从官方商店安装。", profilesChecked);
    }
}
