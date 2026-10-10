using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost.Sync;

/// <summary>
/// Notification eligibility is separate from cloud retention and message acknowledgement.
/// First account/device sync imports earlier messages silently instead of replaying 30 days of popups.
/// This marker is local, account-scoped, and durable across desktop restarts.
/// </summary>
internal static class DeviceMessageNotificationPolicy
{
    private static readonly object Gate = new();
    internal static readonly TimeSpan MaximumPopupAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(2);

    private static string PathFor(string account, string deviceId)
    {
        var identity = account + "\n" + deviceId;
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".first-seen";
        return Path.Combine(HostAssets.ResolveDataDirectoryPath("device-message-cursors"), fileName);
    }

    /// <param name="hadCursor">True when this account/device had already consumed messages before this policy was installed.</param>
    internal static DateTimeOffset ReadOrInitialize(string account, string deviceId, bool hadCursor, DateTimeOffset nowUtc)
    {
        lock (Gate)
        {
            try
            {
                var path = PathFor(account, deviceId);
                if (File.Exists(path) &&
                    DateTimeOffset.TryParse(File.ReadAllText(path), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var stored))
                    return stored.ToUniversalTime();

                // An existing cursor means this is an upgrade, not a newly logged-in device.
                // Still apply the independent 15-minute popup freshness limit.
                var baseline = hadCursor ? DateTimeOffset.MinValue : nowUtc.ToUniversalTime();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, baseline.ToString("O", CultureInfo.InvariantCulture));
                File.Move(temporary, path, true);
                return baseline;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Fail silently for old notifications if the marker cannot be persisted.
                HostAssets.AppendLog("Message notification first-sync marker unavailable: " + error.GetType().Name);
                return nowUtc.ToUniversalTime();
            }
        }
    }

    internal static bool ShouldPopup(DeviceMessageRecord message, DateTimeOffset firstSeenUtc, DateTimeOffset nowUtc)
    {
        if (TryReadCreatedAt(message.ExpiresAt, out var expiry) && expiry.ToUniversalTime() <= nowUtc.ToUniversalTime())
            return false;

        if (!TryReadCreatedAt(message.CreatedAt, out var createdAt))
        {
            // Legacy realtime/LAN envelopes may omit a timestamp. Never replay them on a first sync.
            return firstSeenUtc == DateTimeOffset.MinValue;
        }

        var timestamp = createdAt.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();
        if (timestamp < firstSeenUtc.ToUniversalTime() ||
            timestamp > now + ClockSkewAllowance ||
            now - timestamp > MaximumPopupAge)
            return false;

        return true;
    }

    internal static bool TryReadCreatedAt(string? value, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out timestamp)) return true;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var millis))
        {
            try
            {
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(millis);
                return true;
            }
            catch (ArgumentOutOfRangeException) { }
        }
        timestamp = default;
        return false;
    }
}
