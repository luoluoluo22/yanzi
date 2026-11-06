using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// Discovers model IDs from the local Cookie Suite. The backend also advertises
/// static fallback IDs; advertising an ID is not proof of a working session.
/// Every discovered model ID is selectable. The category and availability fields
/// remain informational: an offline or special-purpose model must return its real
/// upstream outcome instead of being hidden by a client-side whitelist.
/// </summary>
public static class YanziCookieModelCatalog
{
    public sealed record Entry(string Id, string Category, string Availability, string Description);
    public sealed record Snapshot(
        bool BackendOnline, bool ReverseOnline, bool IsStale,
        DateTimeOffset CheckedAt, IReadOnlyList<Entry> Models,
        IReadOnlyList<string> SelectableModels);

    private const string BackendUrl = "http://127.0.0.1:18800";
    private const string ProviderId = "miniapp:cookie-api:cookie-api";
    private static readonly string[] FallbackModels =
    [
        "chatgpt-web", "gemini-3.5-flash-lite",
        "auto", "gpt-6-thinking", "gpt-6", "gpt-6-instant", "gpt-6-mini",
        "gpt-6-t-mini", "gpt-5-6-thinking", "gpt-5-6", "gpt-5-6-instant",
        "gpt-5-6-mini", "gpt-5-6-t-mini", "research", "gpt-image-2",
        "codex-gpt-image-2", "gpt-6-sol-wm", "gpt-6-luna-wm",
        "gpt-6-astra-wm", "gpt-6.1-sol-wm", "gpt-6.1-terra-wm",
        "gpt-5.6-sol-wm", "gpt-5.6-luna-wm", "gpt-5.6-terra-wm",
        "chatgpt-web-moni", "gemini-3.1-pro", "gemini-3.6-flash",
        "gemini-3.8-flash", "gemini-3.1-pro-thinking"
    ];
    private static readonly Regex SafeModelId = new(
        @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,119}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static Snapshot? _snapshot;
    private static DateTimeOffset _nextRefresh;

    private static Snapshot Fallback => new(false, false, true, DateTimeOffset.MinValue,
        FallbackModels.Select(m => new Entry(m,
            m.StartsWith("gemini-", StringComparison.Ordinal) ? "gemini" :
            m.StartsWith("chatgpt-web", StringComparison.Ordinal) ? "browser" :
            m is "gpt-image-2" or "codex-gpt-image-2" ? "image" :
            m == "research" ? "research" :
            m.EndsWith("-wm", StringComparison.Ordinal) ? "work" : "reverse",
            "offline", "")).ToArray(), FallbackModels);

    public static Snapshot Cached => Volatile.Read(ref _snapshot) ?? Fallback;

    public static async Task<Snapshot> RefreshAsync(
        bool force = false, CancellationToken cancellationToken = default)
    {
        var existing = Volatile.Read(ref _snapshot);
        if (!force && existing != null && DateTimeOffset.UtcNow < _nextRefresh)
            return existing;

        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            existing = Volatile.Read(ref _snapshot);
            if (!force && existing != null && DateTimeOffset.UtcNow < _nextRefresh)
                return existing;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(11));
            try
            {
                using var response = await Client.GetAsync(BackendUrl + "/v1/models",
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 262144)
                    throw new InvalidDataException("model catalog too large");

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var doc = await JsonDocument.ParseAsync(stream,
                    new JsonDocumentOptions { MaxDepth = 12 }, timeout.Token);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array || data.GetArrayLength() is < 1 or > 128)
                    throw new InvalidDataException("invalid model catalog");

                // Reverse transport health is metadata, not an entitlement gate.
                // Keep all discovered IDs even when its reverse service is offline.
                bool reverseOnline = false;
                try
                {
                    using var status = await Client.GetAsync(
                        BackendUrl + "/api/chatgpt/status", timeout.Token);
                    if (status.IsSuccessStatusCode)
                    {
                        using var statusJson = JsonDocument.Parse(
                            await status.Content.ReadAsStringAsync(timeout.Token));
                        if (statusJson.RootElement.TryGetProperty("reverse_engine", out var engine) &&
                            engine.ValueKind == JsonValueKind.Object &&
                            engine.TryGetProperty("running", out var running))
                            reverseOnline = running.ValueKind == JsonValueKind.True;
                    }
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // The model catalog still remains valid as an unverified list.
                }

