using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenQuickHost.Sync;

internal interface IPersonalSyncBackend
{
    string DisplayRoot { get; }

    Task ProbeAsync(CancellationToken cancellationToken);

    Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken);

    Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken);

    Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken);
}

internal static class PersonalSyncBackendFactory
{
    public static IPersonalSyncBackend? Create(AppSettings settings, bool requireEnabled = true)
    {
        var sync = settings.PersonalSync ?? new PersonalSyncSettings();
        if (requireEnabled && !sync.Enabled)
        {
            return null;
        }

        var secrets = PersonalSyncSecretStore.Load();
        return PersonalSyncProviders.Normalize(sync.Provider) switch
        {
            PersonalSyncProviders.GitHub when IsGitHubConfigured(sync.GitHub, secrets) => new GitHubPersonalSyncBackend(sync.GitHub, secrets),
            PersonalSyncProviders.Gitee when IsGiteeConfigured(sync.Gitee, secrets) => new GiteePersonalSyncBackend(sync.Gitee, secrets),
            PersonalSyncProviders.GitLab when IsGitLabConfigured(sync.GitLab, secrets) => new GitLabPersonalSyncBackend(sync.GitLab, secrets),
            PersonalSyncProviders.Gitea when IsGiteaConfigured(sync.Gitea, secrets) => new GiteaPersonalSyncBackend(sync.Gitea, secrets),
            PersonalSyncProviders.S3 when IsS3Configured(sync.S3, secrets) => new S3PersonalSyncBackend(sync.S3, secrets),
            PersonalSyncProviders.WebDav when IsWebDavConfigured(sync.WebDav, secrets) => new WebDavPersonalSyncBackend(sync.WebDav, secrets),
            _ => null
        };
    }

    public static bool IsConfigured(AppSettings settings) => Create(settings) != null;

    public static bool IsManuallyConfigured(AppSettings settings) => Create(settings, requireEnabled: false) != null;

    public static string GetDisplayName(AppSettings settings) =>
        PersonalSyncProviders.GetDisplayName(settings.PersonalSync?.Provider);

    public static bool IsGitHubConfigured(PersonalSyncGitHubConfig? config, PersonalSyncSecretBag? secrets)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.Repo) &&
               !string.IsNullOrWhiteSpace(secrets?.GitHubToken);
    }

    public static bool IsGiteeConfigured(PersonalSyncGiteeConfig? config, PersonalSyncSecretBag? secrets)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.Repo) &&
               !string.IsNullOrWhiteSpace(secrets?.GiteeToken);
    }

    public static bool IsGitLabConfigured(PersonalSyncGitLabConfig? config, PersonalSyncSecretBag? secrets)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.BaseUrl) &&
               !string.IsNullOrWhiteSpace(config.ProjectPath) &&
               !string.IsNullOrWhiteSpace(secrets?.GitLabToken);
    }

    public static bool IsGiteaConfigured(PersonalSyncGiteaConfig? config, PersonalSyncSecretBag? secrets)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.BaseUrl) &&
               !string.IsNullOrWhiteSpace(config.Repo) &&
               !string.IsNullOrWhiteSpace(secrets?.GiteaToken);
    }

    public static bool IsS3Configured(PersonalSyncS3Config? config, PersonalSyncSecretBag? secrets)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.AccessKeyId) &&
               !string.IsNullOrWhiteSpace(config.Region) &&
               !string.IsNullOrWhiteSpace(config.Bucket) &&
               !string.IsNullOrWhiteSpace(secrets?.S3SecretAccessKey);
    }

    public static bool IsWebDavConfigured(PersonalSyncWebDavConfig? config, PersonalSyncSecretBag? secrets)
    {
        return config != null &&
               !string.IsNullOrWhiteSpace(config.Url) &&
               !string.IsNullOrWhiteSpace(config.Username) &&
               !string.IsNullOrWhiteSpace(secrets?.WebDavPassword);
    }
}

