using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost;

public static class AppEnvironmentVariableStore
{
    private static string SecretPath => HostAssets.ResolveDataFilePath("environment-variables.dat");

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "YANZI_INPUT",
        "YANZI_CONTEXT_PATH",
        "YANZI_STATE_UPDATES_PATH",
        "YANZI_READY_PATH",
        "YANZI_EXTENSION_ID",
        "YANZI_EXTENSION_DIR",
        "YANZI_EXTENSION_DATA_DIR",
        "YANZI_LAUNCH_SOURCE",
        "YANZI_AGENT_API_BASE_URL",
        "YANZI_AGENT_API_TOKEN",
        "YANZI_HOST_LOG_PATH"
    };

    public static IReadOnlyList<AppEnvironmentVariableSettings> Load()
    {
        var secretValues = LoadSecretValues();
        return AppSettingsStore.Load().EnvironmentVariables
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item =>
            {
                var name = NormalizeName(item.Name);
                return new AppEnvironmentVariableSettings
                {
                    Name = name,
                    Value = secretValues.TryGetValue(name, out var secretValue) ? secretValue : item.Value ?? string.Empty,
                    Description = item.Description ?? string.Empty
                };
            })
            .Where(static item => !ReservedNames.Contains(item.Name))
            .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Save(IEnumerable<AppEnvironmentVariableSettings> variables)
    {
        var settings = AppSettingsStore.Load();
        var normalizedVariables = variables
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(static item => new AppEnvironmentVariableSettings
            {
                Name = NormalizeName(item.Name),
                Value = item.Value ?? string.Empty,
                Description = item.Description ?? string.Empty
            })
            .Where(static item => !ReservedNames.Contains(item.Name))
            .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SaveSecretValues(normalizedVariables.ToDictionary(
            static item => item.Name,
            static item => item.Value ?? string.Empty,
            StringComparer.OrdinalIgnoreCase));

        settings.EnvironmentVariables = normalizedVariables
            .Select(static item => new AppEnvironmentVariableSettings
            {
                Name = item.Name,
                Value = string.Empty,
                Description = item.Description ?? string.Empty
            })
            .ToList();
        AppSettingsStore.Save(settings);
    }

    public static string? GetValue(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalizedName = NormalizeName(name);
        if (ReservedNames.Contains(normalizedName))
        {
            return null;
        }

        return Load()
            .FirstOrDefault(item => item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    public static void ApplyToEnvironment(Action<string, string?> setVariable)
    {
        foreach (var variable in Load())
        {
            if (IsValidEnvironmentName(variable.Name))
            {
                setVariable(variable.Name, variable.Value ?? string.Empty);
            }
        }
    }

    public static IReadOnlyList<string> GetEnvironmentNames()
    {
        return Load()
            .Select(static item => item.Name)
            .Where(IsValidEnvironmentName)
            .ToArray();
    }

    public static string NormalizeName(string? name)
    {
        return (name ?? string.Empty).Trim();
    }

    public static bool IsValidEnvironmentName(string? name)
    {
        var normalized = NormalizeName(name);
        return !string.IsNullOrWhiteSpace(normalized) &&
               !normalized.Contains('=') &&
               !ReservedNames.Contains(normalized);
    }

    private static Dictionary<string, string> LoadSecretValues()
    {
        if (!File.Exists(SecretPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(SecretPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(bytes)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveSecretValues(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(values);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SecretPath, protectedBytes);
    }
}

public sealed class AppEnvironmentVariableSettings
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