                var list = new List<Entry>();
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var id = ReadString(item, "id");
                    if (!SafeModelId.IsMatch(id) || !ids.Add(id)) continue;
                    var tag = ReadString(item, "tag");
                    var scene = ReadString(item, "scene");
                    var category = id is "gpt-image-2" or "codex-gpt-image-2"
                        ? "image"
                        : id == "research" ? "research"
                        : scene == "work" || id.EndsWith("-wm", StringComparison.OrdinalIgnoreCase) ? "work"
                        : tag == "moni" ? "browser"
                        : tag == "pool" ? "gemini"
                        : tag == "reverse" ? "reverse" : "unknown";
                    var availability = category switch
                    {
                        "browser" or "gemini" => "available-to-test",
                        "reverse" or "work" => reverseOnline ? "unverified" : "backend-offline",
                        "image" or "research" => "special-capability",
                        _ => "unsupported"
                    };
                    list.Add(new Entry(id, category, availability,
                        ReadString(item, "description")[..Math.Min(180, ReadString(item, "description").Length)]));
                }
                if (list.Count == 0)
                    throw new InvalidDataException("empty model catalog");
                // No category-based whitelist: publish every backend-discovered ID.
                // Sort only for usability. The full catalog still carries availability.
                var selectable = list.Select(m => m.Id)
                    .OrderBy(m => m == "chatgpt-web" ? 0 :
                        m == "gemini-3.5-flash-lite" ? 1 : 2)
                    .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var result = new Snapshot(true, reverseOnline, false,
                    DateTimeOffset.UtcNow, list.ToArray(), selectable);
                Volatile.Write(ref _snapshot, result);
                _nextRefresh = DateTimeOffset.UtcNow.AddMinutes(3);
                return result;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or
                                        IOException or JsonException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                // An outage must not delete the user's working model selection.
                _nextRefresh = DateTimeOffset.UtcNow.AddSeconds(35);
                var old = Volatile.Read(ref _snapshot);
                var stale = old == null ? Fallback : old with { BackendOnline = false, IsStale = true };
                Volatile.Write(ref _snapshot, stale);
                return stale;
            }
        }
        finally { RefreshGate.Release(); }
    }

    private static string ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString()?.Trim() ?? "" : "";

    /// <summary>
    /// Updates only the miniapp-owned provider, preserving enablement and the
    /// user's selected model when still present. Never mutate unrelated providers.
    /// </summary>
    public static async Task<Snapshot> SyncRegisteredModelsAsync(
        bool force = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await RefreshAsync(force, cancellationToken);
        if (!snapshot.BackendOnline || snapshot.SelectableModels.Count == 0)
            return snapshot;
        var settings = AppSettingsStore.Load();
        var provider = settings.AiServiceProviders?.FirstOrDefault(p =>
            string.Equals(p.Id, ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.OwnerExtensionId, "cookie-api", StringComparison.OrdinalIgnoreCase));
        if (provider == null) return snapshot;

        var desired = snapshot.SelectableModels.ToArray();
        if (provider.Models != null && provider.Models.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase) &&
            desired.Contains(provider.SelectedModel ?? "", StringComparer.OrdinalIgnoreCase))
            return snapshot;

        AppSettingsStore.Update(current =>
        {
            var owned = current.AiServiceProviders?.FirstOrDefault(p =>
                string.Equals(p.Id, ProviderId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.OwnerExtensionId, "cookie-api", StringComparison.OrdinalIgnoreCase));
            if (owned == null) return current;
            var previousSelection = owned.SelectedModel;
            owned.Models = desired.ToList();
            if (!desired.Contains(previousSelection ?? "", StringComparer.OrdinalIgnoreCase))
                owned.SelectedModel = desired.Contains("chatgpt-web", StringComparer.OrdinalIgnoreCase)
                    ? "chatgpt-web" : desired[0];
            return current;
        });
        return snapshot;
    }

    public static async Task RunSyncLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Always use the existing on-demand service; no new backend processes
            // or account sessions are started by discovery.
            while (!cancellationToken.IsCancellationRequested)
            {
                try { await SyncRegisteredModelsAsync(cancellationToken: cancellationToken); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or
                                            InvalidOperationException or TaskCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    HostAssets.AppendLog("[Cookie API] Model catalog sync skipped: " + e.GetType().Name);
                }
                await Task.Delay(TimeSpan.FromMinutes(3), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
