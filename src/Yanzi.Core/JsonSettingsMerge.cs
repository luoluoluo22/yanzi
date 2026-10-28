using System.Text.Json.Nodes;

namespace Yanzi.Core;

/// <summary>Applies a page's changes without replacing concurrent edits to other properties.</summary>
public static class JsonSettingsMerge
{
    public static JsonNode? Apply(JsonNode? baseline, JsonNode? edited, JsonNode? current)
    {
        if (JsonNode.DeepEquals(baseline, edited)) return current?.DeepClone();
        if (baseline is not JsonObject before || edited is not JsonObject after || current is not JsonObject latest)
            return edited?.DeepClone();
        var result = (JsonObject)latest.DeepClone();
        foreach (var key in before.Select(x => x.Key).Union(after.Select(x => x.Key)))
        {
            var existed = before.TryGetPropertyValue(key, out var oldValue);
            var exists = after.TryGetPropertyValue(key, out var newValue);
            if (existed == exists && JsonNode.DeepEquals(oldValue, newValue)) continue;
            if (!exists) result.Remove(key);
            else result[key] = existed ? Apply(oldValue, newValue, latest[key]) : newValue?.DeepClone();
        }
        return result;
    }
}
