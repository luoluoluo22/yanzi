using System.Text.Json;
using System.Text.Json.Nodes;
using Yanzi.Core;

namespace OpenQuickHost;

/// <summary>Owns settings edit snapshots; views only submit their changed state.</summary>
internal sealed class SettingsPersistenceController
{
    private JsonNode baseline;
    public SettingsPersistenceController(AppSettings settings) => baseline = Snapshot(settings);
    public void Reset(AppSettings settings) => baseline = Snapshot(settings);
    public AppSettings Save(AppSettings edited)
    {
        var candidate = Snapshot(edited);
        var saved = AppSettingsStore.Update(current =>
            JsonSettingsMerge.Apply(baseline, candidate, Snapshot(current))!.Deserialize<AppSettings>(JsonDefaults.CamelCaseIndented)!);
        baseline = Snapshot(saved);
        return saved;
    }
    private static JsonNode Snapshot(AppSettings settings) => JsonSerializer.SerializeToNode(settings, JsonDefaults.CamelCaseIndented)!;
}
