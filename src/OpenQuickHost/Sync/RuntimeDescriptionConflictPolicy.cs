using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost.Sync;

/// <summary>
/// Accepts a remote runtime configuration only when the sole difference is that it
/// supplies missing descriptions for otherwise identical environment variables.
/// Never treats a changed credential, deleted variable or unrelated setting as safe.
/// </summary>
internal static class RuntimeDescriptionConflictPolicy
{
    internal static bool CanAdoptRemote(LauncherConfigObjectEnvelope local, LauncherConfigObjectEnvelope remote)
    {
        if (!string.Equals(local.ObjectId, "settings.runtime", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(remote.ObjectId, "settings.runtime", StringComparison.OrdinalIgnoreCase) ||
            local.Deleted || remote.Deleted ||
            local.Payload.ValueKind != JsonValueKind.Object ||
            remote.Payload.ValueKind != JsonValueKind.Object)
            return false;

        if (JsonNode.Parse(local.Payload.GetRawText()) is not JsonObject localRoot ||
            JsonNode.Parse(remote.Payload.GetRawText()) is not JsonObject remoteRoot ||
            !TryGetEnvironmentVariables(localRoot, out var localVariables) ||
            !TryGetEnvironmentVariables(remoteRoot, out var remoteVariables) ||
            localVariables.Count != remoteVariables.Count)
            return false;

        // Everything outside the environment-variable collection must be identical.
        var localSettings = (JsonObject)localRoot.DeepClone();
        var remoteSettings = (JsonObject)remoteRoot.DeepClone();
        localSettings.Remove("environmentVariables");
        remoteSettings.Remove("environmentVariables");
        if (!JsonNode.DeepEquals(localSettings, remoteSettings))
            return false;

        var suppliedDescription = false;
        foreach (var (name, localVariable) in localVariables)
        {
            if (!remoteVariables.TryGetValue(name, out var remoteVariable))
                return false;

            var localWithoutDescription = (JsonObject)localVariable.DeepClone();
            var remoteWithoutDescription = (JsonObject)remoteVariable.DeepClone();
            localWithoutDescription.Remove("description");
            remoteWithoutDescription.Remove("description");
            if (!JsonNode.DeepEquals(localWithoutDescription, remoteWithoutDescription))
                return false; // Includes the secret value: never overwrite diverging credentials.

            var localDescription = ReadDescription(localVariable);
            var remoteDescription = ReadDescription(remoteVariable);
            if (string.Equals(localDescription, remoteDescription, StringComparison.Ordinal))
                continue;
            if (!string.IsNullOrWhiteSpace(localDescription) ||
                string.IsNullOrWhiteSpace(remoteDescription))
                return false;
            suppliedDescription = true;
        }

        return suppliedDescription;
    }

    private static bool TryGetEnvironmentVariables(
        JsonObject payload, out Dictionary<string, JsonObject> variables)
    {
        variables = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (payload["environmentVariables"] is not JsonArray array)
            return false;
        foreach (var node in array)
        {
            if (node is not JsonObject variable ||
                variable["name"] is not JsonValue nameNode ||
                !nameNode.TryGetValue<string>(out var name) ||
                string.IsNullOrWhiteSpace(name) ||
                !variables.TryAdd(name, variable))
                return false;
        }
        return true;
    }

    private static string ReadDescription(JsonObject variable)
    {
        return variable["description"] is JsonValue value &&
               value.TryGetValue<string>(out var description)
            ? description ?? string.Empty
            : string.Empty;
    }
}
