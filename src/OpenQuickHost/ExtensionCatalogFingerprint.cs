using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost;

internal static class ExtensionCatalogFingerprint
{
    internal static string Compute(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in EnumerateSources(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            hash.AppendData(Encoding.UTF8.GetBytes(path + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks + "\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static IEnumerable<string> EnumerateSources(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory))
                if (Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) yield return path;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (name.Equals(".yanzi-csharp-cache", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".yanzi-install-", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".yanzi-backup-", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".yanzi-staging-", StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
        }
    }
}
