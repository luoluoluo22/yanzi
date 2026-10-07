using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

internal static class AccountEnvironmentSecretVault
{
    internal const string ObjectId = "environmentSecrets.v1";
    internal static bool IsManagedObjectId(string? objectId) =>
        string.Equals(objectId, ObjectId, StringComparison.Ordinal);
    private static string AccountCachePath => HostAssets.ResolveDataFilePath("environment-secret-vault-cache.dat");
    private const int SchemaVersion = 1;
    private const string WrapDomain = "yanzi.environment-secret-vault.wrap.v1";
    private const string RecoveryWrapDomain = "yanzi.environment-secret-vault.recovery-wrap.v1";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object ClientGate = new();
    private static readonly object AccountCacheGate = new();
    private static WeakReference<CloudSyncClient>? _client;
    private static CancellationTokenSource? _uploadDebounce;
    private static string? _pendingAccountId;
    private static readonly HashSet<string> PendingChangedNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PendingDeletedNames = new(StringComparer.OrdinalIgnoreCase);

    internal static void Attach(CloudSyncClient client)
    {
        lock (ClientGate)
        {
            var accountId = client.CurrentUserId;
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                if (!string.IsNullOrWhiteSpace(_pendingAccountId) &&
                    !string.Equals(_pendingAccountId, accountId, StringComparison.Ordinal))
                {
                    PendingChangedNames.Clear();
                    PendingDeletedNames.Clear();
                }
                _pendingAccountId = accountId;
            }
            _client = new WeakReference<CloudSyncClient>(client);
        }
    }

    internal static void QueueUploadFromLocal(
        IEnumerable<string>? changedNames = null,
        IEnumerable<string>? deletedNames = null)
    {
        CloudSyncClient? client = null;
        CancellationToken token;
        string expectedAccountId;
        lock (ClientGate)
        {
            _client?.TryGetTarget(out client);
            var accountId = client?.CurrentUserId ?? SyncSessionStore.Load()?.UserId;
            if (string.IsNullOrWhiteSpace(accountId)) return;
            expectedAccountId = accountId;
            if (!string.IsNullOrWhiteSpace(_pendingAccountId) &&
                    !string.Equals(_pendingAccountId, accountId, StringComparison.Ordinal))
                {
                    PendingChangedNames.Clear();
                    PendingDeletedNames.Clear();
                }
            _pendingAccountId = accountId;
            if (changedNames != null)
                foreach (var name in changedNames.Where(AppEnvironmentVariableStore.IsValidEnvironmentName))
                    PendingChangedNames.Add(AppEnvironmentVariableStore.NormalizeName(name));
            if (deletedNames != null)
                foreach (var name in deletedNames.Where(AppEnvironmentVariableStore.IsValidEnvironmentName))
                    PendingDeletedNames.Add(AppEnvironmentVariableStore.NormalizeName(name));
            _uploadDebounce?.Cancel();
            _uploadDebounce?.Dispose();
            _uploadDebounce = new CancellationTokenSource();
            token = _uploadDebounce.Token;
        }
        PersistPending(expectedAccountId, changedNames, deletedNames);
        SaveAccountSnapshot(expectedAccountId, AppEnvironmentVariableStore.SnapshotSecretValuesForVault());
        if (client == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                string[] changed;
                string[] deletions;
                lock (ClientGate)
                {
                    changed = PendingChangedNames.ToArray();
                    deletions = PendingDeletedNames.ToArray();
                }
                if (await UploadLocalAsync(client, deletions, expectedAccountId, token))
                    CompletePending(expectedAccountId, changed, deletions);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Environment secret vault upload deferred: {ex.GetType().Name}");
            }
        }, token);
    }

    internal static void QueueRestoreFromCloud()
    {
        CloudSyncClient? client;
        lock (ClientGate)
        {
            if (_client == null || !_client.TryGetTarget(out client)) return;
        }

        _ = Task.Run(async () =>
        {
            try { await RestoreAfterAuthenticationAsync(client, CancellationToken.None); }
            catch (Exception ex) { HostAssets.AppendLog($"Environment secret vault refresh deferred: {ex.GetType().Name}"); }
        });
    }

    internal static async Task RestoreAfterAuthenticationAsync(
        CloudSyncClient client,
        CancellationToken cancellationToken = default)
    {
        Attach(client);
        if (string.IsNullOrWhiteSpace(client.CurrentUserId) || client.E2eeMasterKey is not { Length: 32 }) return;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var accountId = client.CurrentUserId!;
            PrepareLocalAccountScope(accountId);
            var local = AppEnvironmentVariableStore.SnapshotSecretValuesForVault();
            var pending = SnapshotPending(accountId);
            var recoveryKey = await client.GetSecretVaultRecoveryKeyAsync(cancellationToken);
            var record = await client.GetSyncObjectAsync(ObjectId, cancellationToken);
            if (record == null || record.Deleted)
            {
                if (local.Count > 0) await WriteVaultAsync(client, local, record, recoveryKey, cancellationToken);
                CompletePending(accountId, pending.Changed, pending.Deleted);
                return;
            }

            var remote = DecryptPayload(record.Payload, client.E2eeMasterKey, recoveryKey, out var usedRecoveryKey);
            var merged = new Dictionary<string, string>(remote, StringComparer.OrdinalIgnoreCase);
            var changed = usedRecoveryKey ||
                !RecoveryWrapperMatches(record.Payload, client.E2eeMasterKey, recoveryKey);
            foreach (var name in pending.Deleted)
                changed |= merged.Remove(name);
            foreach (var pair in local)
            {
                if (!merged.TryGetValue(pair.Key, out var remoteValue) || pending.Changed.Contains(pair.Key))
                {
                    if (!string.Equals(remoteValue, pair.Value, StringComparison.Ordinal)) changed = true;
                    merged[pair.Key] = pair.Value;
                }
            }

            AppEnvironmentVariableStore.ApplySecretValuesFromVault(merged);
            SaveAccountSnapshot(accountId, merged);
            if (changed) await WriteVaultAsync(client, merged, record, recoveryKey, cancellationToken);
            CompletePending(accountId, pending.Changed, pending.Deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Authentication must remain usable even if the vault service, local cache or
            // network is temporarily unavailable. Never overwrite data we cannot safely read.
            HostAssets.AppendLog($"Environment secret vault unavailable for this login: {ex.GetType().Name}");
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task<bool> UploadLocalAsync(
        CloudSyncClient client,
        IReadOnlyCollection<string>? deletedNames = null,
        string? expectedAccountId = null,
        CancellationToken cancellationToken = default)
    {
        await client.EnsureAuthenticatedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(client.CurrentUserId) || client.E2eeMasterKey is not { Length: 32 }) return false;
        if (!string.IsNullOrWhiteSpace(expectedAccountId) && !string.Equals(client.CurrentUserId, expectedAccountId, StringComparison.Ordinal)) return false;
        Attach(client);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var accountId = client.CurrentUserId!;
            PrepareLocalAccountScope(accountId);
            var local = AppEnvironmentVariableStore.SnapshotSecretValuesForVault();
            var recoveryKey = await client.GetSecretVaultRecoveryKeyAsync(cancellationToken);
            var current = await client.GetSyncObjectAsync(ObjectId, cancellationToken);
            var usedRecoveryKey = false;
            var remote = current == null || current.Deleted
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : DecryptPayload(current.Payload, client.E2eeMasterKey, recoveryKey, out usedRecoveryKey);
            var merged = new Dictionary<string, string>(remote, StringComparer.OrdinalIgnoreCase);
            if (deletedNames != null)
                foreach (var name in deletedNames) merged.Remove(name);
            foreach (var pair in local) merged[pair.Key] = pair.Value;
            if (current == null || current.Deleted || usedRecoveryKey ||
                !RecoveryWrapperMatches(current.Payload, client.E2eeMasterKey, recoveryKey) ||
                !SecretsEqual(merged, remote))
                await WriteVaultAsync(client, merged, current, recoveryKey, cancellationToken);
            SaveAccountSnapshot(accountId, merged);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void PrepareLocalAccountScope(string accountId)
    {
        lock (AccountCacheGate)
        {
            var cache = LoadLocalAccountCache();
            var current = AppEnvironmentVariableStore.SnapshotSecretValuesForVault();
            if (string.Equals(cache.LastAccountId, accountId, StringComparison.Ordinal))
            {
                if (!cache.Accounts.ContainsKey(accountId))
                {
                    cache.Accounts[accountId] = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
                    SaveLocalAccountCache(cache);
                }
                return;
            }

            if (cache.Accounts.TryGetValue(accountId, out var saved))
            {
                AppEnvironmentVariableStore.ApplySecretValuesFromVault(saved);
            }
            else if (cache.Accounts.Count == 0 && string.IsNullOrWhiteSpace(cache.LastAccountId))
            {
                cache.Accounts[accountId] = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AppEnvironmentVariableStore.ApplySecretValuesFromVault(empty);
                cache.Accounts[accountId] = empty;
            }

            cache.LastAccountId = accountId;
            SaveLocalAccountCache(cache);
        }
    }

    private static void PersistPending(
        string accountId,
        IEnumerable<string>? changedNames,
        IEnumerable<string>? deletedNames)
    {
        lock (AccountCacheGate)
        {
            var cache = LoadLocalAccountCache();
            if (!cache.PendingChanged.TryGetValue(accountId, out var changed))
                cache.PendingChanged[accountId] = changed = [];
            if (!cache.PendingDeleted.TryGetValue(accountId, out var deleted))
                cache.PendingDeleted[accountId] = deleted = [];

            if (changedNames != null)
            {
                foreach (var name in changedNames.Where(AppEnvironmentVariableStore.IsValidEnvironmentName))
                {
                    var normalized = AppEnvironmentVariableStore.NormalizeName(name);
                    if (!changed.Contains(normalized, StringComparer.OrdinalIgnoreCase)) changed.Add(normalized);
                    deleted.RemoveAll(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (deletedNames != null)
            {
                foreach (var name in deletedNames.Where(AppEnvironmentVariableStore.IsValidEnvironmentName))
                {
                    var normalized = AppEnvironmentVariableStore.NormalizeName(name);
                    if (!deleted.Contains(normalized, StringComparer.OrdinalIgnoreCase)) deleted.Add(normalized);
                    changed.RemoveAll(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
                }
            }
            SaveLocalAccountCache(cache);
        }
    }

    private static void SaveAccountSnapshot(string accountId, IReadOnlyDictionary<string, string> secrets)
    {
        lock (AccountCacheGate)
        {
            var cache = LoadLocalAccountCache();
            cache.LastAccountId = accountId;
            cache.Accounts[accountId] = new Dictionary<string, string>(secrets, StringComparer.OrdinalIgnoreCase);
            SaveLocalAccountCache(cache);
        }
    }

    private static LocalAccountSecretCache LoadLocalAccountCache()
    {
        if (!File.Exists(AccountCachePath)) return new LocalAccountSecretCache();
        try
        {
            var protectedBytes = File.ReadAllBytes(AccountCachePath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var parsed = JsonSerializer.Deserialize<LocalAccountSecretCache>(bytes, JsonOptions) ?? new LocalAccountSecretCache();
            parsed.Accounts ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            parsed.Accounts = parsed.Accounts.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(pair.Value ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);
            parsed.PendingChanged ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            parsed.PendingDeleted ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
            parsed.PendingChanged = parsed.PendingChanged.ToDictionary(
                pair => pair.Key,
                pair => (pair.Value ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.Ordinal);
            parsed.PendingDeleted = parsed.PendingDeleted.ToDictionary(
                pair => pair.Key,
                pair => (pair.Value ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.Ordinal);
            return parsed;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            throw new InvalidDataException("本机账号凭证缓存损坏。", ex);
        }
    }

    private static void SaveLocalAccountCache(LocalAccountSecretCache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AccountCachePath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cache, JsonOptions);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        var temporary = AccountCachePath + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, AccountCachePath, true);
    }

    private static (HashSet<string> Changed, HashSet<string> Deleted) SnapshotPending(string accountId)
    {
        HashSet<string> changed;
        HashSet<string> deleted;
        lock (ClientGate)
        {
            if (!string.Equals(_pendingAccountId, accountId, StringComparison.Ordinal))
            {
                changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                changed = new HashSet<string>(PendingChangedNames, StringComparer.OrdinalIgnoreCase);
                deleted = new HashSet<string>(PendingDeletedNames, StringComparer.OrdinalIgnoreCase);
            }
        }

        lock (AccountCacheGate)
        {
            var cache = LoadLocalAccountCache();
            if (cache.PendingChanged.TryGetValue(accountId, out var savedChanged))
                changed.UnionWith(savedChanged);
            if (cache.PendingDeleted.TryGetValue(accountId, out var savedDeleted))
                deleted.UnionWith(savedDeleted);
        }
        return (changed, deleted);
    }

    private static void CompletePending(string? accountId, IEnumerable<string> changed, IEnumerable<string> deleted)
    {
        if (string.IsNullOrWhiteSpace(accountId)) return;
        var changedArray = changed.ToArray();
        var deletedArray = deleted.ToArray();
        lock (ClientGate)
        {
            if (string.Equals(_pendingAccountId, accountId, StringComparison.Ordinal))
            {
                foreach (var name in changedArray) PendingChangedNames.Remove(name);
                foreach (var name in deletedArray) PendingDeletedNames.Remove(name);
            }
        }
        lock (AccountCacheGate)
        {
            var cache = LoadLocalAccountCache();
            if (cache.PendingChanged.TryGetValue(accountId, out var savedChanged))
                cache.PendingChanged[accountId] = savedChanged.Except(changedArray, StringComparer.OrdinalIgnoreCase).ToList();
            if (cache.PendingDeleted.TryGetValue(accountId, out var savedDeleted))
                cache.PendingDeleted[accountId] = savedDeleted.Except(deletedArray, StringComparer.OrdinalIgnoreCase).ToList();
            SaveLocalAccountCache(cache);
        }
    }

    private static bool SecretsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
            if (!right.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal)) return false;
        return true;
    }

    internal static async Task RewrapAfterPasswordChangeAsync(
        CloudSyncClient client,
        byte[]? previousMasterKey,
        byte[] newMasterKey,
        CancellationToken cancellationToken = default)
    {
        if (newMasterKey.Length != 32) return;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var record = await client.GetSyncObjectAsync(ObjectId, cancellationToken);
            if (record == null || record.Deleted) return;
            var payload = record.Payload.Deserialize<SecretVaultPayload>(JsonOptions);
            if (payload == null || string.IsNullOrWhiteSpace(payload.WrappedVaultKey)) return;

            var recoveryKey = await client.GetSecretVaultRecoveryKeyAsync(cancellationToken);
            var vaultKey = UnwrapVaultKey(payload, previousMasterKey, recoveryKey, out _);
            payload.WrappedVaultKey = WrapVaultKey(vaultKey, newMasterKey);
            payload.RecoveryWrappedVaultKey = WrapRecoveryVaultKey(vaultKey, recoveryKey);
            payload.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            await client.PutSyncObjectAsync(
                ObjectId,
                SchemaVersion,
                record.Revision,
                deleted: false,
                payload,
                DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
                DeviceIdentityStore.GetDesktopDisplayName(),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HostAssets.AppendLog($"Environment secret vault rewrap deferred: {ex.GetType().Name}");
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task WriteVaultAsync(
        CloudSyncClient client,
        IReadOnlyDictionary<string, string> secrets,
        CloudSyncObjectRecord? existing,
        byte[] recoveryKey,
        CancellationToken cancellationToken)
    {
        if (client.E2eeMasterKey is not { Length: 32 } masterKey) return;

        byte[] vaultKey;
        var expectedRevision = existing?.Revision ?? 0;
        if (existing != null && !existing.Deleted)
        {
            var existingPayload = existing.Payload.Deserialize<SecretVaultPayload>(JsonOptions);
            vaultKey = existingPayload == null
                ? RandomNumberGenerator.GetBytes(32)
                : UnwrapVaultKey(existingPayload, masterKey, recoveryKey, out _);
        }
        else
        {
            vaultKey = RandomNumberGenerator.GetBytes(32);
        }

        var canonical = secrets
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var plainJson = JsonSerializer.Serialize(canonical, JsonOptions);
        var payload = new SecretVaultPayload
        {
            Version = SchemaVersion,
            WrappedVaultKey = WrapVaultKey(vaultKey, masterKey),
            RecoveryWrappedVaultKey = WrapRecoveryVaultKey(vaultKey, recoveryKey),
            EncryptedSecrets = SyncCryptoService.Encrypt(plainJson, vaultKey),
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };

        await client.PutSyncObjectAsync(
            ObjectId,
            SchemaVersion,
            expectedRevision,
            deleted: false,
            payload,
            DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            DeviceIdentityStore.GetDesktopDisplayName(),
            cancellationToken);
    }

    private static Dictionary<string, string> DecryptPayload(
        JsonElement element,
        byte[] masterKey,
        byte[]? recoveryKey,
        out bool usedRecoveryKey)
    {
        var payload = element.Deserialize<SecretVaultPayload>(JsonOptions)
            ?? throw new InvalidDataException("账号凭证库格式无效。");
        if (payload.Version != SchemaVersion) throw new InvalidDataException("账号凭证库版本不受支持。");
        var vaultKey = UnwrapVaultKey(payload, masterKey, recoveryKey, out usedRecoveryKey);
        var plainJson = SyncCryptoService.Decrypt(payload.EncryptedSecrets, vaultKey);
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(plainJson, JsonOptions)
            ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    internal static void PrepareLocalAccountScopeForVerification(string accountId) =>
        PrepareLocalAccountScope(accountId);

    internal static void SaveAccountSnapshotForVerification(
        string accountId,
        IReadOnlyDictionary<string, string> secrets) =>
        SaveAccountSnapshot(accountId, secrets);

    internal static void PersistPendingForVerification(
        string accountId,
        IEnumerable<string>? changed,
        IEnumerable<string>? deleted) =>
        PersistPending(accountId, changed, deleted);

    internal static (HashSet<string> Changed, HashSet<string> Deleted) SnapshotPendingForVerification(string accountId) =>
        SnapshotPending(accountId);

    internal static void CompletePendingForVerification(
        string accountId,
        IEnumerable<string> changed,
        IEnumerable<string> deleted) =>
        CompletePending(accountId, changed, deleted);

    internal static JsonElement CreatePayloadForVerification(
        IReadOnlyDictionary<string, string> secrets,
        byte[] masterKey,
        byte[]? recoveryKey = null)
    {
        var vaultKey = RandomNumberGenerator.GetBytes(32);
        var canonical = secrets.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.SerializeToElement(new SecretVaultPayload
        {
            Version = SchemaVersion,
            WrappedVaultKey = WrapVaultKey(vaultKey, masterKey),
            RecoveryWrappedVaultKey = recoveryKey is { Length: 32 } ? WrapRecoveryVaultKey(vaultKey, recoveryKey) : string.Empty,
            EncryptedSecrets = SyncCryptoService.Encrypt(JsonSerializer.Serialize(canonical, JsonOptions), vaultKey),
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        }, JsonOptions);
    }

    internal static Dictionary<string, string> DecryptPayloadForVerification(
        JsonElement element,
        byte[] masterKey,
        byte[]? recoveryKey = null) =>
        DecryptPayload(element, masterKey, recoveryKey, out _);

    internal static (Dictionary<string, string> Secrets, bool UsedRecovery) DecryptPayloadWithRecoveryForVerification(
        JsonElement element,
        byte[] masterKey,
        byte[] recoveryKey)
    {
        var secrets = DecryptPayload(element, masterKey, recoveryKey, out var usedRecovery);
        return (secrets, usedRecovery);
    }

    internal static JsonElement RewrapPayloadForVerification(
        JsonElement element,
        byte[]? oldMasterKey,
        byte[] newMasterKey,
        byte[]? recoveryKey = null)
    {
        var payload = element.Deserialize<SecretVaultPayload>(JsonOptions)
            ?? throw new InvalidDataException("账号凭证库格式无效。");
        var vaultKey = UnwrapVaultKey(payload, oldMasterKey, recoveryKey, out _);
        payload.WrappedVaultKey = WrapVaultKey(vaultKey, newMasterKey);
        if (recoveryKey is { Length: 32 })
            payload.RecoveryWrappedVaultKey = WrapRecoveryVaultKey(vaultKey, recoveryKey);
        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    private static bool RecoveryWrapperMatches(JsonElement element, byte[] masterKey, byte[] recoveryKey)
    {
        try
        {
            var payload = element.Deserialize<SecretVaultPayload>(JsonOptions);
            if (payload == null ||
                string.IsNullOrWhiteSpace(payload.WrappedVaultKey) ||
                string.IsNullOrWhiteSpace(payload.RecoveryWrappedVaultKey))
                return false;
            var primary = DecodeVaultKey(SyncCryptoService.Decrypt(
                payload.WrappedVaultKey,
                DeriveWrapKey(masterKey, WrapDomain)));
            var recovery = DecodeVaultKey(SyncCryptoService.Decrypt(
                payload.RecoveryWrappedVaultKey,
                DeriveWrapKey(recoveryKey, RecoveryWrapDomain)));
            return CryptographicOperations.FixedTimeEquals(primary, recovery);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string WrapVaultKey(byte[] vaultKey, byte[] masterKey) =>
        SyncCryptoService.Encrypt(Convert.ToBase64String(vaultKey), DeriveWrapKey(masterKey, WrapDomain));

    private static string WrapRecoveryVaultKey(byte[] vaultKey, byte[] recoveryKey) =>
        SyncCryptoService.Encrypt(Convert.ToBase64String(vaultKey), DeriveWrapKey(recoveryKey, RecoveryWrapDomain));

    private static byte[] UnwrapVaultKey(
        SecretVaultPayload payload,
        byte[]? masterKey,
        byte[]? recoveryKey,
        out bool usedRecoveryKey)
    {
        usedRecoveryKey = false;
        if (masterKey is { Length: 32 } && !string.IsNullOrWhiteSpace(payload.WrappedVaultKey))
        {
            try
            {
                return DecodeVaultKey(SyncCryptoService.Decrypt(
                    payload.WrappedVaultKey,
                    DeriveWrapKey(masterKey, WrapDomain)));
            }
            catch (CryptographicException) when (recoveryKey is { Length: 32 } && !string.IsNullOrWhiteSpace(payload.RecoveryWrappedVaultKey))
            {
                // Password may have changed. Fall through to the authenticated recovery wrapper.
            }
        }

        if (recoveryKey is { Length: 32 } && !string.IsNullOrWhiteSpace(payload.RecoveryWrappedVaultKey))
        {
            usedRecoveryKey = true;
            return DecodeVaultKey(SyncCryptoService.Decrypt(
                payload.RecoveryWrappedVaultKey,
                DeriveWrapKey(recoveryKey, RecoveryWrapDomain)));
        }

        throw new CryptographicException("凭证库密钥无法解锁。");
    }

    private static byte[] DecodeVaultKey(string raw)
    {
        var key = Convert.FromBase64String(raw);
        if (key.Length != 32) throw new CryptographicException("凭证库密钥长度无效。");
        return key;
    }

    private static byte[] DeriveWrapKey(byte[] sourceKey, string domain)
    {
        using var hmac = new HMACSHA256(sourceKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(domain));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class LocalAccountSecretCache
    {
        public int Version { get; set; } = 1;
        public string? LastAccountId { get; set; }
        public Dictionary<string, Dictionary<string, string>> Accounts { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> PendingChanged { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> PendingDeleted { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class SecretVaultPayload
    {
        public int Version { get; set; } = SchemaVersion;
        public string WrappedVaultKey { get; set; } = string.Empty;
        public string RecoveryWrappedVaultKey { get; set; } = string.Empty;
        public string EncryptedSecrets { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }
}