internal abstract class PersonalSyncBackendBase : IPersonalSyncBackend
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public abstract string DisplayRoot { get; }

    public abstract Task ProbeAsync(CancellationToken cancellationToken);

    public abstract Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken);

    public abstract Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken);

    public abstract Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken);

    protected static string NormalizeRelativePath(string relativePath)
    {
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("同步路径不能为空。");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("同步路径不能包含 . 或 ..。");
        }

        return string.Join("/", segments);
    }

    protected static string CombineWithPrefix(string prefix, string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        var normalizedPrefix = (prefix ?? string.Empty).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return normalizedPath;
        }

        normalizedPrefix = normalizedPrefix.Trim('/');
        return string.IsNullOrWhiteSpace(normalizedPrefix)
            ? normalizedPath
            : $"{normalizedPrefix}/{normalizedPath}";
    }

    protected static byte[] DecodeBase64Content(string? value)
    {
        var normalized = (value ?? string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        return string.IsNullOrWhiteSpace(normalized)
            ? []
            : Convert.FromBase64String(normalized);
    }

    private static string GetExtensionTitle(string extensionId)
    {
        try
        {
            var manifestJson = LocalExtensionCatalog.LoadManifestJson(extensionId);
            using var doc = System.Text.Json.JsonDocument.Parse(manifestJson);
            if (doc.RootElement.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }
        catch
        {
        }
        return extensionId;
    }

    protected virtual string GenerateWebUrl(string path)
    {
        var root = DisplayRoot ?? string.Empty;
        var m = System.Text.RegularExpressions.Regex.Match(root, @"^(github|gitee|gitlab|gitea)://([^/]+)/([^@]+)@(.+)$");
        if (m.Success)
        {
            var provider = m.Groups[1].Value;
            var owner = m.Groups[2].Value;
            var repo = m.Groups[3].Value;
            var branch = m.Groups[4].Value;
            var normalizedPath = (path ?? string.Empty).Replace('\\', '/').Trim('/');
            
            if (provider == "github") return $"https://github.com/{owner}/{repo}/blob/{branch}/{normalizedPath}";
            if (provider == "gitee") return $"https://gitee.com/{owner}/{repo}/blob/{branch}/{normalizedPath}";
        }
        return string.Empty;
    }

    protected string GetBusinessCommitMessage(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
        if (normalized.EndsWith("state/launcher-config.json", StringComparison.OrdinalIgnoreCase))
        {
            return "同步配置：更新快捷菜单与系统主设置";
        }
        if (normalized.EndsWith("state/yanm-state.json", StringComparison.OrdinalIgnoreCase))
        {
            return "同步状态：更新燕幕组件状态";
        }
        if (normalized.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            return "同步目录：更新个人扩展索引";
        }
        if (normalized.Contains("packages/", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "同步扩展：上传个人扩展包";
        }
        var appdataIndex = normalized.IndexOf("appdata/", StringComparison.OrdinalIgnoreCase);
        if (appdataIndex >= 0)
        {
            var subPath = normalized.Substring(appdataIndex + "appdata/".Length);
            var parts = subPath.Split('/');
            if (parts.Length >= 2)
            {
                var extensionId = parts[0];
                var fileName = parts[^1];
                var extName = GetExtensionTitle(extensionId);
                var webUrl = GenerateWebUrl(path);
                var linkText = string.IsNullOrWhiteSpace(webUrl) ? fileName : $"{webUrl}";
                return $"上传数据：备份 【{extName}】 的 {linkText}";
            }
            return "上传数据：备份扩展专属应用数据";
        }
        return $"上传数据：更新 {path}";
    }

    protected string GetBusinessDeleteMessage(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
        if (normalized.EndsWith("state/launcher-config.json", StringComparison.OrdinalIgnoreCase))
        {
            return "同步配置：清除快捷菜单与系统主设置";
        }
        if (normalized.EndsWith("state/yanm-state.json", StringComparison.OrdinalIgnoreCase))
        {
            return "同步状态：清除燕幕组件状态";
        }
        if (normalized.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            return "同步目录：清除个人扩展索引";
        }
        if (normalized.Contains("packages/", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "同步扩展：删除个人扩展包";
        }
        var appdataIndex = normalized.IndexOf("appdata/", StringComparison.OrdinalIgnoreCase);
        if (appdataIndex >= 0)
        {
            var subPath = normalized.Substring(appdataIndex + "appdata/".Length);
            var parts = subPath.Split('/');
            if (parts.Length >= 2)
            {
                var extensionId = parts[0];
                var fileName = parts[^1];
                var extName = GetExtensionTitle(extensionId);
                var webUrl = GenerateWebUrl(path);
                var linkText = string.IsNullOrWhiteSpace(webUrl) ? fileName : $"{webUrl}";
                return $"上传数据：清除 【{extName}】 的 {linkText}";
            }
            return "上传数据：清除扩展专属应用数据";
        }
        return $"上传数据：删除 {path}";
    }
}

internal sealed class GitHubPersonalSyncBackend : PersonalSyncBackendBase
{
    private readonly PersonalSyncGitHubConfig _config;
    private readonly PersonalSyncSecretBag _secrets;
    private readonly HttpClient _httpClient = new();
    private string? _resolvedOwner;

    public GitHubPersonalSyncBackend(PersonalSyncGitHubConfig config, PersonalSyncSecretBag secrets)
    {
        _config = config;
        _secrets = secrets;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secrets.GitHubToken.Trim());
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Yanzi", "0.1"));
    }

    public override string DisplayRoot => $"github://{GetConfiguredOwnerDisplay()}/{_config.Repo}@{ResolveBranch()}";

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && await CanCreateRepositoryForOwnerAsync(owner, cancellationToken))
        {
            await CreateRepositoryAsync(cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}?ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitHubContentPayload>(JsonOptions, cancellationToken);
        if (!string.IsNullOrWhiteSpace(payload?.Content))
        {
            return DecodeBase64Content(payload.Content);
        }

        if (!string.IsNullOrWhiteSpace(payload?.GitUrl))
        {
            return await ReadBlobBytesAsync(payload.GitUrl, cancellationToken);
        }

        return null;
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        var sha = await TryGetShaAsync(path, cancellationToken);
        using var response = await PutFileAsync(owner, path, content, sha, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var latestSha = await TryGetShaAsync(path, cancellationToken);
            if (!string.IsNullOrWhiteSpace(latestSha) &&
                !string.Equals(latestSha, sha, StringComparison.Ordinal))
            {
                using var retryResponse = await PutFileAsync(owner, path, content, latestSha, cancellationToken);
                if (retryResponse.IsSuccessStatusCode)
                {
                    return;
                }

                throw await PersonalSyncFailure.CreateFailureAsync("GitHub", retryResponse, cancellationToken);
            }
        }

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity &&
            await IsGitHubFileTooLargeResponseAsync(response, cancellationToken))
        {
            HostAssets.AppendLog($"GitHub personal sync switching to Git database API: path={path}, bytes={content.Length}");
            await WriteBytesWithGitDatabaseAsync(owner, path, content, cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        var sha = await TryGetShaAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}")
        {
            Content = JsonContent.Create(new GitHubDeletePayload
            {
                Message = GetBusinessDeleteMessage(path),
                Sha = sha,
                Branch = ResolveBranch()
            }, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }
    }

    private async Task<string?> TryGetShaAsync(string path, CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}?ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitHubContentPayload>(JsonOptions, cancellationToken);
        return payload?.Sha;
    }

    private Task<HttpResponseMessage> PutFileAsync(string owner, string path, byte[] content, string? sha, CancellationToken cancellationToken)
    {
        var body = new GitHubWritePayload
        {
            Message = GetBusinessCommitMessage(path),
            Content = Convert.ToBase64String(content),
            Branch = ResolveBranch(),
            Sha = sha
        };
        return _httpClient.PutAsJsonAsync(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}",
            body,
            JsonOptions,
            cancellationToken);
    }

    private async Task<byte[]?> ReadBlobBytesAsync(string gitUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(gitUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        var blob = await response.Content.ReadFromJsonAsync<GitHubBlobPayload>(JsonOptions, cancellationToken);
        return DecodeBase64Content(blob?.Content);
    }

    private async Task WriteBytesWithGitDatabaseAsync(string owner, string path, byte[] content, CancellationToken cancellationToken)
    {
        var branch = ResolveBranch();
        var currentRef = await GetGitRefAsync(owner, branch, cancellationToken);
        var currentCommit = await GetGitCommitAsync(owner, currentRef.Object.Sha, cancellationToken);
        var blob = await CreateGitBlobAsync(owner, content, cancellationToken);
        var tree = await CreateGitTreeAsync(owner, currentCommit.Tree.Sha, path, blob.Sha, cancellationToken);
        var commit = await CreateGitCommitAsync(owner, GetBusinessCommitMessage(path), tree.Sha, currentRef.Object.Sha, cancellationToken);
        await UpdateGitRefAsync(owner, branch, commit.Sha, cancellationToken);
    }

    private async Task<GitHubRefPayload> GetGitRefAsync(string owner, string branch, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/git/ref/heads/{Uri.EscapeDataString(branch)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<GitHubRefPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub 未返回分支引用，无法写入大文件。");
    }

    private async Task<GitHubCommitPayload> GetGitCommitAsync(string owner, string commitSha, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/git/commits/{Uri.EscapeDataString(commitSha)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<GitHubCommitPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub 未返回提交信息，无法写入大文件。");
    }

    private async Task<GitHubBlobPayload> CreateGitBlobAsync(string owner, byte[] content, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/git/blobs",
            new GitHubCreateBlobPayload
            {
                Content = Convert.ToBase64String(content),
                Encoding = "base64"
            },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<GitHubBlobPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub 未返回 Blob 信息，无法写入大文件。");
    }

    private async Task<GitHubTreePayload> CreateGitTreeAsync(string owner, string baseTreeSha, string path, string blobSha, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/git/trees",
            new GitHubCreateTreePayload
            {
                BaseTree = baseTreeSha,
                Tree =
                [
                    new GitHubTreeItemPayload
                    {
                        Path = path,
                        Mode = "100644",
                        Type = "blob",
                        Sha = blobSha
                    }
                ]
            },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<GitHubTreePayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub 未返回 Tree 信息，无法写入大文件。");
    }

    private async Task<GitHubCommitCreatedPayload> CreateGitCommitAsync(string owner, string message, string treeSha, string parentSha, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/git/commits",
            new GitHubCreateCommitPayload
            {
                Message = message,
                Tree = treeSha,
                Parents = [parentSha]
            },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<GitHubCommitCreatedPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub 未返回新提交信息，无法写入大文件。");
    }

    private async Task UpdateGitRefAsync(string owner, string branch, string commitSha, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/git/refs/heads/{Uri.EscapeDataString(branch)}")
        {
            Content = JsonContent.Create(new GitHubUpdateRefPayload
            {
                Sha = commitSha,
                Force = false
            }, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }
    }

    private static async Task<bool> IsGitHubFileTooLargeResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return text.Contains("file is too large", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("too large to be processed", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildPath(string relativePath) => CombineWithPrefix(_config.PathPrefix, relativePath);

    private string ResolveBranch() => string.IsNullOrWhiteSpace(_config.Branch) ? "main" : _config.Branch.Trim();

    private string GetConfiguredOwnerDisplay() =>
        string.IsNullOrWhiteSpace(_config.Username)
            ? "<token-owner>"
            : _config.Username.Trim();

    private async Task<string> ResolveOwnerAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_resolvedOwner))
        {
            return _resolvedOwner;
        }

        if (!string.IsNullOrWhiteSpace(_config.Username))
        {
            _resolvedOwner = _config.Username.Trim();
            return _resolvedOwner;
        }

        using var response = await _httpClient.GetAsync("https://api.github.com/user", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitHubViewerPayload>(JsonOptions, cancellationToken);
        var owner = payload?.Login?.Trim();
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new InvalidOperationException("GitHub Token 未返回可用账号名，无法定位同步仓库。");
        }

        _resolvedOwner = owner;
        return _resolvedOwner;
    }

    private async Task<bool> CanCreateRepositoryForOwnerAsync(string owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.Username))
        {
            return true;
        }

        var authenticatedLogin = await ResolveAuthenticatedLoginAsync(cancellationToken);
        return string.Equals(owner, authenticatedLogin, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveAuthenticatedLoginAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("https://api.github.com/user", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitHubViewerPayload>(JsonOptions, cancellationToken);
        var login = payload?.Login?.Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new InvalidOperationException("GitHub Token 未返回可用账号名，无法创建同步仓库。");
        }

        return login;
    }

    private async Task CreateRepositoryAsync(CancellationToken cancellationToken)
    {
        var repoName = _config.Repo.Trim();
        using var response = await _httpClient.PostAsJsonAsync(
            "https://api.github.com/user/repos",
            new GitHubCreateRepoPayload
            {
                Name = repoName,
                Description = "This is a Yanzi personal sync repository.",
                Private = true,
                AutoInit = true
            },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitHub", response, cancellationToken);
        }
    }

    private static string EncodePath(string path) => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
}

internal sealed class GiteePersonalSyncBackend : PersonalSyncBackendBase
{
    private readonly PersonalSyncGiteeConfig _config;
    private readonly PersonalSyncSecretBag _secrets;
    private readonly HttpClient _httpClient = new();
    private string? _resolvedOwner;

    public GiteePersonalSyncBackend(PersonalSyncGiteeConfig config, PersonalSyncSecretBag secrets)
    {
        _config = config;
        _secrets = secrets;
    }

    public override string DisplayRoot => $"gitee://{GetConfiguredOwnerDisplay()}/{_config.Repo}@{ResolveBranch()}";

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        CloudSyncDiagnostics.Log("GiteeBackend", "Probe started", ("owner", owner), ("repo", _config.Repo), ("branch", ResolveBranch()));
        using var response = await _httpClient.GetAsync($"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}?access_token={Uri.EscapeDataString(_secrets.GiteeToken.Trim())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && await CanCreateRepositoryForOwnerAsync(owner, cancellationToken))
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Repository missing, creating automatically", ("owner", owner), ("repo", _config.Repo));
            await CreateRepositoryAsync(cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Probe failed", ("owner", owner), ("repo", _config.Repo), ("statusCode", (int)response.StatusCode));
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        CloudSyncDiagnostics.Log("GiteeBackend", "Probe completed", ("owner", owner), ("repo", _config.Repo));
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        CloudSyncDiagnostics.Log("GiteeBackend", "Read requested", ("owner", owner), ("repo", _config.Repo), ("path", path));
        using var response = await _httpClient.GetAsync($"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}?access_token={Uri.EscapeDataString(_secrets.GiteeToken.Trim())}&ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Read returned not found", ("path", path));
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Read failed", ("path", path), ("statusCode", (int)response.StatusCode));
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Trim() == "[]")
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<GiteeContentPayload>(rawJson, JsonOptions);
        var bytes = DecodeBase64Content(payload?.Content);
        CloudSyncDiagnostics.Log("GiteeBackend", "Read completed", ("path", path), ("bytes", bytes.Length), ("sha", payload?.Sha));
        return bytes;
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        var sha = await TryGetShaAsync(path, cancellationToken);
        var method = string.IsNullOrWhiteSpace(sha) ? HttpMethod.Post : HttpMethod.Put;
        CloudSyncDiagnostics.Log(
            "GiteeBackend",
            "Write requested",
            ("owner", owner),
            ("repo", _config.Repo),
            ("path", path),
            ("bytes", content.Length),
            ("method", method.Method),
            ("hasExistingSha", !string.IsNullOrWhiteSpace(sha)));
        using var request = new HttpRequestMessage(method, $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}")
        {
            Content = JsonContent.Create(new GiteeWritePayload
            {
                AccessToken = _secrets.GiteeToken.Trim(),
                Content = Convert.ToBase64String(content),
                Message = GetBusinessCommitMessage(path),
                Branch = ResolveBranch(),
                Sha = sha
            }, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Write failed", ("path", path), ("statusCode", (int)response.StatusCode));
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        CloudSyncDiagnostics.Log("GiteeBackend", "Write completed", ("path", path), ("bytes", content.Length));
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        var sha = await TryGetShaAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(sha))
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Delete skipped: sha missing", ("path", path));
            return;
        }

        CloudSyncDiagnostics.Log("GiteeBackend", "Delete requested", ("owner", owner), ("repo", _config.Repo), ("path", path), ("sha", sha));

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}")
        {
            Content = JsonContent.Create(new GiteeDeletePayload
            {
                AccessToken = _secrets.GiteeToken.Trim(),
                Message = GetBusinessDeleteMessage(path),
                Sha = sha,
                Branch = ResolveBranch()
            }, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Delete failed", ("path", path), ("statusCode", (int)response.StatusCode));
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        CloudSyncDiagnostics.Log("GiteeBackend", "Delete completed", ("path", path), ("statusCode", (int)response.StatusCode));
    }

    private async Task<string?> TryGetShaAsync(string path, CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo)}/contents/{EncodePath(path)}?access_token={Uri.EscapeDataString(_secrets.GiteeToken.Trim())}&ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Trim() == "[]")
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<GiteeContentPayload>(rawJson, JsonOptions);
        return payload?.Sha;
    }

    private string BuildPath(string relativePath) => CombineWithPrefix(_config.PathPrefix, relativePath);

    private string ResolveBranch() => string.IsNullOrWhiteSpace(_config.Branch) ? "master" : _config.Branch.Trim();

    private string GetConfiguredOwnerDisplay() =>
        string.IsNullOrWhiteSpace(_config.Username)
            ? "<token-owner>"
            : _config.Username.Trim();

    private async Task<string> ResolveOwnerAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_resolvedOwner))
        {
            return _resolvedOwner;
        }

        if (!string.IsNullOrWhiteSpace(_config.Username))
        {
            _resolvedOwner = _config.Username.Trim();
            CloudSyncDiagnostics.Log("GiteeBackend", "Owner resolved from config", ("owner", _resolvedOwner));
            return _resolvedOwner;
        }

        CloudSyncDiagnostics.Log("GiteeBackend", "Resolving owner from token");
        using var response = await _httpClient.GetAsync($"https://gitee.com/api/v5/user?access_token={Uri.EscapeDataString(_secrets.GiteeToken.Trim())}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Owner resolve failed", ("statusCode", (int)response.StatusCode));
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitProviderViewerPayload>(JsonOptions, cancellationToken);
        var owner = payload?.Login?.Trim();
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new InvalidOperationException("Gitee Token 未返回可用账号名，无法定位同步仓库。");
        }

        _resolvedOwner = owner;
        CloudSyncDiagnostics.Log("GiteeBackend", "Owner resolved from token", ("owner", _resolvedOwner));
        return _resolvedOwner;
    }

    private async Task<bool> CanCreateRepositoryForOwnerAsync(string owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.Username))
        {
            return true;
        }

        var authenticatedLogin = await ResolveOwnerAsync(cancellationToken);
        return string.Equals(owner, authenticatedLogin, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CreateRepositoryAsync(CancellationToken cancellationToken)
    {
        var repoName = _config.Repo.Trim();
        CloudSyncDiagnostics.Log("GiteeBackend", "Create repository requested", ("repo", repoName));
        using var response = await _httpClient.PostAsJsonAsync(
            "https://gitee.com/api/v5/user/repos",
            new GiteeCreateRepoPayload
            {
                AccessToken = _secrets.GiteeToken.Trim(),
                Name = repoName,
                Description = "This is a Yanzi personal sync repository.",
                Private = true,
                AutoInit = true
            },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            CloudSyncDiagnostics.Log("GiteeBackend", "Create repository failed", ("repo", repoName), ("statusCode", (int)response.StatusCode));
            throw await PersonalSyncFailure.CreateFailureAsync("Gitee", response, cancellationToken);
        }

        CloudSyncDiagnostics.Log("GiteeBackend", "Create repository completed", ("repo", repoName));
    }

    private static string EncodePath(string path) => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
}

