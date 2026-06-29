using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed class CloudSyncClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _directHttpClient;
    private readonly SyncOptions _options;
    private SyncSession? _session;
    private SavedCredential? _credential;

    public CloudSyncClient(SyncOptions options)
    {
        _options = options;
        _httpClient = CreateHttpClient(options.BaseUrl, useProxy: true);
        _directHttpClient = CreateHttpClient(options.BaseUrl, useProxy: false);
        _session = SyncSessionStore.Load();
        _credential = SecureCredentialStore.Load();
    }

    public string CurrentUserLabel =>
        _session != null
            ? $"{_session.Username} ({_session.UserId})"
            : !string.IsNullOrWhiteSpace(_credential?.LoginEmail)
                ? _credential!.LoginEmail
                : "未登录";

    public bool HasCredential => !string.IsNullOrWhiteSpace(_credential?.LoginEmail) && !string.IsNullOrWhiteSpace(_credential?.Password);

    public string? GetSavedPassword() => _credential?.Password;

    public void SetCredential(string email, string password, bool remember)
    {
        CloudSyncDiagnostics.Log(
            "CloudSyncClient.Auth",
            "Credential updated",
            ("email", email),
            ("remember", remember),
            ("passwordLength", password?.Length ?? 0));
        _credential = new SavedCredential
        {
            Email = email.Trim(),
            Password = password
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
        if (HasValidSession())
        {
            CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Using existing session", ("user", CurrentUserLabel));
            return;
        }

        if (!HasCredential)
        {
            CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Authentication blocked: missing credential");
            throw new InvalidOperationException("缺少登录凭据，请先登录。");
        }

        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Authenticating with saved credential", ("email", _credential?.LoginEmail));
        _session = await LoginAsync(_credential!.LoginEmail, _credential.Password, cancellationToken);
        SyncSessionStore.Save(_session);
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Authentication completed", ("userId", _session.UserId), ("username", _session.Username));
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
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Register requested", ("email", email), ("username", username), ("passwordLength", password?.Length ?? 0), ("codeLength", code?.Length ?? 0));
        var payload = new
        {
            email = email.Trim(),
            username = username.Trim(),
            password,
            code = code.Trim()
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/register", payload, includeAuth: false, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        _session = await ReadSessionAsync(response, cancellationToken);
        SyncSessionStore.Save(_session);
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Register completed", ("userId", _session.UserId), ("username", _session.Username));
        return _session;
    }

    public async Task<SyncSession> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Login requested", ("email", email), ("passwordLength", password?.Length ?? 0));
        var payload = new
        {
            email = email.Trim(),
            password
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
        _session = await ReadSessionAsync(response, cancellationToken);
        SyncSessionStore.Save(_session);
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Login completed", ("userId", _session.UserId), ("username", _session.Username));
        return _session;
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
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Reset password requested", ("email", email), ("passwordLength", password?.Length ?? 0), ("codeLength", code?.Length ?? 0));
        var payload = new
        {
            email = email.Trim(),
            password,
            code = code.Trim()
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/reset-password", payload, includeAuth: false, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        _session = await ReadSessionAsync(response, cancellationToken);
        SyncSessionStore.Save(_session);
        CloudSyncDiagnostics.Log("CloudSyncClient.Auth", "Reset password completed", ("userId", _session.UserId), ("username", _session.Username));
        return _session;
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
        var body = JsonSerializer.Serialize(new
        {
            manifest = new
            {
                name = command.ExtensionId,
                displayName = command.Title,
                version = command.DeclaredVersion,
                category = command.Category,
                description = command.Subtitle,
                accentHex = command.AccentBrush?.ToString(),
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
            }
        });

        using var request = CreateJsonRequest(HttpMethod.Put, $"/v1/extensions/{Uri.EscapeDataString(command.ExtensionId)}", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> PublishIconAsync(CommandItem command, string version, CancellationToken cancellationToken = default)
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
        using var request = CreateRequest(
            HttpMethod.Put,
            $"/v1/extensions/{Uri.EscapeDataString(command.ExtensionId)}/icon?version={Uri.EscapeDataString(version)}&filename={Uri.EscapeDataString(Path.GetFileName(localPath))}",
            includeAuth: true);
        request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(localPath, cancellationToken));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(localPath));
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await ReadAsync<UploadIconResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("图标上传响应为空。");
        return string.IsNullOrWhiteSpace(payload.IconUrl) ? iconReference : payload.IconUrl;
    }

    public async Task UpsertUserExtensionAsync(CommandItem command, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            installedVersion = command.DeclaredVersion,
            enabled = true,
            settings = new
            {
                source = "openquickhost-desktop",
                title = command.Title
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

    public async Task<IReadOnlyList<DeviceMessageRecord>> GetPendingDeviceMessagesAsync(
        string deviceId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/me/mobile/messages?deviceId={Uri.EscapeDataString(deviceId)}&limit={limit}",
            includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await ReadAsync<DeviceMessageListResponse>(response, cancellationToken);
        return payload?.Items ?? [];
    }

    public async Task<string> SendDeviceMessageAsync(
        string sourceDeviceId,
        string targetPlatform,
        string kind,
        string title,
        string text,
        string? targetDeviceId = null,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var body = JsonSerializer.Serialize(new
        {
            sourceDeviceId,
            targetDeviceId,
            targetPlatform,
            kind,
            title,
            text,
            payload = payload ?? new { }
        });

        using var request = CreateJsonRequest(HttpMethod.Post, "/v1/me/mobile/messages", body, includeAuth: true);
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await ReadAsync<DeviceMessageCreateResponse>(response, cancellationToken);
        return result?.MessageId ?? string.Empty;
    }

    public async Task<HttpResponseMessage> GetMobileMessagesEventsStreamAsync(string deviceId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        
        var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/me/mobile/messages/events?deviceId={Uri.EscapeDataString(deviceId)}");
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
                var response = await attempts[index].client.SendAsync(CloneRequest(request), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (Exception ex) when (IsRetryableTransportException(ex) && index < attempts.Length - 1)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (index + 1)), cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (index == attempts.Length - 1)
                {
                    throw new InvalidOperationException($"Failed to establish SSE connection: {ex.Message}", lastError);
                }
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
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Put,
            $"/v1/extensions/{Uri.EscapeDataString(command.ExtensionId)}/archive?version={Uri.EscapeDataString(version)}",
            includeAuth: true);
        request.Content = new ByteArrayContent(packageBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await SendAsyncWithFallback(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> DownloadExtensionArchiveAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsyncWithFallback(
            HttpMethod.Get,
            $"/v1/extensions/{Uri.EscapeDataString(extensionId)}/archive",
            includeAuth: false,
            cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
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
        _session = null;
        SyncSessionStore.Clear();
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, string body, bool includeAuth)
    {
        var request = CreateRequest(method, path, includeAuth);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool includeAuth)
    {
        var request = new HttpRequestMessage(method, path);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeAuth && HasValidSession())
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session!.AccessToken);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object body, bool includeAuth, CancellationToken cancellationToken)
    {
        var request = CreateJsonRequest(method, path, JsonSerializer.Serialize(body), includeAuth);
        return await SendAsyncWithFallback(request, cancellationToken);
    }

    private Task<HttpResponseMessage> SendAsyncWithFallback(HttpMethod method, string path, bool includeAuth, CancellationToken cancellationToken)
    {
        var request = CreateRequest(method, path, includeAuth);
        return SendAsyncWithFallback(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsyncWithFallback(HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
                return await attempts[index].client.SendAsync(CloneRequest(request), cancellationToken);
            }
            catch (Exception ex) when (IsRetryableTransportException(ex) && index < attempts.Length - 1)
            {
                lastError = ex;
                CloudSyncDiagnostics.Log(
                    "CloudSyncClient.Http",
                    "Retryable request failure",
                    ("method", request.Method.Method),
                    ("uri", request.RequestUri?.ToString()),
                    ("attempt", index + 1),
                    ("channel", attempts[index].label),
                    ("error", ex.Message));
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (index + 1)), cancellationToken);
            }
        }

        CloudSyncDiagnostics.Log(
            "CloudSyncClient.Http",
            "Request failed after fallback",
            ("method", request.Method.Method),
            ("uri", request.RequestUri?.ToString()),
            ("error", lastError?.Message));
        throw lastError ?? new HttpRequestException("Cloud request failed before receiving a response.");
    }

    private static HttpClient CreateHttpClient(string baseUrl, bool useProxy)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = useProxy
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("YanziClient-Desktop", "0.2.3"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Yanzi-Client", "desktop");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Yanzi-Client-Version", "0.2.3");
        return client;
    }

    private static bool IsRetryableTransportException(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("0 bytes from the transport stream", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("request was canceled", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content == null)
        {
            return clone;
        }

        var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var content = new ByteArrayContent(bytes);
        foreach (var header in request.Content.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Content = content;
        return clone;
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
            Email = auth.Email
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
    }
}

public sealed class DeviceMessageListResponse
{
    public bool Ok { get; set; }

    public string? UserId { get; set; }

    public string? DeviceId { get; set; }

    public List<DeviceMessageRecord> Items { get; set; } = [];
}

public sealed class DeviceMessageCreateResponse
{
    public bool Ok { get; set; }

    public string MessageId { get; set; } = string.Empty;
}

public sealed class DeviceMessageRecord
{
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
