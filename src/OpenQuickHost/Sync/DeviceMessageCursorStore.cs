using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost.Sync;

internal static class DeviceMessageCursorStore
{
    private static readonly object Gate = new();

    private static string PathFor(string account, string deviceId)
    {
        var identity = account + "\n" + deviceId;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".cursor";
        return Path.Combine(HostAssets.ResolveDataDirectoryPath("device-message-cursors"), name);
    }

    internal static bool Exists(string account, string deviceId)
    {
        lock (Gate)
        {
            try { return File.Exists(PathFor(account, deviceId)); }
            catch { return false; }
        }
    }

    public static long Read(string account, string deviceId)
    {
        lock (Gate)
        {
            try
            {
                var path = PathFor(account, deviceId);
                if (!File.Exists(path)) return 0;
                return long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
                    ? value
                    : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public static void Write(string account, string deviceId, long cursor)
    {
        if (cursor < 0) throw new ArgumentOutOfRangeException(nameof(cursor));
        lock (Gate)
        {
            var path = PathFor(account, deviceId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, cursor.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, path, true);
        }
    }
}