internal sealed class GitLabPersonalSyncBackend : PersonalSyncBackendBase
{
    private readonly PersonalSyncGitLabConfig _config;
    private readonly PersonalSyncSecretBag _secrets;
    private readonly HttpClient _httpClient = new();

    public GitLabPersonalSyncBackend(PersonalSyncGitLabConfig config, PersonalSyncSecretBag secrets)
    {
        _config = config;
        _secrets = secrets;
        _httpClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _secrets.GitLabToken.Trim());
    }

    public override string DisplayRoot => $"gitlab://{_config.ProjectPath}@{ResolveBranch()}";

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/projects/{Uri.EscapeDataString(_config.ProjectPath.Trim())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && await CanCreateProjectAsync(cancellationToken))
        {
            await CreateProjectAsync(cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitLab", response, cancellationToken);
        }
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/projects/{Uri.EscapeDataString(_config.ProjectPath.Trim())}/repository/files/{Uri.EscapeDataString(path)}?ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitLab", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitLabFilePayload>(JsonOptions, cancellationToken);
        return DecodeBase64Content(payload?.Content);
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var exists = await TryReadBytesAsync(relativePath, cancellationToken) != null;
        var body = new GitLabWritePayload
        {
            Branch = ResolveBranch(),
            Content = Convert.ToBase64String(content),
            CommitMessage = GetBusinessCommitMessage(path),
            Encoding = "base64"
        };
        var method = exists ? HttpMethod.Put : HttpMethod.Post;
        using var request = new HttpRequestMessage(method, $"{ResolveApiBase()}/projects/{Uri.EscapeDataString(_config.ProjectPath.Trim())}/repository/files/{Uri.EscapeDataString(path)}")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitLab", response, cancellationToken);
        }
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ResolveApiBase()}/projects/{Uri.EscapeDataString(_config.ProjectPath.Trim())}/repository/files/{Uri.EscapeDataString(path)}")
        {
            Content = JsonContent.Create(new GitLabDeletePayload
            {
                Branch = ResolveBranch(),
                CommitMessage = GetBusinessDeleteMessage(path)
            }, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitLab", response, cancellationToken);
        }
    }

    private string ResolveApiBase()
    {
        var baseUrl = (_config.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"https://{baseUrl}";
        }

        return $"{baseUrl}/api/v4";
    }

    private string BuildPath(string relativePath) => CombineWithPrefix(_config.PathPrefix, relativePath);

    private string ResolveBranch() => string.IsNullOrWhiteSpace(_config.Branch) ? "main" : _config.Branch.Trim();

    private Task<bool> CanCreateProjectAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        var projectPath = _config.ProjectPath.Trim();
        string name = projectPath;
        int? namespaceId = null;

        if (projectPath.Contains('/'))
        {
            var parts = projectPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
            {
                var namespaceName = string.Join("/", parts.Take(parts.Length - 1));
                name = parts.Last();
                namespaceId = await FindNamespaceIdAsync(namespaceName, cancellationToken);
            }
        }

        using var response = await _httpClient.PostAsJsonAsync(
            $"{ResolveApiBase()}/projects",
            new GitLabCreateProjectPayload
            {
                Name = name,
                Path = name,
                Visibility = "private",
                InitializeWithReadme = true,
                NamespaceId = namespaceId
            },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("GitLab", response, cancellationToken);
        }
    }

    private async Task<int?> FindNamespaceIdAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"{ResolveApiBase()}/namespaces?search={Uri.EscapeDataString(path)}",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var namespaces = await response.Content.ReadFromJsonAsync<List<GitLabNamespaceDto>>(JsonOptions, cancellationToken);
            var matched = namespaces?.FirstOrDefault(n => string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase));
            return matched?.Id;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class GiteaPersonalSyncBackend : PersonalSyncBackendBase
{
    private readonly PersonalSyncGiteaConfig _config;
    private readonly PersonalSyncSecretBag _secrets;
    private readonly HttpClient _httpClient = new();
    private string? _resolvedOwner;

    public GiteaPersonalSyncBackend(PersonalSyncGiteaConfig config, PersonalSyncSecretBag secrets)
    {
        _config = config;
        _secrets = secrets;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", _secrets.GiteaToken.Trim());
    }

    public override string DisplayRoot => $"gitea://{GetConfiguredOwnerDisplay()}/{_config.Repo}@{ResolveBranch()}";

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo.Trim())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && await CanCreateRepositoryForOwnerAsync(owner, cancellationToken))
        {
            await CreateRepositoryAsync(cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo.Trim())}/contents/{EncodePath(path)}?ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GiteaContentPayload>(JsonOptions, cancellationToken);
        return DecodeBase64Content(payload?.Content);
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        var sha = await TryGetShaAsync(relativePath, cancellationToken);
        var body = new GiteaWritePayload
        {
            Branch = ResolveBranch(),
            Content = Convert.ToBase64String(content),
            Message = GetBusinessCommitMessage(path),
            Sha = sha
        };
        var method = string.IsNullOrWhiteSpace(sha) ? HttpMethod.Post : HttpMethod.Put;
        using var request = new HttpRequestMessage(method, $"{ResolveApiBase()}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo.Trim())}/contents/{EncodePath(path)}")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        var sha = await TryGetShaAsync(relativePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ResolveApiBase()}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo.Trim())}/contents/{EncodePath(path)}")
        {
            Content = JsonContent.Create(new GiteaDeletePayload
            {
                Branch = ResolveBranch(),
                Message = GetBusinessDeleteMessage(path),
                Sha = sha
            }, options: JsonOptions)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }
    }

    private async Task<string?> TryGetShaAsync(string relativePath, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath);
        var owner = await ResolveOwnerAsync(cancellationToken);
        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(_config.Repo.Trim())}/contents/{EncodePath(path)}?ref={Uri.EscapeDataString(ResolveBranch())}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GiteaContentPayload>(JsonOptions, cancellationToken);
        return payload?.Sha;
    }

    private string ResolveApiBase()
    {
        var baseUrl = (_config.BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"https://{baseUrl}";
        }

        return $"{baseUrl}/api/v1";
    }

    private string BuildPath(string relativePath) => CombineWithPrefix(_config.PathPrefix, relativePath);

    private string ResolveBranch() => string.IsNullOrWhiteSpace(_config.Branch) ? "main" : _config.Branch.Trim();

    private string GetConfiguredOwnerDisplay() =>
        string.IsNullOrWhiteSpace(_config.Username)
            ? "<token-owner>"
            : _config.Username.Trim();

    private async Task<string> ResolveOwnerAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_resolvedOwner))
        {
            return _resolvedOwner;
        }

        if (!string.IsNullOrWhiteSpace(_config.Username))
        {
            _resolvedOwner = _config.Username.Trim();
            return _resolvedOwner;
        }

        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/user", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitProviderViewerPayload>(JsonOptions, cancellationToken);
        var owner = payload?.Login?.Trim();
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new InvalidOperationException("Gitea Token 未返回可用账号名，无法定位同步仓库。");
        }

        _resolvedOwner = owner;
        return _resolvedOwner;
    }

    private async Task<bool> CanCreateRepositoryForOwnerAsync(string owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.Username))
        {
            return true;
        }

        var authenticatedLogin = await ResolveAuthenticatedLoginAsync(cancellationToken);
        return string.Equals(owner, authenticatedLogin, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveAuthenticatedLoginAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{ResolveApiBase()}/user", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }

        var payload = await response.Content.ReadFromJsonAsync<GitProviderViewerPayload>(JsonOptions, cancellationToken);
        var login = payload?.Login?.Trim();
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new InvalidOperationException("Gitea Token 未返回可用账号名，无法创建同步仓库。");
        }

        return login;
    }

    private async Task CreateRepositoryAsync(CancellationToken cancellationToken)
    {
        var repoName = _config.Repo.Trim();
        using var response = await _httpClient.PostAsJsonAsync(
            $"{ResolveApiBase()}/user/repos",
            new GiteaCreateRepoPayload
            {
                Name = repoName,
                Description = "This is a Yanzi personal sync repository.",
                Private = true,
                AutoInit = true
            },
            JsonOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("Gitea", response, cancellationToken);
        }
    }

    private static string EncodePath(string path) => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
}

