using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenQuickHost.Sync;

public static class SecureCredentialStore
{
    public static string CredentialPath =>
        HostAssets.ResolveDataFilePath("synccredentials.dat");

    public static SavedCredential? Load()
    {
        if (!File.Exists(CredentialPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(CredentialPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SavedCredential>(bytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SavedCredential credential)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credential, JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(CredentialPath, protectedBytes);
    }

    public static void Clear()
    {
        if (File.Exists(CredentialPath))
        {
            File.Delete(CredentialPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

public sealed class SavedCredential
{
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("username")]
    public string LegacyUsername { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string LoginEmail =>
        !string.IsNullOrWhiteSpace(Email)
            ? Email
            : LegacyUsername;
}
