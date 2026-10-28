using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Publishes Yanzi-managed variables to the current Windows user's environment.
/// HKCU\Environment is visible to independent programs started after the environment-change notification.
/// Values in this registry location are NOT DPAPI encrypted. Provider-only secrets are excluded.
/// </summary>
internal static class WindowsEnvironmentVariablePublisher
{
    private static readonly object Gate = new();
    private static string OwnershipPath => HostAssets.ResolveDataFilePath("windows-environment-ownership.json");

    // Never replace operating-system foundations (especially PATH) from a mini-app settings editor.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "COMSPEC", "SYSTEMROOT", "WINDIR", "TEMP", "TMP",
        "APPDATA", "LOCALAPPDATA", "USERPROFILE", "HOMEPATH", "HOMEDRIVE",
        "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "PSMODULEPATH"
    };

    internal sealed record PublishResult(int AddedOrUpdated, int Removed, int Conflicts, int Skipped);

    public static PublishResult Synchronize()
    {
        if (!OperatingSystem.IsWindows())
            return new PublishResult(0, 0, 0, 0);

        lock (Gate)
        {
            var allowed = AppEnvironmentVariableStore.GetEnvironmentNames()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var entries = AppEnvironmentVariableStore.Load();
            var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skipped = 0;
            foreach (var item in entries)
            {
                if (!allowed.Contains(item.Name) || ProtectedNames.Contains(item.Name) ||
                    string.IsNullOrWhiteSpace(item.Value) || item.Name.IndexOf('\0') >= 0 ||
                    item.Name.Contains('\r') || item.Name.Contains('\n'))
                {
                    skipped++;
                    continue;
                }

                desired[item.Name] = item.Value;
            }

            var owned = LoadOwnership();
            var changed = 0;
            var removed = 0;
            var conflicts = 0;
            foreach (var previous in owned.ToArray())
            {
                if (desired.ContainsKey(previous.Key))
                    continue;

                try
                {
                    var current = Environment.GetEnvironmentVariable(previous.Key, EnvironmentVariableTarget.User);
                    if (current != null && Digest(current) == previous.Value)
                    {
                        Environment.SetEnvironmentVariable(previous.Key, null, EnvironmentVariableTarget.User);
                        if (Environment.GetEnvironmentVariable(previous.Key) == current)
                            Environment.SetEnvironmentVariable(previous.Key, null);
                        removed++;
                    }
                    owned.Remove(previous.Key);
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"[WindowsEnv] Unable to remove managed name {previous.Key}: {ex.GetType().Name}");
                    conflicts++;
                }
            }

            foreach (var pair in desired)
            {
                try
                {
                    var current = Environment.GetEnvironmentVariable(pair.Key, EnvironmentVariableTarget.User);
                    var wasOwned = owned.TryGetValue(pair.Key, out var previousHash);
                    // Do not clobber variables now controlled by another application/user action.
                    if (current != null && current != pair.Value &&
                        (!wasOwned || Digest(current) != previousHash))
                    {
                        conflicts++;
                        continue;
                    }

                    if (current == pair.Value)
                    {
                        if (wasOwned) owned[pair.Key] = Digest(pair.Value);
                        continue;
                    }

                    Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                    owned[pair.Key] = Digest(pair.Value);
                    changed++;
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"[WindowsEnv] Unable to publish name {pair.Key}: {ex.GetType().Name}");
                    conflicts++;
                }
            }

            try { SaveOwnership(owned); }
            catch (Exception ex) { HostAssets.AppendLog($"[WindowsEnv] Ownership save failed: {ex.GetType().Name}"); }

            if (changed > 0 || removed > 0)
                NotifyEnvironmentChanged();

            HostAssets.AppendLog($"[WindowsEnv] User scope sync: updated={changed}, removed={removed}, conflicts={conflicts}, skipped={skipped}");
            return new PublishResult(changed, removed, conflicts, skipped);
        }
    }

    private static string Digest(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static Dictionary<string, string> LoadOwnership()
    {
        try
        {
            if (File.Exists(OwnershipPath))
                return new Dictionary<string, string>(
                    JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(OwnershipPath))
                    ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[WindowsEnv] Ownership load failed: {ex.GetType().Name}");
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveOwnership(Dictionary<string, string> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OwnershipPath)!);
        var tempPath = OwnershipPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state));
        File.Move(tempPath, OwnershipPath, true);
    }

    private static void NotifyEnvironmentChanged()
    {
        try
        {
            _ = SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero,
                "Environment", 0x0002, 500, out _);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[WindowsEnv] Broadcast failed: {ex.GetType().Name}");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}
