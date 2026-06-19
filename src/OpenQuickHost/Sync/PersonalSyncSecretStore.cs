using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class PersonalSyncSecretStore
{
    public static string SecretPath =>
        HostAssets.ResolveDataFilePath("personalsync-secrets.dat");

    public static PersonalSyncSecretBag Load()
    {
        if (!File.Exists(SecretPath))
        {
            return new PersonalSyncSecretBag();
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(SecretPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<PersonalSyncSecretBag>(bytes, JsonOptions) ?? new PersonalSyncSecretBag();
        }
        catch
        {
            return new PersonalSyncSecretBag();
        }
    }

    public static void Save(PersonalSyncSecretBag secrets)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(secrets ?? new PersonalSyncSecretBag(), JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SecretPath, protectedBytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
