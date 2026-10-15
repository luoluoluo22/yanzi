using System.IO;

namespace OpenQuickHost;

public static class YanziDeviceResourceAuthorization
{
    public static bool AllowsFile(string path, IEnumerable<string> roots)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var candidate = ResolvePhysicalPath(path);
            return roots.Where(Path.IsPathFullyQualified).Select(ResolvePhysicalPath).Any(root =>
                candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    private static string ResolvePhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo? entry = Directory.Exists(current) ? new DirectoryInfo(current) : File.Exists(current) ? new FileInfo(current) : null;
            if (entry?.LinkTarget != null) current = entry.ResolveLinkTarget(true)?.FullName ?? throw new IOException("unresolved_resource_link");
        }
        return Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
    }
}
