using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

    // Public surface plus embedded resource bytes. A rebuild that does not change
    // either keeps already compiled extensions valid.
    internal static string CacheIdentity { get; } = ComputeCacheIdentity();

    private static string ComputeCacheIdentity()
    {
        var builder = new StringBuilder();
        builder.Append(SharedAssembly.GetName().Version);
        builder.Append('\n');

        Type[] types;
        try
        {
            types = SharedAssembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(static type => type != null).Cast<Type>().ToArray();
        }

        foreach (var type in types.OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            builder.Append(type.FullName);
            builder.Append(':');
            builder.Append(type.BaseType?.FullName);
            builder.Append('\n');
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Select(static member => member.ToString() ?? string.Empty)
                         .OrderBy(static signature => signature, StringComparer.Ordinal))
            {
                builder.Append(member);
                builder.Append('\n');
            }
        }

        foreach (var name in SharedAssembly.GetManifestResourceNames().OrderBy(static name => name, StringComparer.Ordinal))
        {
            builder.Append(name);
            builder.Append(':');
            using var stream = SharedAssembly.GetManifestResourceStream(name);
            builder.Append(stream == null ? "missing" : Convert.ToHexString(SHA256.HashData(stream)));
            builder.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..16].ToLowerInvariant();
    }

    internal static MetadataReference BuildReference() =>
        MetadataReference.CreateFromFile(SharedAssembly.Location);

    internal static Assembly? Resolve(AssemblyName requested) =>
        string.Equals(requested.Name, SharedAssembly.GetName().Name, StringComparison.Ordinal)
            ? SharedAssembly
            : null;
}
