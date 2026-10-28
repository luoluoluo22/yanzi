namespace OpenQuickHost.Sync;

public sealed class SyncOptions
{
    public const string DefaultBaseUrl = "https://sync.luoluoluo.cc.cd";

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);
}
