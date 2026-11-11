namespace OpenQuickHost.Sync;

public sealed class SyncOptions
{
    public const string RetiredBaseUrl = "https://sync.luoluoluo.cc.cd";

    public const string DefaultBaseUrl = "";

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public bool IsConfigured =>
        !IsRetiredHost(BaseUrl) &&
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsRetiredHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        return Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) &&
               uri.Host.Equals("sync.luoluoluo.cc.cd", StringComparison.OrdinalIgnoreCase);
    }

    public static SyncOptions WithoutRetiredHost(SyncOptions options) =>
        IsRetiredHost(options.BaseUrl) ? new SyncOptions { BaseUrl = string.Empty } : options;
}
