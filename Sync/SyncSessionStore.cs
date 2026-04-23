using System.IO;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class SyncSessionStore
{
    public static string SessionPath =>
        Path.Combine(AppContext.BaseDirectory, "syncsession.json");

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
    public string AccessToken { get; init; } = string.Empty;

    public long ExpiresAt { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;
}

