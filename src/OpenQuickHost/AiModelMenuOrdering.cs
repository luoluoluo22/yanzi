namespace OpenQuickHost;

internal static class AiModelMenuOrdering
{
    // Explicit choices precede the discovered catalog, with the current choice first.
    // Distinct preserves the manual order and does not mutate persistent configuration.
    internal static string[] Order(IEnumerable<string>? configured,
        IEnumerable<string>? discovered, string? selectedModel)
    {
        return (configured ?? []).Concat(discovered ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => string.Equals(model, selectedModel,
                StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToArray();
    }
}