internal sealed class S3PersonalSyncBackend : PersonalSyncBackendBase
{
    private readonly PersonalSyncS3Config _config;
    private readonly PersonalSyncSecretBag _secrets;

    public S3PersonalSyncBackend(PersonalSyncS3Config config, PersonalSyncSecretBag secrets)
    {
        _config = config;
        _secrets = secrets;
    }

    public override string DisplayRoot => $"s3://{_config.Bucket}/{NormalizePrefix(_config.PathPrefix)}";

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, string.Empty, null, "application/octet-stream", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("S3", response, cancellationToken);
        }
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, relativePath, null, "application/octet-stream", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("S3", response, cancellationToken);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Put, relativePath, content, contentType, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("S3", response, cancellationToken);
        }
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, relativePath, null, "application/octet-stream", cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("S3", response, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativePath, byte[]? content, string contentType, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(relativePath) ? NormalizePrefix(_config.PathPrefix) : CombineWithPrefix(_config.PathPrefix, relativePath);
        var endpoint = ResolveEndpoint();
        var path = string.IsNullOrWhiteSpace(key)
            ? $"/{Uri.EscapeDataString(_config.Bucket.Trim())}"
            : $"/{Uri.EscapeDataString(_config.Bucket.Trim())}/{string.Join("/", key.Split('/').Select(Uri.EscapeDataString))}";
        var requestUri = new Uri($"{endpoint}{path}");

        var payload = content ?? [];
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var timestamp = DateTime.UtcNow;
        var amzDate = timestamp.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = timestamp.ToString("yyyyMMdd");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = requestUri.Host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };

        if (content != null && content.Length > 0 && !string.IsNullOrWhiteSpace(contentType))
        {
            headers["content-type"] = contentType;
        }

        var signedHeaders = string.Join(";", headers.Keys.Select(static key => key.ToLowerInvariant()).OrderBy(static key => key, StringComparer.Ordinal));
        var canonicalHeaders = string.Concat(headers.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => $"{entry.Key.ToLowerInvariant()}:{entry.Value.Trim()}\n"));
        var canonicalRequest = string.Join(
            "\n",
            method.Method,
            requestUri.AbsolutePath,
            string.Empty,
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var credentialScope = $"{dateStamp}/{_config.Region.Trim()}/s3/aws4_request";
        var stringToSign = string.Join(
            "\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant());
        var signingKey = BuildAwsSigningKey(_secrets.S3SecretAccessKey.Trim(), dateStamp, _config.Region.Trim(), "s3");
        var signature = Convert.ToHexString(HmacSha256(signingKey, stringToSign)).ToLowerInvariant();
        var authorization = $"AWS4-HMAC-SHA256 Credential={_config.AccessKeyId.Trim()}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (content != null)
        {
            request.Content = new ByteArrayContent(content);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }
        }

        var client = new HttpClient();
        return await client.SendAsync(request, cancellationToken);
    }

    private string ResolveEndpoint()
    {
        var endpoint = string.IsNullOrWhiteSpace(_config.Endpoint)
            ? $"https://s3.{_config.Region.Trim()}.amazonaws.com"
            : _config.Endpoint.Trim();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"https://{endpoint}";
        }

        return endpoint.TrimEnd('/');
    }

    private static string NormalizePrefix(string? prefix) => (prefix ?? string.Empty).Replace('\\', '/').Trim('/');

    private static byte[] BuildAwsSigningKey(string secret, string dateStamp, string regionName, string serviceName)
    {
        var kDate = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secret}"), dateStamp);
        var kRegion = HmacSha256(kDate, regionName);
        var kService = HmacSha256(kRegion, serviceName);
        return HmacSha256(kService, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}

