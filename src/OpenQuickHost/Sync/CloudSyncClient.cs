using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Yanzi.Core;

namespace OpenQuickHost.Sync;

public sealed partial class CloudSyncClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _directHttpClient;
    private readonly HttpClient _largeTransferHttpClient;
    private readonly HttpClient _directLargeTransferHttpClient;
    private readonly SyncOptions _options;
    private SyncSession? _session;
    private SavedCredential? _credential;
    private readonly object _transportRetryLock = new();
    private int _consecutiveTransportFailures;
    private DateTimeOffset _transportRetryAfterUtc;
    public AccountSyncCoordinator AccountCoordinator { get; }
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);

    public byte[]? E2eeMasterKey { get; private set; }

    private (byte[] MasterKey, string LoginHash) DeriveSyncKeys(string email, string password)
    {
        return SyncCryptoService.DeriveKeys(password, email);
    }

    public CloudSyncClient(SyncOptions options, AccountSyncCoordinator? coordinator = null)
    {
        AccountCoordinator = coordinator ?? new AccountSyncCoordinator();
        _options = options;
        _httpClient = CreateHttpClient(options.BaseUrl, useProxy: true, TimeSpan.FromSeconds(30));
        _directHttpClient = CreateHttpClient(options.BaseUrl, useProxy: false, TimeSpan.FromSeconds(30));
        _largeTransferHttpClient = CreateHttpClient(options.BaseUrl, useProxy: true, TimeSpan.FromMinutes(2));
        _directLargeTransferHttpClient = CreateHttpClient(options.BaseUrl, useProxy: false, TimeSpan.FromMinutes(2));
        _session = SyncSessionStore.Load();
        _credential = SecureCredentialStore.Load();
    }

    private DateTime _sessionDiskTime;
    private DateTime _credentialDiskTime;
    public void AdoptRuntimeEncryptionKey(string account, byte[] key)
    {
        if (CurrentUserId != account || key.Length != 32) throw new InvalidOperationException("账号状态已变化。");
        E2eeMasterKey = key.ToArray();
    }
    public async Task ReloadPersistedSessionAsync()
    {
        var sessionTime = File.GetLastWriteTimeUtc(SyncSessionStore.SessionPath);
        var credentialTime = File.GetLastWriteTimeUtc(SecureCredentialStore.CredentialPath);
        if (sessionTime == _sessionDiskTime && credentialTime == _credentialDiskTime) return;
        await _authenticationLock.WaitAsync();
        try
        {
            var session = SyncSessionStore.Load();
            var credential = SecureCredentialStore.Load();
            var accountChanged = session?.UserId != _session?.UserId;
            if (accountChanged || session?.AccessToken != _session?.AccessToken)
                AccountCoordinator.Invalidate();
            _session = session;
            _credential = credential;
            E2eeMasterKey = credential != null ? DeriveSyncKeys(credential.LoginEmail, credential.Password).MasterKey
                : accountChanged || session == null ? null : E2eeMasterKey;
            _sessionDiskTime = sessionTime;
            _credentialDiskTime = credentialTime;
        }
        finally { _authenticationLock.Release(); }
    }

    public string CurrentUserLabel =>
        _session != null
            ? $"{_session.Username} ({_session.UserId})"
            : !string.IsNullOrWhiteSpace(_credential?.LoginEmail)
                ? _credential!.LoginEmail
                : "未登录";

    public string? CurrentUserId => _session?.UserId;
    internal string TaskJournalAccount => _options.BaseUrl.TrimEnd('/') + "\n" + CurrentUserId;

    public bool HasCredential => !string.IsNullOrWhiteSpace(_credential?.LoginEmail) && !string.IsNullOrWhiteSpace(_credential?.Password);

    public string? GetSavedPassword() => _credential?.Password;

    public void SetCredential(string email, string password, bool remember)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("邮箱不能为空。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("密码不能为空。");
        }

        var normalizedEmail = email.Trim();
        var normalizedPassword = password.Trim();

        CloudSyncDiagnostics.Log(
            "CloudSyncClient.Auth",
            "Credential updated",
            ("email", normalizedEmail),
            ("remember", remember),
            ("passwordLength", normalizedPassword.Length));

        // 本地派生并缓存 E2eeMasterKey
        var keys = DeriveSyncKeys(normalizedEmail, normalizedPassword);
        E2eeMasterKey = keys.MasterKey;

        _credential = new SavedCredential
        {
            Email = normalizedEmail,
            Password = normalizedPassword
        };

        if (remember)
        {
            SecureCredentialStore.Save(_credential);
        }
        else
        {
            SecureCredentialStore.Clear();
        }

        ClearSession();
    }

    public void ClearCredential()
    {
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Credential cleared");
        _credential = null;
        E2eeMasterKey = null;
        SecureCredentialStore.Clear();
        ClearSession();
    }

    public void ClearSessionOnly()
    {
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Session cleared only");
        ClearSession();
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        var expectedGeneration = AccountCoordinator.Generation;
        await _authenticationLock.WaitAsync(cancellationToken);
        try { AccountCoordinator.RequireCurrent(expectedGeneration); await EnsureAuthenticatedCoreAsync(cancellationToken); }
        finally { _authenticationLock.Release(); }
    }

    private async Task EnsureAuthenticatedCoreAsync(CancellationToken cancellationToken)
    {
        if (HasValidSession())
        {
            if (E2eeMasterKey == null && HasCredential)
            {
                var restoredKeys = DeriveSyncKeys(_credential!.LoginEmail, _credential.Password);
                E2eeMasterKey = restoredKeys.MasterKey;
                CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "E2eeMasterKey restored from saved credential for existing session");
            }
            return;
        }

        if (!HasCredential)
        {
            CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Authentication blocked: missing credential");
            throw new InvalidOperationException("缺少登录凭据，请先登录。");
        }

        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Authenticating with saved credential", ("email", _credential?.LoginEmail));

        // 自动登录时，基于本地存储的凭据重新派生并缓存 E2eeMasterKey
        var keys = DeriveSyncKeys(_credential!.LoginEmail, _credential.Password);
        E2eeMasterKey = keys.MasterKey;

        // 这里传递明文密码，LoginAsync 内部会将其转为 LoginHash 进行网络传输
        var authenticated = await LoginAsync(_credential.LoginEmail, _credential.Password, cancellationToken);
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Authentication completed", ("userId", authenticated.UserId), ("username", authenticated.Username));
    }

    public async Task<SendCodeResponse> SendRegistrationCodeAsync(string email, string username, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            email = email.Trim(),
            username = username.Trim()
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/send-code", payload, includeAuth: false, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<SendCodeResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("验证码响应为空。");
    }

    public async Task<SyncSession> RegisterAsync(string email, string username, string password, string code, CancellationToken cancellationToken = default)
    {
        var generation = AccountCoordinator.Generation;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("密码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("验证码不能为空。");
        }

        var normalizedPassword = password.Trim();
        var normalizedCode = code.Trim();

        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Register requested", ("email", email), ("username", username), ("passwordLength", normalizedPassword.Length), ("codeLength", normalizedCode.Length));

        // 注册时派生并缓存 E2eeMasterKey
        var keys = DeriveSyncKeys(email, normalizedPassword);
        E2eeMasterKey = keys.MasterKey;

        var payload = new
        {
            email = email.Trim(),
            username = username.Trim(),
            password = keys.LoginHash, // 发送 LoginHash 给云端
            code = normalizedCode
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/register", payload, includeAuth: false, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var session = await ReadSessionAsync(response, cancellationToken);
        AccountCoordinator.Commit(generation, () => { _session = session; SyncSessionStore.Save(session); });
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Register completed", ("userId", session.UserId), ("username", session.Username));
        return session;
    }

    public async Task<SyncSession> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var generation = AccountCoordinator.Generation;
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Login requested", ("email", email), ("passwordLength", password?.Length ?? 0));

        var normalizedPassword = password ?? string.Empty;
        var keys = DeriveSyncKeys(email, normalizedPassword);
        E2eeMasterKey = keys.MasterKey;

        var payload = new
        {
            email = email.Trim(),
            password = keys.LoginHash,
            authVersion = "e2ee-v1",
            legacyPassword = normalizedPassword
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/login", payload, includeAuth: false, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            ClearSession();
            // 登录失败时不清除本地加密记住的凭据文件，避免因后端暂时网络波动或接口不可用导致本地凭据被强行抹除。
            // 这样既能在网络恢复后自动重连，也能在需要重新登录时在弹窗中保留自动填充邮箱的能力。
            CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Login rejected", ("email", email), ("statusCode", (int)response.StatusCode));
            throw new InvalidOperationException("邮箱或密码错误。");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var session = await ReadSessionAsync(response, cancellationToken);
        AccountCoordinator.Commit(generation, () => { _session = session; SyncSessionStore.Save(session); });
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Login completed", ("userId", session.UserId), ("username", session.Username));
        return session;
    }

    public async Task<SendCodeResponse> SendPasswordResetCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            email = email.Trim()
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/send-reset-code", payload, includeAuth: false, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<SendCodeResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("重置验证码响应为空。");
    }

    public async Task<SyncSession> ResetPasswordAsync(string email, string password, string code, CancellationToken cancellationToken = default)
    {
        var generation = AccountCoordinator.Generation;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("密码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("验证码不能为空。");
        }

        var normalizedPassword = password.Trim();
        var normalizedCode = code.Trim();

        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Reset password requested", ("email", email), ("passwordLength", normalizedPassword.Length), ("codeLength", normalizedCode.Length));

        // 重置密码时派生并缓存 E2eeMasterKey
        var keys = DeriveSyncKeys(email, normalizedPassword);
        E2eeMasterKey = keys.MasterKey;

        var payload = new
        {
            email = email.Trim(),
            password = keys.LoginHash, // 发送 LoginHash 给云端
            code = normalizedCode
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/reset-password", payload, includeAuth: false, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var session = await ReadSessionAsync(response, cancellationToken);
        AccountCoordinator.Commit(generation, () => { _session = session; SyncSessionStore.Save(session); });
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Reset password completed", ("userId", session.UserId), ("username", session.Username));
        return session;
    }

    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsyncWithFallback(HttpMethod.Get, "/health", includeAuth: false, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<HealthResponse>(response, cancellationToken);
    }

    public async Task<AuthMeResponse?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/auth/me", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<AuthMeResponse>(response, cancellationToken);
    }

    public async Task<VipStatusResponse?> GetVipStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/user/vip-status", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var status = await ReadAsync<VipStatusResponse>(response, cancellationToken);
        if (status != null && _session != null)
        {
            _session.IsVip = status.IsVip;
            _session.VipType = status.VipType;
            _session.VipExpireAt = status.VipExpireAt;
            _session.DaysRemaining = status.DaysRemaining;
            SyncSessionStore.Save(_session);
        }
        return status;
    }

    public async Task<RedeemLicenseResponse?> RedeemLicenseAsync(string code, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var payload = new { code = code.Trim().ToUpperInvariant() };
        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/licenses/redeem", payload, includeAuth: true, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<RedeemLicenseResponse>(response, cancellationToken);
        if (result != null && result.Ok && _session != null)
        {
            _session.IsVip = result.IsVip;
            _session.VipType = result.VipType;
            _session.VipExpireAt = result.VipExpireAt;
            _session.DaysRemaining = result.DaysRemaining;
            SyncSessionStore.Save(_session);
        }
        return result;
    }

    public async Task<IReadOnlyList<CloudExtensionRecord>> GetExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var cacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var response = await SendAsyncWithFallback(
            HttpMethod.Get,
            $"/v1/extensions?_ts={cacheBust}",
            includeAuth: false,
            cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await ReadAsync<ExtensionListResponse>(response, cancellationToken);
        return payload?.Items ?? [];
    }

    public async Task<IReadOnlyList<UserExtensionRecord>> GetUserExtensionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/me/extensions", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await ReadAsync<UserExtensionListResponse>(response, cancellationToken);
        return payload?.Items ?? [];
    }

    public async Task UpsertExtensionAsync(CommandItem command, string? iconOverride = null, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var accentHex = System.Windows.Application.Current.Dispatcher.CheckAccess()
            ? command.AccentBrush?.ToString()
            : System.Windows.Application.Current.Dispatcher.Invoke(() => command.AccentBrush?.ToString());

        var body = JsonSerializer.Serialize(new
        {
            manifest = new
            {
                name = command.ExtensionId,
                displayName = command.Title,
                version = command.DeclaredVersion,
                category = command.Category,
                description = command.Subtitle,
                accentHex = accentHex,
                keywords = command.Keywords,
                icon = string.IsNullOrWhiteSpace(iconOverride) ? command.IconReference : iconOverride,
                queryPrefixes = command.QueryPrefixes,
                queryTargetTemplate = command.QueryTargetTemplate,
                globalShortcut = (string?)null,
                hotkeyBehavior = command.HotkeyBehavior,
                runtime = command.Runtime,
                entryMode = command.EntryMode,
                entry = command.EntryPoint,
                permissions = command.Permissions,
                script = string.IsNullOrWhiteSpace(command.InlineScriptSource)
                    ? null
                    : new
                    {
                        source = command.InlineScriptSource
                    },
                hostedView = command.HostedView == null
                    ? null
                    : new
                    {
                        type = command.HostedView.Type,
                        title = command.HostedView.Title,
                        description = command.HostedView.Description,
                        inputLabel = command.HostedView.InputLabel,
                        inputPlaceholder = command.HostedView.InputPlaceholder,
                        outputLabel = command.HostedView.OutputLabel,
                        actionButtonText = command.HostedView.ActionButtonText,
                        actionType = command.HostedView.ActionType,
                        outputTemplate = command.HostedView.OutputTemplate,
                        emptyState = command.HostedView.EmptyState
                    }
            }
        });

        using var request = CreateJsonRequest(HttpMethod.Put, $"/v1/extensions/{Uri.EscapeDataString(command.ExtensionId)}", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpsertPrivateExtensionAsync(CommandItem command, string? iconOverride = null, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            manifest = BuildManifestPayload(command, iconOverride)
        });

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/v1/me/extensions/{Uri.EscapeDataString(command.ExtensionId)}/private",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<string?> PublishIconAsync(CommandItem command, string version, CancellationToken cancellationToken = default)
    {
        return await UploadIconAsync(command, version, privateLibrary: false, cancellationToken);
    }

    public async Task<string?> PublishPrivateIconAsync(CommandItem command, string version, CancellationToken cancellationToken = default)
    {
        return await UploadIconAsync(command, version, privateLibrary: true, cancellationToken);
    }

    private async Task<string?> UploadIconAsync(CommandItem command, string version, bool privateLibrary, CancellationToken cancellationToken)
    {
        var iconReference = command.IconReference?.Trim();
        if (string.IsNullOrWhiteSpace(iconReference) || ExtensionIconLibrary.IsBuiltInReference(iconReference))
        {
            return iconReference;
        }

        if (Uri.TryCreate(iconReference, UriKind.Absolute, out var absoluteUri) &&
            (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return iconReference;
        }

        var localPath = ExtensionIconLibrary.ResolveLocalIconFilePath(iconReference, command.ExtensionDirectoryPath);
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            return iconReference;
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var path = privateLibrary
            ? $"/v1/me/extensions/{Uri.EscapeDataString(command.ExtensionId)}/icon"
            : $"/v1/extensions/{Uri.EscapeDataString(command.ExtensionId)}/icon";
        using var request = CreateRequest(
            HttpMethod.Put,
            $"{path}?version={Uri.EscapeDataString(version)}&filename={Uri.EscapeDataString(Path.GetFileName(localPath))}",
            includeAuth: true);
        request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(localPath, cancellationToken));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(localPath));
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await ReadAsync<UploadIconResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("图标上传响应为空。");
        return string.IsNullOrWhiteSpace(payload.IconUrl) ? iconReference : payload.IconUrl;
    }

    public async Task UpsertUserExtensionAsync(CommandItem command, string? iconOverride = null, bool hasArchive = false, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var icon = string.IsNullOrWhiteSpace(iconOverride) ? command.IconReference : iconOverride;
        var accentHex = System.Windows.Application.Current.Dispatcher.CheckAccess()
            ? command.AccentBrush?.ToString()
            : System.Windows.Application.Current.Dispatcher.Invoke(() => command.AccentBrush?.ToString());

        var body = JsonSerializer.Serialize(new
        {
            installedVersion = command.DeclaredVersion,
            enabled = true,
            settings = new
            {
                source = "openquickhost-desktop",
                title = command.Title,
                name = command.Title,
                displayName = command.Title,
                description = command.Subtitle,
                icon,
                accentHex = accentHex,
                hasArchive,
                packageScope = hasArchive ? "private-account" : "",
                manifest = new
                {
                    id = command.ExtensionId,
                    name = command.Title,
                    displayName = command.Title,
                    version = command.DeclaredVersion,
                    category = command.Category,
                    description = command.Subtitle,
                    icon,
                    accentHex = accentHex,
                    runtime = command.Runtime,
                    entryMode = command.EntryMode
                }
            }
        });

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/v1/me/extensions/{Uri.EscapeDataString(command.ExtensionId)}",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/v1/extensions/{Uri.EscapeDataString(extensionId)}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemoveUserExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/v1/me/extensions/{Uri.EscapeDataString(extensionId)}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<T?> GetUserConfigAsync<T>(string configId, CancellationToken cancellationToken = default)
    {
        var items = await GetUserExtensionsAsync(cancellationToken);
        var record = items.FirstOrDefault(item =>
            item.ExtensionId.Equals(configId, StringComparison.OrdinalIgnoreCase));
        if (record == null || string.IsNullOrWhiteSpace(record.SettingsJson))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(record.SettingsJson);
    }

    public async Task UpsertUserConfigAsync(string configId, object settings, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        await EnsureConfigExtensionExistsAsync(configId, cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            installedVersion = "1",
            enabled = true,
            settings
        });

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/v1/me/extensions/{Uri.EscapeDataString(configId)}",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CloudSyncObjectListResponse> GetSyncObjectsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/sync/objects", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<CloudSyncObjectListResponse>(response, cancellationToken)
            ?? new CloudSyncObjectListResponse();
    }

    public async Task<CloudSyncCapabilitiesResponse> GetSyncCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/sync/capabilities", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<CloudSyncCapabilitiesResponse>(response, cancellationToken)
            ?? new CloudSyncCapabilitiesResponse();
    }

    public async Task<CloudSyncObjectRecord?> GetSyncObjectAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new ArgumentException("同步对象 ID 不能为空。", nameof(objectId));
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/sync/objects/{Uri.EscapeDataString(objectId.Trim())}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<CloudSyncObjectWriteResponse>(response, cancellationToken);
        return result?.Object;
    }

    public async Task<CloudSyncObjectListResponse> GetSyncChangesAsync(
        long sinceRevision,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (sinceRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sinceRevision));
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 500);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/sync/changes?since={sinceRevision}&limit={normalizedLimit}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<CloudSyncObjectListResponse>(response, cancellationToken)
            ?? new CloudSyncObjectListResponse();
    }

    public async Task<CloudSyncObjectRecord> PutSyncObjectAsync(
        string objectId,
        int schemaVersion,
        long expectedRevision,
        bool deleted,
        object payload,
        string? updatedByDeviceId,
        string? updatedByDeviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new ArgumentException("同步对象 ID 不能为空。", nameof(objectId));
        }
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            schemaVersion,
            expectedRevision,
            deleted,
            payload,
            updatedByDeviceId,
            updatedByDeviceName
        });
        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/v1/sync/objects/{Uri.EscapeDataString(objectId.Trim())}",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<CloudSyncObjectWriteResponse>(response, cancellationToken);
        return result?.Object ?? throw new InvalidDataException("云端未返回写入后的同步对象。");
    }

    public async Task<CloudSyncObjectHistoryResponse> GetSyncObjectHistoryAsync(
        string objectId,
        long beforeRevision = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new ArgumentException("同步对象 ID 不能为空。", nameof(objectId));
        }
        if (beforeRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(beforeRevision));
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/sync/history?objectId={Uri.EscapeDataString(objectId.Trim())}&before={beforeRevision}&limit={normalizedLimit}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<CloudSyncObjectHistoryResponse>(response, cancellationToken)
            ?? new CloudSyncObjectHistoryResponse();
    }

    public async Task<CloudSyncObjectRecord> RestoreSyncObjectAsync(
        string objectId,
        long expectedRevision,
        long restoreRevision,
        string? updatedByDeviceId,
        string? updatedByDeviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new ArgumentException("同步对象 ID 不能为空。", nameof(objectId));
        }
        if (expectedRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }
        if (restoreRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restoreRevision));
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            expectedRevision,
            restoreRevision,
            updatedByDeviceId,
            updatedByDeviceName
        });
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/v1/sync/objects/{Uri.EscapeDataString(objectId.Trim())}/restore",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<CloudSyncObjectWriteResponse>(response, cancellationToken);
        return result?.Object ?? throw new InvalidDataException("云端未返回恢复后的同步对象。");
    }

    public async Task<YanmStateResponse?> GetYanmStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/me/yanm-state", includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<YanmStateResponse>(response, cancellationToken);
    }

    public async Task<YanmStateResponse?> UpsertYanmStateAsync(YanmSettings yanm, string? updatedAtUtc = null, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            updatedAtUtc = string.IsNullOrWhiteSpace(updatedAtUtc) ? DateTime.UtcNow.ToString("O") : updatedAtUtc,
            yanm
        });

        using var request = CreateJsonRequest(HttpMethod.Put, "/v1/me/yanm-state", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<YanmStateResponse>(response, cancellationToken);
    }

    public async Task<YanmStateResponse?> PatchYanmComponentStateAsync(
        IReadOnlyDictionary<string, string> componentState,
        string? updatedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (componentState.Count == 0)
        {
            throw new ArgumentException("燕幕组件状态补丁不能为空。", nameof(componentState));
        }

        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            updatedAtUtc = string.IsNullOrWhiteSpace(updatedAtUtc) ? DateTime.UtcNow.ToString("O") : updatedAtUtc,
            componentState
        });

        using var request = CreateJsonRequest(HttpMethod.Put, "/v1/me/yanm-state/component-state", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<YanmStateResponse>(response, cancellationToken);
    }

    public async Task RegisterDeviceAsync(
        string deviceId,
        string platform,
        string displayName,
        object? capabilities = null,
        string? pushToken = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            deviceId,
            platform,
            displayName,
            pushToken,
            capabilities = capabilities ?? new { }
        });

        using var request = CreateJsonRequest(HttpMethod.Post, "/v1/me/devices", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<DeviceMessageListResponse> GetPendingDeviceMessagePageAsync(
        string deviceId,
        long after = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/me/mobile/messages?deviceId={Uri.EscapeDataString(deviceId)}&limit={limit}&after={after}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<DeviceMessageListResponse>(response, cancellationToken)
            ?? new DeviceMessageListResponse { DeviceId = deviceId, NextCursor = after, HighWater = after };
    }

    public async Task<IReadOnlyList<DeviceMessageRecord>> GetPendingDeviceMessagesAsync(
        string deviceId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var items = (await GetPendingDeviceMessagePageAsync(deviceId, 0, limit, cancellationToken)).Items;
        var taskAccount = TaskJournalAccount;
        foreach (var message in items) PlatformTaskJournal.Observed(taskAccount, message);
        return items;
    }

    public async Task<string> SendDeviceMessageAsync(
        string sourceDeviceId,
        string targetPlatform,
        string kind,
        string title,
        string text,
        string? targetDeviceId = null,
        object? payload = null,
        CancellationToken cancellationToken = default,
        string? clientMessageId = null,
        DateTimeOffset? expiresAt = null)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var taskAccount = TaskJournalAccount;
        clientMessageId ??= Guid.NewGuid().ToString("N");
        if (expiresAt == null && DeviceMessageOutbox.ReadSaved(CurrentUserId!, clientMessageId) is { } previous)
        {
            using var savedEnvelope = JsonDocument.Parse(previous);
            if (savedEnvelope.RootElement.TryGetProperty("expiresAt", out var savedExpiry) && savedExpiry.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(savedExpiry.GetString(), out var deadline)) expiresAt = deadline;
        }
        var body = JsonSerializer.Serialize(new
        {
            sourceDeviceId,
            targetDeviceId,
            targetPlatform,
            kind,
            title,
            text,
            payload = payload ?? new { },
            clientMessageId,
            expiresAt = (expiresAt ?? (YanziDeviceMessageProtocol.IsExecution(kind) ? DateTimeOffset.UtcNow.AddMinutes(2) : DateTimeOffset.UtcNow.AddDays(30))).ToString("O")
        });
        var saved = DeviceMessageOutbox.Save(CurrentUserId!, clientMessageId, body);
        PlatformTaskJournal.Envelope(taskAccount, body);
        string id;
        try { id = await SendSavedDeviceMessageAsync(body, cancellationToken); }
        catch (DeviceMessageRejectedException error) { PlatformTaskJournal.Envelope(taskAccount, body, status: "failed", result: "HTTP " + error.StatusCode); DeviceMessageOutbox.Fail(saved); throw; }
        catch (Exception error) { PlatformTaskJournal.Envelope(taskAccount, body, status: "unknown", result: error.Message); throw; }
        DeviceMessageOutbox.Complete(saved);
        return id;
    }

    private async Task<string> SendSavedDeviceMessageAsync(string body, CancellationToken cancellationToken)
    {
        var taskAccount = TaskJournalAccount;
        using var request = CreateJsonRequest(HttpMethod.Post, "/v1/me/mobile/messages", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        if ((int)response.StatusCode is 400 or 403 or 404 or 409 or 410 or 413 or 426)
            throw new DeviceMessageRejectedException((int)response.StatusCode);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<DeviceMessageCreateResponse>(response, cancellationToken);
        PlatformTaskJournal.Envelope(taskAccount, body, result?.MessageId, "submitted");
        return result?.MessageId ?? string.Empty;
    }

    // Capability invocations and background replay use different client instances.
    // Serialize their durable jobs before uploading or constructing an envelope.
    private static readonly SemaphoreSlim _chatDeliveryGate = new(1, 1);
    public async Task<string> DeliverChatJobAsync(DesktopChatJob job, CancellationToken cancellationToken = default)
    {
        await _chatDeliveryGate.WaitAsync(cancellationToken);
        try
        {
        await EnsureAuthenticatedAsync(cancellationToken);
        job = DesktopChatOutbox.Find(job) ?? throw new InvalidOperationException("outbox_job_missing");
        if (job.CompletedMessageId != null) return job.CompletedMessageId;
        if (job.Account != CurrentUserId) throw new InvalidOperationException("outbox_account_mismatch");
        if (DateTimeOffset.UtcNow - job.CreatedAt > TimeSpan.FromDays(30))
        { DesktopChatOutbox.Complete(job); throw new InvalidOperationException("message_expired"); }
        await RegisterDeviceAsync(job.Source, "desktop", DeviceIdentityStore.GetDesktopDisplayName(), cancellationToken: cancellationToken);
        if (job.FilePath != null && job.Attachment == null)
        {
            job = job with { Attachment = await UploadMobileAttachmentAsync(job.FilePath, cancellationToken) };
            DesktopChatOutbox.Save(job);
        }
        object payload = job.Attachment is { } attachment ? new {
            source = "desktop-chat", attachmentId = attachment.AttachmentId, fileName = attachment.FileName,
            size = attachment.Size, sha256 = attachment.Sha256, contentType = attachment.ContentType,
            clientTransferId = job.Id, clientOperationId = job.Id
        } : new { source = "desktop-chat", clientTransferId = job.Id, clientOperationId = job.Id };
        var id = await SendDeviceMessageAsync(job.Source, "android", job.Kind, "YanziChat", job.Text,
            targetDeviceId: job.TargetDeviceId, payload: payload, cancellationToken: cancellationToken, clientMessageId: job.Id, expiresAt: job.CreatedAt.AddDays(30));
        DesktopChatOutbox.Complete(job, id);
        return id;
        } finally { _chatDeliveryGate.Release(); }
    }

    private readonly SemaphoreSlim _outboxReplay = new(1, 1);
    public async Task ReplayDeviceOutboxAsync(CancellationToken cancellationToken = default)
    {
        if (!await _outboxReplay.WaitAsync(0, cancellationToken)) return;
        try
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            foreach (var job in DesktopChatOutbox.Pending(CurrentUserId!))
                try { await DeliverChatJobAsync(job, cancellationToken); }
                catch (DeviceMessageRejectedException error) { DesktopChatOutbox.Complete(job, "rejected:" + error.StatusCode); HostAssets.AppendLog("Chat outbox terminal rejection: " + error.StatusCode); }
            foreach (var path in DeviceMessageOutbox.Pending(CurrentUserId!))
            {
                var body = File.ReadAllText(path);
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("expiresAt", out var expiry) && expiry.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(expiry.GetString(), out var deadline) && deadline <= DateTimeOffset.UtcNow)
                { PlatformTaskJournal.Envelope(TaskJournalAccount, body, status: "expired"); DeviceMessageOutbox.Complete(path); HostAssets.AppendLog("Device outbox command expired before sending."); continue; }
                try { await SendSavedDeviceMessageAsync(body, cancellationToken); }
                catch (DeviceMessageRejectedException error) { PlatformTaskJournal.Envelope(TaskJournalAccount, body, status: "failed", result: "HTTP " + error.StatusCode); DeviceMessageOutbox.Fail(path); HostAssets.AppendLog("Device outbox terminal rejection: " + error.StatusCode); continue; }
                DeviceMessageOutbox.Complete(path);
            }
        }
        finally { _outboxReplay.Release(); }
    }

    public async Task<DeviceMessageRecord> QueryTaskMessageAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var taskAccount = TaskJournalAccount;
        using var request=CreateRequest(HttpMethod.Get,$"/v1/me/mobile/messages/{Uri.EscapeDataString(id)}",includeAuth:true);
        using var response=await SendAsyncWithFallback(request,cancellationToken);
        await EnsureSuccessAsync(response,cancellationToken);
        var message=await ReadAsync<DeviceMessageRecord>(response,cancellationToken) ?? throw new InvalidDataException("任务响应无效");
        PlatformTaskJournal.Observed(taskAccount,message); return message;
    }

    public async Task<bool> ClaimDeviceMessageAsync(string messageId, string deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateJsonRequest(HttpMethod.Post, $"/v1/me/mobile/messages/{Uri.EscapeDataString(messageId)}/claim",
            JsonSerializer.Serialize(new { deviceId }), includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("acquired").GetBoolean();
    }

    public async Task<HttpResponseMessage> GetMobileMessagesEventsStreamAsync(string deviceId, CancellationToken cancellationToken)
    {
        var generation = AccountCoordinator.Generation;
        ThrowIfTransportCoolingDown(cancellationToken);
        await EnsureAuthenticatedAsync(cancellationToken);
        AccountCoordinator.RequireCurrent(generation);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/me/mobile/messages/events?deviceId={Uri.EscapeDataString(deviceId)}");
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (HasValidSession())
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session!.AccessToken);
        }

        Exception? lastError = null;
        var attempts = new (HttpClient client, string label)[]
        {
            (_httpClient, "proxy"),
            (_directHttpClient, "direct"),
            (_httpClient, "proxy-retry")
        };
        for (var index = 0; index < attempts.Length; index++)
        {
            try
            {
                using var attemptRequest = await CloneRequestAsync(request, cancellationToken);
                var response = await attempts[index].client.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                try { AccountCoordinator.RequireCurrent(generation); response.EnsureSuccessStatusCode(); }
                catch { response.Dispose(); throw; }
                ResetTransportBackoff();
                return response;
            }
            catch (Exception ex) when (AccountCoordinator.Generation == generation && IsRetryableTransportException(ex, cancellationToken) && index < attempts.Length - 1)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (index + 1)), cancellationToken);
            }
            catch (Exception ex)
            {
                AccountCoordinator.RequireCurrent(generation);
                lastError = ex;
                var retryable = IsRetryableTransportException(ex, cancellationToken);
                if (retryable)
                {
                    RegisterTransportFailure();
                }
                throw new InvalidOperationException($"Failed to establish SSE connection: {ex.Message}", lastError);
            }
        }
        throw new InvalidOperationException("Failed to establish SSE connection.", lastError);
    }

    public async Task AckDeviceMessageAsync(
        string messageId,
        string deviceId,
        bool? success = null,
        string? result = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        object bodyObj;
        if (success.HasValue)
        {
            bodyObj = new
            {
                deviceId,
                success = success.Value,
                resultState = result?.StartsWith("execution_result_unknown", StringComparison.Ordinal) == true ? "unknown" : "executed",
                result = result ?? string.Empty
            };
        }
        else
        {
            bodyObj = new { deviceId };
        }

        var body = JsonSerializer.Serialize(bodyObj);
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/v1/me/mobile/messages/{Uri.EscapeDataString(messageId)}/ack",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task EnsureConfigExtensionExistsAsync(string configId, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            manifest = new
            {
                name = configId,
                displayName = "Yanzi WebDAV Settings",
                version = "1",
                category = "系统配置",
                description = "Stores WebDAV sync configuration for the current account.",
                keywords = new[] { "yanzi", "webdav", "settings" }
            }
        });

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/v1/extensions/{Uri.EscapeDataString(configId)}",
            body,
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UploadExtensionArchiveAsync(CommandItem command, byte[] packageBytes, string version, CancellationToken cancellationToken = default)
    {
        await UploadArchiveAsync(command, packageBytes, version, privateLibrary: false, expectedRevision: 0, cancellationToken);
    }

    public async Task UploadPrivateExtensionArchiveAsync(CommandItem command, byte[] packageBytes, string version, int expectedRevision = 0, CancellationToken cancellationToken = default)
    {
        await UploadArchiveAsync(command, packageBytes, version, privateLibrary: true, expectedRevision, cancellationToken);
    }

    private async Task UploadArchiveAsync(CommandItem command, byte[] packageBytes, string version, bool privateLibrary, int expectedRevision, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var path = privateLibrary
            ? $"/v1/me/extensions/{Uri.EscapeDataString(command.ExtensionId)}/archive"
            : $"/v1/extensions/{Uri.EscapeDataString(command.ExtensionId)}/archive";

        var url = $"{path}?version={Uri.EscapeDataString(version)}";
        if (privateLibrary)
        {
            url += $"&expectedRevision={expectedRevision}";
        }

        using var request = CreateRequest(
            HttpMethod.Put,
            url,
            includeAuth: true);
        request.Content = new ByteArrayContent(packageBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await SendAsyncWithFallback(
            request,
            cancellationToken,
            largeTransfer: true);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<byte[]> DownloadExtensionArchiveAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/extensions/{Uri.EscapeDataString(extensionId)}/archive",
            includeAuth: false);
        using var response = await SendAsyncWithFallback(
            request,
            cancellationToken,
            largeTransfer: true);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<byte[]> DownloadMyExtensionArchiveAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/me/extensions/{Uri.EscapeDataString(extensionId)}/archive",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(
            request,
            cancellationToken,
            largeTransfer: true);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<bool> CheckExtensionArchiveExistsAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsyncWithFallback(
                HttpMethod.Get,
                $"/v1/extensions/{Uri.EscapeDataString(extensionId)}/archive",
                includeAuth: false,
                cancellationToken: cancellationToken);
            HostAssets.AppendLog($"[StoreCheck] {extensionId} StatusCode={(int)response.StatusCode} ({response.StatusCode})");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[StoreCheck] {extensionId} Check FAILED with exception: {ex.Message}");
            return false;
        }
    }

    public static string CreateExtensionId(CommandItem command)
    {
        var chars = command.Title
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var collapsed = new string(chars);
        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        return collapsed.Trim('-');
    }

    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private bool HasValidSession()
    {
        return _session != null &&
               !string.IsNullOrWhiteSpace(_session.AccessToken) &&
               _session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
    }

    private void ClearSession()
    {
        AccountCoordinator.Invalidate(() => { _session = null; SyncSessionStore.Clear(); });
    }

    private static object BuildManifestPayload(CommandItem command, string? iconOverride = null)
    {
        return new
        {
            name = command.ExtensionId,
            displayName = command.Title,
            version = command.DeclaredVersion,
            category = command.Category,
            description = command.Subtitle,
            accentHex = System.Windows.Application.Current.Dispatcher.CheckAccess()
                ? command.AccentBrush?.ToString()
                : System.Windows.Application.Current.Dispatcher.Invoke(() => command.AccentBrush?.ToString()),
            keywords = command.Keywords,
            icon = string.IsNullOrWhiteSpace(iconOverride) ? command.IconReference : iconOverride,
            queryPrefixes = command.QueryPrefixes,
            queryTargetTemplate = command.QueryTargetTemplate,
            globalShortcut = command.GlobalShortcut,
            hotkeyBehavior = command.HotkeyBehavior,
            runtime = command.Runtime,
            entryMode = command.EntryMode,
            entry = command.EntryPoint,
            permissions = command.Permissions,
            script = string.IsNullOrWhiteSpace(command.InlineScriptSource)
                ? null
                : new
                {
                    source = command.InlineScriptSource
                },
            hostedView = command.HostedView == null
                ? null
                : new
                {
                    type = command.HostedView.Type,
                    title = command.HostedView.Title,
                    description = command.HostedView.Description,
                    inputLabel = command.HostedView.InputLabel,
                    inputPlaceholder = command.HostedView.InputPlaceholder,
                    outputLabel = command.HostedView.OutputLabel,
                    actionButtonText = command.HostedView.ActionButtonText,
                    actionType = command.HostedView.ActionType,
                    outputTemplate = command.HostedView.OutputTemplate,
                    emptyState = command.HostedView.EmptyState
                }
        };
    }

    private static async Task<SyncSession> ReadSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var auth = await ReadAsync<AuthResponse>(response, cancellationToken)
                   ?? throw new InvalidOperationException("登录响应为空。");
        return new SyncSession
        {
            AccessToken = auth.AccessToken,
            ExpiresAt = auth.ExpiresAt,
            UserId = auth.UserId,
            Username = auth.Username,
            Email = auth.Email,
            IsVip = auth.IsVip,
            VipType = auth.VipType,
            VipExpireAt = auth.VipExpireAt,
            DaysRemaining = auth.DaysRemaining
        };
    }

    public async Task<WebDavConfigDto?> FetchWebDavConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!HasValidSession())
        {
            CloudSyncDiagnostics.Log("CloudSyncClient.Config", "Fetch legacy WebDAV config skipped: no valid session");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/sync/webdav-config");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session!.AccessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No WebDAV config on server
                CloudSyncDiagnostics.Log("CloudSyncClient.Config", "Legacy WebDAV config endpoint returned not found");
                return null;
            }

            await EnsureSuccessAsync(response, cancellationToken);

            var dto = await ReadAsync<WebDavConfigDto>(response, cancellationToken);
            CloudSyncDiagnostics.Log(
                "CloudSyncClient.Config",
                "Legacy WebDAV config fetched",
                ("found", dto != null),
                ("hasPassword", !string.IsNullOrWhiteSpace(dto?.Password)),
                ("username", dto?.Username),
                ("serverUrl", dto?.ServerUrl));
            return dto;
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Log("CloudSyncClient.Config", "Legacy WebDAV config fetch failed", ("error", ex.Message));
            System.Diagnostics.Debug.WriteLine($"Failed to fetch WebDAV config: {ex.Message}");
            return null;
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfterSec = 60;
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    retryAfterSec = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                }
                else if (response.Headers.RetryAfter.Date.HasValue)
                {
                    retryAfterSec = (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                }
            }
            if (retryAfterSec < 1) retryAfterSec = 1;
            throw new InvalidOperationException($"请求过于频繁，请在 {retryAfterSec} 秒后重试。");
        }

        ErrorResponse? error = null;
        try
        {
            error = await ReadAsync<ErrorResponse>(response, cancellationToken);
        }
        catch
        {
            // Fall back to the generic status code error below.
        }

        var message = error?.Message;
        if (response.StatusCode == HttpStatusCode.Conflict &&
            string.Equals(error?.Error, "sync_revision_conflict", StringComparison.OrdinalIgnoreCase))
        {
            throw new CloudSyncRevisionConflictException(
                message ?? "同步对象 revision 冲突。",
                error?.Details?.ObjectId,
                error?.Details?.ExpectedRevision ?? 0,
                error?.Details?.CurrentRevision ?? 0);
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException(message);
        }

        throw new InvalidOperationException($"请求失败：{(int)response.StatusCode} {response.ReasonPhrase}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }

        public string? Message { get; set; }

        public SyncConflictDetails? Details { get; set; }
    }

    private sealed class SyncConflictDetails
    {
        public string? ObjectId { get; set; }

        public long ExpectedRevision { get; set; }

        public long CurrentRevision { get; set; }
    }
}

