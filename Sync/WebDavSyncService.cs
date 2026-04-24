using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace OpenQuickHost.Sync;

public sealed class WebDavSyncService
{
    private readonly AppSettings _settings;
    private readonly SavedWebDavCredential? _credential;
    private readonly HttpClient _httpClient;

    public WebDavSyncService(AppSettings settings)
    {
        _settings = settings;
        _credential = WebDavCredentialStore.Load();
        if (string.IsNullOrWhiteSpace(_settings.WebDavServerUrl))
        {
            throw new InvalidOperationException("WebDAV 服务地址未配置。");
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(EnsureTrailingSlash(_settings.WebDavServerUrl), UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        if (_credential != null && !string.IsNullOrWhiteSpace(_credential.Password))
        {
            var raw = $"{_settings.WebDavUsername}:{_credential.Password}";
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
    }

    public bool IsConfigured =>
        _settings.EnableWebDavSync &&
        Uri.TryCreate(_settings.WebDavServerUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(_settings.WebDavUsername) &&
        !string.IsNullOrWhiteSpace(_credential?.Password);

    public string SyncRootDisplay => $"{_settings.WebDavServerUrl.TrimEnd('/')}{NormalizeRootPath(_settings.WebDavRootPath)}";

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureCollectionTreeAsync(cancellationToken, NormalizeRootPath(_settings.WebDavRootPath).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        await EnsureCollectionAsync("extensions", cancellationToken);
    }

    public async Task<WebDavSyncResult> SyncExtensionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await ProbeAsync(cancellationToken);

        var pulled = 0;
        var uploaded = 0;
        var remoteExtensionIds = await ListChildCollectionsAsync("extensions", cancellationToken);
        foreach (var remoteExtensionId in remoteExtensionIds)
        {
            var localDirectory = Path.Combine(HostAssets.ExtensionsPath, remoteExtensionId);
            if (Directory.Exists(localDirectory))
            {
                continue;
            }

            await DownloadExtensionAsync(remoteExtensionId, localDirectory, cancellationToken);
            pulled++;
        }

        if (Directory.Exists(HostAssets.ExtensionsPath))
        {
            foreach (var localDirectory in Directory.EnumerateDirectories(HostAssets.ExtensionsPath))
            {
                var extensionId = Path.GetFileName(localDirectory);
                if (string.IsNullOrWhiteSpace(extensionId))
                {
                    continue;
                }

                await UploadExtensionAsync(extensionId, localDirectory, cancellationToken);
                uploaded++;
            }
        }

        return new WebDavSyncResult(uploaded, pulled, SyncRootDisplay);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("WebDAV 未完整配置，请先填写地址、用户名并设置密码。");
        }
    }

    private async Task UploadExtensionAsync(string extensionId, string localDirectory, CancellationToken cancellationToken)
    {
        await EnsureCollectionAsync($"extensions/{extensionId}", cancellationToken);
        foreach (var directory in Directory.EnumerateDirectories(localDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(localDirectory, directory).Replace('\\', '/');
            await EnsureCollectionAsync($"extensions/{extensionId}/{relative}", cancellationToken);
        }

        foreach (var filePath in Directory.EnumerateFiles(localDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(localDirectory, filePath).Replace('\\', '/');
            var remotePath = $"extensions/{extensionId}/{relative}";
            using var request = CreateRequest(HttpMethod.Put, remotePath);
            request.Content = new ByteArrayContent(await File.ReadAllBytesAsync(filePath, cancellationToken));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task DownloadExtensionAsync(string extensionId, string localDirectory, CancellationToken cancellationToken)
    {
        var remoteEntries = await ListDescendantsAsync($"extensions/{extensionId}", cancellationToken);
        Directory.CreateDirectory(localDirectory);
        foreach (var entry in remoteEntries.Where(static x => x.IsCollection))
        {
            if (string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(localDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        foreach (var entry in remoteEntries.Where(static x => !x.IsCollection))
        {
            var localPath = Path.Combine(localDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            var bytes = await _httpClient.GetByteArrayAsync(BuildRelativeUri($"extensions/{extensionId}/{entry.RelativePath}"), cancellationToken);
            await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
        }
    }

    private async Task<List<string>> ListChildCollectionsAsync(string relativePath, CancellationToken cancellationToken)
    {
        var entries = await ListAsync(relativePath, depth: 1, cancellationToken);
        return entries
            .Where(static x => x.IsCollection && !string.IsNullOrWhiteSpace(x.Name))
            .Select(static x => x.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<WebDavEntry>> ListDescendantsAsync(string relativePath, CancellationToken cancellationToken)
    {
        return (await ListAsync(relativePath, depth: "infinity", cancellationToken))
            .Where(static x => !string.IsNullOrWhiteSpace(x.RelativePath))
            .ToList();
    }

    private async Task<List<WebDavEntry>> ListAsync(string relativePath, object depth, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("PROPFIND"), relativePath);
        request.Headers.Add("Depth", depth.ToString());
        request.Content = new StringContent(
            """
<?xml version="1.0" encoding="utf-8" ?>
<propfind xmlns="DAV:">
  <prop>
    <displayname />
    <resourcetype />
  </prop>
</propfind>
""",
            Encoding.UTF8,
            "application/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.MultiStatus && response.StatusCode != HttpStatusCode.OK)
        {
            response.EnsureSuccessStatusCode();
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseEntries(xml, relativePath);
    }

    private List<WebDavEntry> ParseEntries(string xml, string requestedPath)
    {
        var document = XDocument.Parse(xml);
        var baseRelative = NormalizeRelativePath(requestedPath);
        var absoluteRootPrefix = new Uri(_httpClient.BaseAddress!, BuildRelativeUri(string.Empty)).AbsolutePath.TrimEnd('/') + "/";
        var items = new List<WebDavEntry>();
        foreach (var response in document.Descendants().Where(x => x.Name.LocalName == "response"))
        {
            var hrefValue = response.Descendants().FirstOrDefault(x => x.Name.LocalName == "href")?.Value;
            if (string.IsNullOrWhiteSpace(hrefValue))
            {
                continue;
            }

            var absolute = Uri.TryCreate(hrefValue, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(_httpClient.BaseAddress!, hrefValue);
            var absolutePath = Uri.UnescapeDataString(absolute.AbsolutePath);
            var relativeAbsolute = absolutePath.StartsWith(absoluteRootPrefix, StringComparison.OrdinalIgnoreCase)
                ? absolutePath[absoluteRootPrefix.Length..]
                : absolutePath.Trim('/');
            if (string.Equals(relativeAbsolute.Trim('/'), baseRelative.Trim('/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!relativeAbsolute.StartsWith(baseRelative.Trim('/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = relativeAbsolute[baseRelative.Trim('/').Length..].Trim('/');
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var isCollection = response.Descendants().Any(x => x.Name.LocalName == "collection");
            items.Add(new WebDavEntry(relativePath, Path.GetFileName(relativePath.TrimEnd('/')), isCollection));
        }

        return items;
    }

    private async Task EnsureCollectionTreeAsync(CancellationToken cancellationToken, params string[] segments)
    {
        var current = string.Empty;
        foreach (var segment in segments)
        {
            current = string.IsNullOrWhiteSpace(current) ? segment : $"{current}/{segment}";
            await EnsureCollectionAsync(current, cancellationToken);
        }
    }

    private async Task EnsureCollectionAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("MKCOL"), relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.Conflict)
        {
            if (await CollectionExistsAsync(relativePath, cancellationToken))
            {
                return;
            }

            throw new InvalidOperationException($"WebDAV 目录不可用：{relativePath}，服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task<bool> CollectionExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("PROPFIND"), relativePath);
        request.Headers.Add("Depth", "0");
        request.Content = new StringContent(
            """
<?xml version="1.0" encoding="utf-8" ?>
<propfind xmlns="DAV:">
  <prop>
    <resourcetype />
  </prop>
</propfind>
""",
            Encoding.UTF8,
            "application/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.StatusCode == HttpStatusCode.MultiStatus ||
               response.StatusCode == HttpStatusCode.OK;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        return new HttpRequestMessage(method, BuildRelativeUri(relativePath));
    }

    private string BuildRelativeUri(string relativePath)
    {
        var root = NormalizeRootPath(_settings.WebDavRootPath).Trim('/');
        var suffix = NormalizeRelativePath(relativePath);
        return string.IsNullOrWhiteSpace(suffix)
            ? root + "/"
            : root + "/" + string.Join("/", suffix.Split('/').Select(Uri.EscapeDataString));
    }

    private static string NormalizeRootPath(string? rootPath)
    {
        var value = string.IsNullOrWhiteSpace(rootPath) ? "/yanzi" : rootPath.Trim();
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        return value.TrimEnd('/');
    }

    private static string NormalizeRelativePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private sealed record WebDavEntry(string RelativePath, string? Name, bool IsCollection);
}

public sealed record WebDavSyncResult(int UploadedCount, int PulledCount, string RemoteRoot);