internal sealed class WebDavPersonalSyncBackend : PersonalSyncBackendBase
{
    private readonly PersonalSyncWebDavConfig _config;
    private readonly PersonalSyncSecretBag _secrets;
    private readonly HttpClient _httpClient = new();

    public WebDavPersonalSyncBackend(PersonalSyncWebDavConfig config, PersonalSyncSecretBag secrets)
    {
        _config = config;
        _secrets = secrets;
        var raw = $"{_config.Username.Trim()}:{_secrets.WebDavPassword}";
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
    }

    public override string DisplayRoot => $"{ResolveBaseUrl()}/{NormalizePrefix(_config.PathPrefix)}";

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), ResolveCollectionUri(string.Empty));
        request.Headers.TryAddWithoutValidation("Depth", "0");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("WebDAV", response, cancellationToken);
        }
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(ResolveFileUri(relativePath), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("WebDAV", response, cancellationToken);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        await EnsureParentCollectionsAsync(relativePath, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, ResolveFileUri(relativePath))
        {
            Content = new ByteArrayContent(content)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("WebDAV", response, cancellationToken);
        }
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync(ResolveFileUri(relativePath), cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw await PersonalSyncFailure.CreateFailureAsync("WebDAV", response, cancellationToken);
        }
    }

    private async Task EnsureParentCollectionsAsync(string relativePath, CancellationToken cancellationToken)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length <= 1)
        {
            return;
        }

        var current = string.Empty;
        foreach (var segment in segments.Take(segments.Length - 1))
        {
            current = string.IsNullOrWhiteSpace(current) ? segment : $"{current}/{segment}";
            using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), ResolveCollectionUri(current));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode ||
                response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                response.StatusCode == HttpStatusCode.Conflict)
            {
                continue;
            }

            throw await PersonalSyncFailure.CreateFailureAsync("WebDAV", response, cancellationToken);
        }
    }

    private Uri ResolveCollectionUri(string relativePath)
    {
        var path = string.IsNullOrWhiteSpace(relativePath) ? NormalizePrefix(_config.PathPrefix) : BuildPath(relativePath);
        var baseUrl = ResolveBaseUrl();
        return string.IsNullOrWhiteSpace(path)
            ? new Uri(baseUrl, UriKind.Absolute)
            : new Uri($"{baseUrl}/{path}", UriKind.Absolute);
    }

    private Uri ResolveFileUri(string relativePath)
    {
        return new Uri($"{ResolveBaseUrl()}/{BuildPath(relativePath)}", UriKind.Absolute);
    }

    private string ResolveBaseUrl() => (_config.Url ?? string.Empty).Trim().TrimEnd('/');

    private string BuildPath(string relativePath) => CombineWithPrefix(_config.PathPrefix, relativePath);

    private static string NormalizePrefix(string? prefix) => (prefix ?? string.Empty).Replace('\\', '/').Trim('/');
}

