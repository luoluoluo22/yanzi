using System.IO;
using System.Security.Cryptography;

namespace OpenQuickHost;

/// <summary>Content identity of the main application assembly, independent of marketing version.</summary>
public static class ReleaseBuildIdentity
{
    private static readonly Lazy<string> CachedHostHash = new(() =>
    {
        using var file = File.OpenRead(typeof(App).Assembly.Location);
        return Convert.ToHexString(SHA256.HashData(file));
    });

    public static string AssemblySha256 => CachedHostHash.Value;
}
