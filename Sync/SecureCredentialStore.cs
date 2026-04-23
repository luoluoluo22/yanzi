using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class SecureCredentialStore
{
    public static string CredentialPath =>
        Path.Combine(AppContext.BaseDirectory, "synccredentials.dat");

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
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