internal sealed class GitHubContentPayload
{
    public string? Content { get; set; }

    public string? Sha { get; set; }

    [JsonPropertyName("git_url")]
    public string? GitUrl { get; set; }
}

internal sealed class GitHubViewerPayload
{
    public string? Login { get; set; }
}

internal sealed class GitHubBlobPayload
{
    public string Sha { get; set; } = string.Empty;

    public string? Content { get; set; }

    public string? Encoding { get; set; }
}

internal sealed class GitHubRefPayload
{
    public GitHubRefObjectPayload Object { get; set; } = new();
}

internal sealed class GitHubRefObjectPayload
{
    public string Sha { get; set; } = string.Empty;
}

internal sealed class GitHubCommitPayload
{
    public GitHubTreePayload Tree { get; set; } = new();
}

internal sealed class GitHubTreePayload
{
    public string Sha { get; set; } = string.Empty;
}

internal sealed class GitProviderViewerPayload
{
    public string? Login { get; set; }
}

internal sealed class GiteeContentPayload
{
    public string? Content { get; set; }

    public string? Sha { get; set; }
}

internal sealed class GiteaContentPayload
{
    public string? Content { get; set; }

    public string? Sha { get; set; }
}

internal sealed class GitLabFilePayload
{
    public string? Content { get; set; }
}

