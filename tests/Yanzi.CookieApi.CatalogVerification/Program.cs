using OpenQuickHost;
using System.Net.Http;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception("CHECK_FAILED: " + label);
    Console.WriteLine("PASS " + label);
}
var before = AppSettingsStore.Load();
var providerBefore = before.AiServiceProviders.FirstOrDefault(p =>
    p.Id == "miniapp:cookie-api:cookie-api");
var otherProviderCount = before.AiServiceProviders.Count(p => p.Id != "miniapp:cookie-api:cookie-api");
using var client = new HttpClient(new HttpClientHandler { UseProxy = false })
{ Timeout = TimeSpan.FromSeconds(3) };
try
{
    using var health = await client.GetAsync("http://127.0.0.1:18800/api/hub/status");
    if (!health.IsSuccessStatusCode)
    {
        Console.WriteLine("SKIP no running local Cookie Suite");
        return;
    }
}
catch (HttpRequestException)
{
    Console.WriteLine("SKIP no local Cookie Suite");
    return;
}
var snapshot = await YanziCookieModelCatalog.RefreshAsync(force: true);
Check(snapshot.BackendOnline && !snapshot.IsStale, "live backend catalog");
Check(snapshot.Models.Count >= 7, "live model discovery");
Check(snapshot.SelectableModels.Contains("chatgpt-web"), "browser model included");
Check(snapshot.SelectableModels.Contains("gemini-3.5-flash-lite"), "Gemini model included");
Check(snapshot.SelectableModels.Contains("gemini-3.1-pro"), "other Gemini model included");
Check(snapshot.SelectableModels.Contains("gpt-6"), "GPT reverse models selectable even offline");
Check(snapshot.SelectableModels.Contains("auto"), "auto route selectable");
Check(snapshot.SelectableModels.Contains("gpt-image-2"), "image model exposed in selector");
Check(snapshot.SelectableModels.Contains("codex-gpt-image-2"), "Codex image model exposed");
Check(snapshot.SelectableModels.Contains("research"), "research model exposed");
Check(snapshot.SelectableModels.Contains("gpt-6.1-terra-wm"), "Work model exposed");
Check(snapshot.Models.Count == snapshot.SelectableModels.Count,
    "no discovered model blocked by category filtering");
Check(snapshot.SelectableModels.Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
      snapshot.SelectableModels.Count, "no duplicate published model ids");
Check(snapshot.Models.Any(m => m.Id == "gpt-6" && m.Category == "reverse"),
    "reverse candidate visible");
var synced = await YanziCookieModelCatalog.SyncRegisteredModelsAsync(force: true);
Check(synced.SelectableModels.Count == snapshot.SelectableModels.Count, "sync same model set");
var after = AppSettingsStore.Load();
var registered = after.AiServiceProviders.FirstOrDefault(p =>
    p.Id == "miniapp:cookie-api:cookie-api");
Check(registered != null, "miniapp-owned provider remains registered");
Check(registered!.Models.SequenceEqual(snapshot.SelectableModels), "registered dynamic catalog");
Check(after.AiServiceProviders.Count(p => p.Id != "miniapp:cookie-api:cookie-api") ==
      otherProviderCount, "unrelated providers preserved");
if (providerBefore != null &&
    snapshot.SelectableModels.Contains(providerBefore.SelectedModel ?? ""))
    Check(registered.SelectedModel == providerBefore.SelectedModel,
        "default model preserved");
Console.WriteLine("ALL_CATALOG_CHECKS_PASSED discovered=" + snapshot.Models.Count +
                  " selectable=" + snapshot.SelectableModels.Count);
