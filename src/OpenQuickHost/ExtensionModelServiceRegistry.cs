using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Declarative, extension-owned model provider registrations. Reading manifests never
/// executes an extension, starts its window, or installs a dependency.
/// A missing/invalidated extension retracts ONLY its own registrations.
/// </summary>
internal static class ExtensionModelServiceRegistry
{
    private const string Prefix = "miniapp:";
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    internal static void Reconcile(AppSettings settings, string? extensionsRoot = null)
    {
        settings.AiServiceProviders ??= [];
        extensionsRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "Extensions");

        var registrations = new Dictionary<string, AiServiceProviderSettings>(IdComparer);
        try
        {
            if (Directory.Exists(extensionsRoot))
            {
                foreach (var directory in Directory.EnumerateDirectories(extensionsRoot))
                {
                    var extensionId = Path.GetFileName(directory);
                    var manifestPath = Path.Combine(directory, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath),
                            new JsonDocumentOptions { MaxDepth = 24 });
                        var manifest = doc.RootElement;
                        if (manifest.ValueKind != JsonValueKind.Object ||
                            !StringEquals(GetString(manifest, "id"), extensionId) ||
                            !manifest.TryGetProperty("modelServices", out var services) ||
                            services.ValueKind != JsonValueKind.Array) continue;

                        foreach (var entry in services.EnumerateArray())
                        {
                            if (entry.ValueKind != JsonValueKind.Object) continue;
                            var serviceId = GetString(entry, "id");
                            var type = GetString(entry, "providerType");
                            var baseUrl = GetString(entry, "baseUrl");
                            if (!IsSafeId(serviceId) || string.IsNullOrWhiteSpace(type) ||
                                !Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
                                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
                                endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0) continue;

                            var credential = GetString(entry, "credential");
                            var isAgy = StringEquals(credential, "agy-local");
                            var isCookieApi = StringEquals(credential, "cookie-api-local");
                            if (isAgy && (!StringEquals(extensionId, "agy-model-service") ||
                                !StringEquals(type, YanziAgyModelBridge.ProviderType) ||
                                !StringEquals(baseUrl.TrimEnd('/'), YanziAgyModelBridge.BaseUrl))) continue;
                            if (isCookieApi && (!StringEquals(extensionId, "cookie-api") ||
                                !StringEquals(type, YanziCookieApiBridge.ProviderType) ||
                                !StringEquals(baseUrl.TrimEnd('/'), YanziCookieApiBridge.BaseUrl))) continue;
                            // No manifest can reference another local user's authentication token.
                            if (!string.IsNullOrEmpty(credential) && !isAgy && !isCookieApi) continue;

                            var key = Prefix + extensionId + ":" + serviceId;
                            var models = new List<string>();
                            if (entry.TryGetProperty("models", out var listed) &&
                                listed.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var model in listed.EnumerateArray())
                                {
                                    if (model.ValueKind != JsonValueKind.String) continue;
                                    var name = model.GetString()?.Trim();
                                    if (!string.IsNullOrWhiteSpace(name) && name.Length <= 160 &&
                                        !models.Contains(name, StringComparer.OrdinalIgnoreCase))
                                        models.Add(name);
                                    if (models.Count == 64) break;
                                }
                            }
                            var initialModel = GetString(entry, "selectedModel");
                            if (string.IsNullOrWhiteSpace(initialModel))
                                initialModel = models.FirstOrDefault() ?? string.Empty;
                            if (initialModel.Length > 160) continue;
                            registrations[key] = new AiServiceProviderSettings
                            {
                                Id = key,
                                OwnerExtensionId = extensionId,
                                Name = GetString(entry, "name") is { Length: > 0 } providerName ? providerName : extensionId,
                                ProviderType = type,
                                BaseUrl = baseUrl,
                                ApiKey = isAgy ? YanziAgyModelBridge.GetLocalKey()
                                    : isCookieApi ? YanziCookieApiBridge.GetLocalKey() : string.Empty,
                                IsEnabled = true,
                                Models = models,
                                SelectedModel = initialModel
                            };
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        HostAssets.AppendLog($"[ModelRegistry] Skipped invalid extension manifest: id={extensionId}, error={ex.GetType().Name}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HostAssets.AppendLog($"[ModelRegistry] Unable to scan extensions: {ex.GetType().Name}");
            return; // Fail closed on deletion: do not delete registrations on an I/O failure.
        }

        // All auto-managed IDs are fully derived from local manifests. A deleted
        // extension may never delete or change manually created providers.
        settings.AiServiceProviders.RemoveAll(provider =>
            (provider.Id?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true ||
             !string.IsNullOrWhiteSpace(provider.OwnerExtensionId)) &&
            !registrations.ContainsKey(provider.Id ?? string.Empty));

        foreach (var discovered in registrations.Values)
        {
            var current = settings.AiServiceProviders.FirstOrDefault(p => StringEquals(p.Id, discovered.Id));
            if (current is null)
            {
                settings.AiServiceProviders.Add(discovered);
                continue;
            }

            current.OwnerExtensionId = discovered.OwnerExtensionId;
            current.Name = discovered.Name;
            current.ProviderType = discovered.ProviderType;
            current.BaseUrl = discovered.BaseUrl;
            // Device-local AGY token is never taken from a synced account setting.
            if (StringEquals(discovered.ProviderType, YanziAgyModelBridge.ProviderType) ||
                (StringEquals(discovered.ProviderType, YanziCookieApiBridge.ProviderType) &&
                 StringEquals(discovered.OwnerExtensionId, "cookie-api")))
                current.ApiKey = discovered.ApiKey;
            if (current.Models is null || current.Models.Count == 0)
                current.Models = discovered.Models;
            if (string.IsNullOrWhiteSpace(current.SelectedModel))
                current.SelectedModel = discovered.SelectedModel;
            // Intentionally preserve user-selected models and IsEnabled.
        }

        if (!string.IsNullOrWhiteSpace(settings.ActiveServiceProviderId) &&
            !settings.AiServiceProviders.Any(p => StringEquals(p.Id, settings.ActiveServiceProviderId)))
            settings.ActiveServiceProviderId = settings.AiServiceProviders.FirstOrDefault(p => p.IsEnabled)?.Id ?? string.Empty;
    }

    private static bool StringEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private static string GetString(JsonElement source, string key) =>
        source.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
}