internal sealed class GitHubWritePayload
{
    public string Message { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public string? Sha { get; set; }
}

internal sealed class GitHubCreateRepoPayload
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Private { get; set; } = true;

    [JsonPropertyName("auto_init")]
    public bool AutoInit { get; set; } = true;
}

internal sealed class GitHubCreateBlobPayload
{
    public string Content { get; set; } = string.Empty;

    public string Encoding { get; set; } = "base64";
}

internal sealed class GitHubCreateTreePayload
{
    [JsonPropertyName("base_tree")]
    public string BaseTree { get; set; } = string.Empty;

    public List<GitHubTreeItemPayload> Tree { get; set; } = [];
}

internal sealed class GitHubTreeItemPayload
{
    public string Path { get; set; } = string.Empty;

    public string Mode { get; set; } = "100644";

    public string Type { get; set; } = "blob";

    public string Sha { get; set; } = string.Empty;
}

internal sealed class GitHubCreateCommitPayload
{
    public string Message { get; set; } = string.Empty;

    public string Tree { get; set; } = string.Empty;

    public List<string> Parents { get; set; } = [];
}

internal sealed class GitHubCommitCreatedPayload
{
    public string Sha { get; set; } = string.Empty;
}

internal sealed class GitHubUpdateRefPayload
{
    public string Sha { get; set; } = string.Empty;

