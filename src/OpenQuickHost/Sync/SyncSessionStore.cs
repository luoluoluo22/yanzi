using System.IO;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class SyncSessionStore
{
    public static string SessionPath =>
        HostAssets.ResolveDataFilePath("syncsession.json");

    public static SyncSession? Load()
    {
        if (!File.Exists(SessionPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(SessionPath);
            return JsonSerializer.Deserialize<SyncSession>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SyncSession session)
    {
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(SessionPath, json);
    }

    public static void Clear()
    {
        if (File.Exists(SessionPath))
        {
            File.Delete(SessionPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed class SyncSession
{
    public string AccessToken { get; set; } = string.Empty;

    public long ExpiresAt { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool IsVip { get; set; }

    public string? VipType { get; set; }

    public string? VipExpireAt { get; set; }

    public int DaysRemaining { get; set; }
}
