using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

/// <summary>
/// AI 密钥只保存在当前 Windows 用户的 DPAPI 密文中。同步载荷只保留服务商元数据。
/// </summary>
public static class AiCredentialStore
{
    private static string SecretPath => HostAssets.ResolveDataFilePath("ai-credentials.dat");

    // 并发 Save 会互相覆盖（读旧 bag → 合并 → 写回，后写者丢掉前者新增的 Key），全部串行化。
    private static readonly object BagIoLock = new();

    public static bool ImportPlaintextAndHydrate(AppSettings settings)
    {
        lock (BagIoLock)
        {
            return ImportPlaintextAndHydrateCore(settings);
        }
    }

    private static bool ImportPlaintextAndHydrateCore(AppSettings settings)
    {
        var bag = Load();
        var importedPlaintext = false;

        if (!string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            bag.LegacyApiKey = settings.AiApiKey.Trim();
            importedPlaintext = true;
        }

        settings.AiServiceProviders ??= [];
        foreach (var provider in settings.AiServiceProviders)
        {
            if (string.IsNullOrWhiteSpace(provider.Id)) continue;
            if (!string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                bag.ProviderApiKeys[provider.Id] = provider.ApiKey.Trim();
                importedPlaintext = true;
            }
        }

        if (importedPlaintext)
        {
            Save(bag);
        }

        if (string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            settings.AiApiKey = bag.LegacyApiKey;
        }
        foreach (var provider in settings.AiServiceProviders)
        {
            if (string.IsNullOrWhiteSpace(provider.ApiKey) &&
                !string.IsNullOrWhiteSpace(provider.Id) &&
                bag.ProviderApiKeys.TryGetValue(provider.Id, out var apiKey))
            {
                provider.ApiKey = apiKey;
            }
        }

        return importedPlaintext;
    }

    public static void Capture(AppSettings settings)
    {
        lock (BagIoLock)
        {
            var bag = Load();
            bag.LegacyApiKey = settings.AiApiKey?.Trim() ?? string.Empty;

            var activeProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in settings.AiServiceProviders ?? [])
            {
                var id = provider.Id?.Trim() ?? string.Empty;
                if (id.Length == 0) continue;
                activeProviderIds.Add(id);
                if (string.IsNullOrWhiteSpace(provider.ApiKey))
                {
                    bag.ProviderApiKeys.Remove(id);
                }
                else
                {
                    bag.ProviderApiKeys[id] = provider.ApiKey.Trim();
                }
            }

            foreach (var staleId in bag.ProviderApiKeys.Keys.Where(id => !activeProviderIds.Contains(id)).ToArray())
            {
                bag.ProviderApiKeys.Remove(staleId);
            }
            Save(bag);
        }
    }

    public static void RemovePlaintext(AppSettings settings)
    {
        settings.AiApiKey = string.Empty;
        foreach (var provider in settings.AiServiceProviders ?? [])
        {
            provider.ApiKey = string.Empty;
        }
    }

    public static List<AiServiceProviderSettings> PrepareSyncedProviderMetadata(
        IEnumerable<AiServiceProviderSettings> providers)
    {
        var clones = JsonSerializer.Deserialize<List<AiServiceProviderSettings>>(
            JsonSerializer.Serialize(providers.ToList())) ?? [];
        foreach (var provider in clones)
        {
            provider.ApiKey = string.Empty;
        }
        return clones;
    }

    /// <summary>
    /// 合并云端服务商元数据时保留本机密钥。旧快照若仍带有密钥，会在本机保存时迁入 DPAPI。
    /// </summary>
    public static void PreserveLocalSecrets(AppSettings local, AppSettings incoming)
    {
        if (!string.IsNullOrWhiteSpace(local.AiApiKey))
        {
            incoming.AiApiKey = local.AiApiKey;
        }

        var localKeys = (local.AiServiceProviders ?? [])
            .Where(static provider => !string.IsNullOrWhiteSpace(provider.Id) && !string.IsNullOrWhiteSpace(provider.ApiKey))
            .ToDictionary(static provider => provider.Id, static provider => provider.ApiKey, StringComparer.OrdinalIgnoreCase);
        foreach (var provider in incoming.AiServiceProviders ?? [])
        {
            if (localKeys.TryGetValue(provider.Id, out var key))
            {
                provider.ApiKey = key;
            }
        }
    }

    private static AiCredentialBag Load()
    {
        if (!File.Exists(SecretPath)) return new AiCredentialBag();
        try
        {
            var protectedBytes = File.ReadAllBytes(SecretPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var bag = JsonSerializer.Deserialize<AiCredentialBag>(bytes, JsonOptions) ?? new AiCredentialBag();
            bag.ProviderApiKeys = new Dictionary<string, string>(bag.ProviderApiKeys ?? [], StringComparer.OrdinalIgnoreCase);
            return bag;
        }
        catch
        {
            return new AiCredentialBag();
        }
    }

    private static void Save(AiCredentialBag bag)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bag, JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        // 原子写：写入中断不再导致整个密钥包损坏丢失。
        var tempPath = SecretPath + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);
        try
        {
            File.Replace(tempPath, SecretPath, destinationBackupFileName: null);
        }
        catch (IOException)
        {
            File.Move(tempPath, SecretPath, overwrite: true);
        }
    }

    private sealed class AiCredentialBag
    {
        public string LegacyApiKey { get; set; } = string.Empty;

        public Dictionary<string, string> ProviderApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
