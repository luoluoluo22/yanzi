namespace OpenQuickHost.Sync;

public sealed class SyncOptions
{
    public string BaseUrl { get; init; } = string.Empty;

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);
}
