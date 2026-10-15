using System.Reflection;
using Microsoft.CodeAnalysis;
using Yanzi.UI.Wpf;

namespace OpenQuickHost;

/// <summary>
/// Trusted, first-party C# extension design-system reference.
/// External extensions never select a DLL path or load arbitrary dependencies.
/// </summary>
internal static class YanziSharedUiCompilation
{
    private static readonly Assembly SharedAssembly = typeof(YanziUi).Assembly;

    internal static string CacheIdentity =>
        SharedAssembly.ManifestModule.ModuleVersionId.ToString("N");

    internal static MetadataReference BuildReference() =>
        MetadataReference.CreateFromFile(SharedAssembly.Location);

    internal static Assembly? Resolve(AssemblyName requested) =>
        string.Equals(requested.Name, SharedAssembly.GetName().Name, StringComparison.Ordinal)
            ? SharedAssembly
            : null;
}
