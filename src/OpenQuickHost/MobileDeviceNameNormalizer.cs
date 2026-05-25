namespace OpenQuickHost;

internal static class MobileDeviceNameNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> KnownModelNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["23113RKC6C"] = "Redmi K70"
    };

    public static string Normalize(string? value, string? fallback = null)
    {
        var candidate = FirstNonEmpty(value, fallback, "unknown");
        if (candidate.StartsWith("android-", StringComparison.OrdinalIgnoreCase))
        {
            return "Android 手机";
        }

        foreach (var (model, displayName) in KnownModelNames)
        {
            if (candidate.Equals(model, StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains(model, StringComparison.OrdinalIgnoreCase))
            {
                return displayName;
            }
        }

        return candidate;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