public sealed class CloudSyncRevisionConflictException : InvalidOperationException
{
    public CloudSyncRevisionConflictException(
        string message,
        string? objectId,
        long expectedRevision,
        long currentRevision)
        : base(message)
    {
        ObjectId = objectId ?? string.Empty;
        ExpectedRevision = expectedRevision;
        CurrentRevision = currentRevision;
    }

    public string ObjectId { get; }

    public long ExpectedRevision { get; }

    public long CurrentRevision { get; }
}

public sealed class DeviceMessageListResponse
{
    public bool Ok { get; set; }

    public string? UserId { get; set; }

    public string? DeviceId { get; set; }

    public List<DeviceMessageRecord> Items { get; set; } = [];

    public long NextCursor { get; set; }

    public long HighWater { get; set; }

    public bool CursorReset { get; set; }

    public bool HasMore { get; set; }
}

public sealed class DeviceMessageCreateResponse
{
    public bool Ok { get; set; }

    public string MessageId { get; set; } = string.Empty;
}

public sealed class DeviceMessageRecord
{
    public List<DeviceMessageReceipt> Receipts { get; set; } = [];
    public int ProtocolVersion { get; set; } = YanziDeviceMessageProtocol.Version;
    public long? Sequence { get; set; }
    public string? ClientMessageId { get; set; }
    public string? OperationId { get; set; }
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string MessageId { get; set; } = string.Empty;

    public string? SourceDeviceId { get; set; }

    public string? SourceDeviceName { get; set; }

    public string? SourceDeviceDisplayName { get; set; }

    public string? TargetDeviceId { get; set; }

    public string? TargetPlatform { get; set; }

    public string Kind { get; set; } = "text";

    public string Title { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Status { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string? DeliveredAt { get; set; }

    public string? AckedAt { get; set; }

    public string? ExpiresAt { get; set; }
}

public sealed record DeviceMessageReceipt(string DeviceId, string DisplayName, string Status, string AckedAt);
