using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class WebDavCredentialStore
{
    public static string CredentialPath =>
        Path.Combine(AppContext.BaseDirectory, "webdavcredentials.dat");

    public static SavedWebDavCredential? Load()
    {
        if (!File.Exists(CredentialPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(CredentialPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SavedWebDavCredential>(bytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(SavedWebDavCredential credential)
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed class SavedWebDavCredential
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
