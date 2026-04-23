using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed class CloudSyncClient
{
    private readonly HttpClient _httpClient;
    private readonly SyncOptions _options;
    private SyncSession? _session;
    private SavedCredential? _credential;

    public CloudSyncClient(SyncOptions options)
    {
        _options = options;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _session = SyncSessionStore.Load();
        _credential = SecureCredentialStore.Load();
    }

    public string CurrentUserLabel =>
        _session != null
            ? $"{_session.Username} ({_session.UserId})"
            : !string.IsNullOrWhiteSpace(_credential?.Username)
                ? _credential!.Username
                : "未登录";

    public bool HasCredential => !string.IsNullOrWhiteSpace(_credential?.Username) && !string.IsNullOrWhiteSpace(_credential?.Password);

    public void SetCredential(string username, string password, bool remember)
    {
        _credential = new SavedCredential
        {
            Username = username.Trim(),
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
        _credential = null;
        SecureCredentialStore.Clear();
        ClearSession();
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (HasValidSession())
        {
            return;
        }

        if (!HasCredential)
        {
            throw new InvalidOperationException("缺少登录凭据，请先登录。");
        }

        _session = await LoginAsync(_credential!.Username, _credential.Password, cancellationToken);
        SyncSessionStore.Save(_session);
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
        return _session;
    }

    public async Task<SyncSession> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            username = username.Trim(),
            password
        };

        using var response = await SendJsonAsync(HttpMethod.Post, "/v1/auth/login", payload, includeAuth: false, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            ClearSession();
            throw new InvalidOperationException("用户名或密码错误。");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        _session = await ReadSessionAsync(response, cancellationToken);
        SyncSessionStore.Save(_session);
        return _session;
    }

    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("/health", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<HealthResponse>(response, cancellationToken);
    }

    public async Task<AuthMeResponse?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/auth/me", includeAuth: true);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<AuthMeResponse>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<CloudExtensionRecord>> GetExtensionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("/v1/extensions", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await ReadAsync<ExtensionListResponse>(response, cancellationToken);
        return payload?.Items ?? [];
    }

    public async Task<IReadOnlyList<UserExtensionRecord>> GetUserExtensionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, "/v1/me/extensions", includeAuth: true);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await ReadAsync<UserExtensionListResponse>(response, cancellationToken);
        return payload?.Items ?? [];
    }

    public async Task UpsertExtensionAsync(CommandItem command, CancellationToken cancellationToken = default)
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
                keywords = command.Keywords,
                queryPrefixes = command.QueryPrefixes,
                queryTargetTemplate = command.QueryTargetTemplate,
                globalShortcut = command.GlobalShortcut,
                hotkeyBehavior = command.HotkeyBehavior,
                runtime = command.Runtime,
                entry = command.EntryPoint,
                permissions = command.Permissions,
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
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
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
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveUserExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"/v1/me/extensions/{Uri.EscapeDataString(extensionId)}",
            includeAuth: true);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
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
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<byte[]> DownloadExtensionArchiveAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"/v1/extensions/{Uri.EscapeDataString(extensionId)}/archive",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
        return await _httpClient.SendAsync(request, cancellationToken);
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
            Username = auth.Username
        };
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
