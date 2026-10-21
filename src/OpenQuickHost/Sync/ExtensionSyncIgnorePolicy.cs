using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenQuickHost.Sync;

/// <summary>
/// Shared by extension archives and the file watcher. Git ignore syntax is
/// evaluated relative to each ignore file, including nested files and negation.
/// Yanzi rules are additional exclusions; neither file may undo safety rules.
/// </summary>
internal static class ExtensionSyncIgnorePolicy
{
    private sealed record Rule(string BasePath, Regex Pattern, bool Negated, bool DirectoryOnly);
    private sealed record CachedRules(DateTime LastWriteUtc, long Length, Rule[] Rules);
    private static readonly ConcurrentDictionary<string, CachedRules> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static bool ShouldInclude(string root, string filePath, bool isDirectory = false)
    {
        var relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');
        if (relative == "." || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            return false;
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(IsHardExcluded))
            return false;

        // Do not read outside the extension tree or upload junction/symlink targets.
        var current = Path.GetFullPath(root);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        // The parent directory must itself be reachable. A negation cannot
        // re-include a file inside an ignored parent without re-including it.
        for (var i = 1; i <= parts.Length; i++)
        {
            var candidate = string.Join("/", parts.Take(i));
            var directorySegment = i < parts.Length || isDirectory;
            if (Ignored(root, candidate, directorySegment, ".gitignore") ||
                Ignored(root, candidate, directorySegment, ".yanziignore"))
                return false;
        }
        return true;
    }

    internal static IEnumerable<string> EnumerateIncludedFiles(string root)
    {
        // Do not recurse through Git object stores, ignored directories,
        // symlinks or runtime dependency trees just to reject every file.
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (ShouldInclude(root, file))
                    yield return file;
            }
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (ShouldInclude(root, directory, isDirectory: true))
                    pending.Push(directory);
            }
        }
    }

    private static bool IsHardExcluded(string part)
    {
        if (part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".hg", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".svn", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".runtime", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".raccoon-runtime", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".yanzi-csharp-cache", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".ssh", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".aws", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("synccredentials.dat", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("appsettings.local.json", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("debug.log", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith(".yanzi-inline-", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith(".yanzi-device", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals(".env.example", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
            part.Contains(".bak-", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
            part.Contains(".backup-", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".p12", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
            part.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool Ignored(string root, string candidate, bool isDirectory, string ignoreName)
    {
        var segments = candidate.Split('/');
        var excluded = false;
        // Include rules in the root and all ancestors of candidate; the
        // deepest .gitignore is evaluated last as Git specifies.
        for (var depth = 0; depth < segments.Length; depth++)
        {
            var basePath = string.Join("/", segments.Take(depth));
            var file = Path.Combine(root, basePath.Replace('/', Path.DirectorySeparatorChar), ignoreName);
            foreach (var rule in ReadRules(file, basePath))
            {
                if (!candidate.StartsWith(basePath.Length == 0 ? "" : basePath + "/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var suffix = basePath.Length == 0 ? candidate : candidate[(basePath.Length + 1)..];
                if ((!rule.DirectoryOnly || isDirectory) && rule.Pattern.IsMatch(suffix))
                    excluded = !rule.Negated;
            }
        }
        return excluded;
    }

    private static Rule[] ReadRules(string path, string basePath)
    {
        if (!File.Exists(path)) return [];
        var info = new FileInfo(path);
        if (Cache.TryGetValue(path, out var cached) &&
            cached.LastWriteUtc == info.LastWriteTimeUtc && cached.Length == info.Length)
            return cached.Rules;
        var result = new List<Rule>();
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith(@"\#", StringComparison.Ordinal)) line = line[1..];
            var negate = line.StartsWith('!');
            if (negate) line = line[1..];
            else if (line.StartsWith(@"\!", StringComparison.Ordinal)) line = line[1..];
            // Git ignores trailing unescaped spaces.
            while (line.EndsWith(' ') && !line.EndsWith(@"\ ", StringComparison.Ordinal))
                line = line[..^1];
            if (line.Length == 0) continue;
            var directoryOnly = line.EndsWith('/');
            if (directoryOnly) line = line[..^1];
            var anchored = line.StartsWith('/');
            if (anchored) line = line[1..];
            if (line.Length == 0) continue;
            var hasSlash = line.Contains('/');
            var regex = new StringBuilder();
            regex.Append(anchored || hasSlash ? "^" : "(?:^|/)");
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '\\' && i + 1 < line.Length)
                {
                    regex.Append(Regex.Escape(line[++i].ToString()));
                    continue;
                }
                if (ch == '*')
                {
                    if (i + 1 < line.Length && line[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < line.Length && line[i + 1] == '/')
                        {
                            i++;
                            regex.Append("(?:.*/)?");
                        }
                        else regex.Append(".*");
                    }
                    else regex.Append("[^/]*");
                }
                else if (ch == '?') regex.Append("[^/]");
                else if (ch == '[')
                {
                    var end = line.IndexOf(']', i + 1);
                    if (end > i + 1)
                    {
                        var content = line[(i + 1)..end];
                        regex.Append('[');
                        if (content.StartsWith('!')) regex.Append('^').Append(content[1..]);
                        else regex.Append(content);
                        regex.Append(']');
                        i = end;
                    }
                    else regex.Append(@"\[");
                }
                else regex.Append(Regex.Escape(ch.ToString()));
            }
            regex.Append('$');
            try { result.Add(new Rule(basePath, new Regex(regex.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant), negate, directoryOnly)); }
            catch (ArgumentException) { /* Invalid pattern cannot suppress protected files. */ }
        }
        var rules = result.ToArray();
        Cache[path] = new CachedRules(info.LastWriteTimeUtc, info.Length, rules);
        return rules;
    }
}