    public bool Force { get; set; }
}

internal sealed class GitHubDeletePayload
{
    public string Message { get; set; } = string.Empty;

    public string Sha { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;
}

internal sealed class GiteeWritePayload
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    public string? Sha { get; set; }
}

internal sealed class GiteeDeletePayload
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Sha { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;
}

internal sealed class GiteeCreateRepoPayload
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("private")]
    public bool Private { get; set; } = true;

    [JsonPropertyName("auto_init")]
    public bool AutoInit { get; set; } = true;
}

internal sealed class GiteaCreateRepoPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("private")]
    public bool Private { get; set; } = true;

    [JsonPropertyName("auto_init")]
    public bool AutoInit { get; set; } = true;
}

internal sealed class GitLabCreateProjectPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "private";

    [JsonPropertyName("initialize_with_readme")]
    public bool InitializeWithReadme { get; set; } = true;

    [JsonPropertyName("namespace_id")]
    public int? NamespaceId { get; set; }
}

internal sealed class GitLabNamespaceDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

internal sealed class GitLabWritePayload
{
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("commit_message")]
    public string CommitMessage { get; set; } = string.Empty;

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "base64";
}

internal sealed class GitLabDeletePayload
{
    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;

    [JsonPropertyName("commit_message")]
    public string CommitMessage { get; set; } = string.Empty;
}

internal sealed class GiteaWritePayload
{
    public string Branch { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Sha { get; set; }
}

internal sealed class GiteaDeletePayload
{
    public string Branch { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Sha { get; set; } = string.Empty;
}

internal static class PersonalSyncFailure
{
    public static async Task<Exception> CreateFailureAsync(string provider, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = string.IsNullOrWhiteSpace(text)
            ? response.ReasonPhrase ?? "unknown_error"
            : text.Length <= 240 ? text : text[..240];
        var friendly = BuildFriendlyMessage(provider, response.StatusCode);
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            return new InvalidOperationException($"{friendly}（HTTP {(int)response.StatusCode}）");
        }

        return new InvalidOperationException($"{provider} 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。服务返回：{detail}");
    }

    private static string BuildFriendlyMessage(string provider, HttpStatusCode statusCode)
    {
        return provider switch
        {
            "GitHub" => statusCode switch
            {
                HttpStatusCode.Unauthorized => "GitHub Token 无效或已过期。请重新生成 Token 并保存",
                HttpStatusCode.Forbidden => "GitHub 拒绝访问。请确认 Token 有仓库读写权限，或是否触发了 GitHub 访问限制",
                HttpStatusCode.NotFound => "GitHub 仓库未找到，或当前 Token 没有访问权限。请确认仓库名正确；如果仓库在组织下，请在高级配置填写仓库所有者/组织名；私有仓库需要 Token 具备 repo 权限",
                HttpStatusCode.Conflict => "GitHub 写入冲突。远端文件已被其他设备或上一次同步更新，请重新点击“立即同步”让燕子拉取最新版本后再提交",
                HttpStatusCode.UnprocessableEntity => "GitHub 无法处理本次写入。常见原因是文件过大、仓库规则限制或请求内容无效；燕子会对大文件改用 Git 数据接口，若仍失败请检查仓库权限和文件体积",
                _ => string.Empty
            },
            "Gitee" => statusCode switch
            {
                HttpStatusCode.Unauthorized => "Gitee Token 无效或已过期。请重新生成 Token 并保存",
                HttpStatusCode.Forbidden => "Gitee 拒绝访问。请确认 Token 有仓库读写权限",
                HttpStatusCode.NotFound => "Gitee 仓库未找到，或当前 Token 没有访问权限。请确认仓库名和所有者是否正确",
                _ => string.Empty
            },
            "GitLab" => statusCode switch
            {
                HttpStatusCode.Unauthorized => "GitLab Token 无效或已过期。请重新生成 Token 并保存",
                HttpStatusCode.Forbidden => "GitLab 拒绝访问。请确认 Token 有仓库读写权限",
                HttpStatusCode.NotFound => "GitLab 项目未找到，或当前 Token 没有访问权限。请确认项目路径是否正确，私有项目需要 Token 具备访问权限",
                _ => string.Empty
            },
            "Gitea" => statusCode switch
            {
                HttpStatusCode.Unauthorized => "Gitea Token 无效或已过期。请重新生成 Token 并保存",
                HttpStatusCode.Forbidden => "Gitea 拒绝访问。请确认 Token 有仓库读写权限",
                HttpStatusCode.NotFound => "Gitea 仓库未找到，或当前 Token 没有访问权限。请确认服务地址、仓库名和所有者是否正确",
                _ => string.Empty
            },
            "WebDAV" => statusCode switch
            {
                HttpStatusCode.Unauthorized => "WebDAV 用户名或密码不正确。坚果云请使用“应用密码”，不是登录密码",
                HttpStatusCode.Forbidden => "WebDAV 拒绝访问。请确认账号有目录读写权限，或根目录路径是否允许访问",
                HttpStatusCode.NotFound => "WebDAV 地址或根目录不存在。请确认服务器地址和同步目录填写正确",
                _ => string.Empty
            },
            "S3" => statusCode switch
            {
                HttpStatusCode.Unauthorized => "S3 Access Key 或 Secret 不正确。请重新检查密钥",
                HttpStatusCode.Forbidden => "S3 拒绝访问。请确认密钥具备 Bucket 读写权限",
                HttpStatusCode.NotFound => "S3 Bucket 或路径不存在。请确认 Bucket、区域和 Endpoint 是否正确",
                _ => string.Empty
            },
            _ => string.Empty
        };
    }
}
