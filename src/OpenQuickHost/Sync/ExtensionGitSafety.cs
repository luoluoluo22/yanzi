using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

/// <summary>
/// Never replace/remove an extension Git worktree with an unrelated cloud ZIP.
/// A verified clean, locally available descendant commit may fast-forward; all
/// other inbound versions are retained as immutable conflict archives.
/// </summary>
internal static class ExtensionGitSafety
{
    internal const string ProvenanceEntry = ".yanzi-git-provenance.json";
    private sealed record Provenance(int SchemaVersion, string Commit);

    internal static bool HasGitWorktree(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));

    internal static void ValidateArchiveEntries(ZipArchive archive)
    {
        // Never let an incoming ZIP create/replace a Git repository or inject
        // local authentication/state, even when installing a new extension.
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            var parts = normalized.Split('/');
            if (parts.Any(part =>
                    part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals(".hg", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals(".svn", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals(".ssh", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
                    (part.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) &&
                     !part.Equals(".env.example", StringComparison.OrdinalIgnoreCase)) ||
                    part.Equals("appsettings.local.json", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("synccredentials.dat", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
                    part.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Git-native extension archive contains prohibited path: {normalized}");
        }
    }

    internal static void RemoveTransportMetadata(string staging)
    {
        var path = Path.Combine(staging, ProvenanceEntry);
        if (File.Exists(path)) File.Delete(path);
    }


    internal static void AddProvenance(ZipArchive archive, string directory)
    {
        if (!HasGitWorktree(directory) || !IsClean(directory)) return;
        var commit = RunGit(directory, "rev-parse", "HEAD");
        var topLevel = RunGit(directory, "rev-parse", "--show-toplevel");
        if (!IsCommit(commit) || string.IsNullOrWhiteSpace(topLevel) ||
            !string.Equals(Path.GetFullPath(topLevel).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return;
        var entry = archive.CreateEntry(ProvenanceEntry, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(new Provenance(1, commit!)));
    }

    // True means no further extraction/promotion is needed; the existing
    // worktree already contains this snapshot or has been fast-forwarded.
    internal static bool HandleIncoming(string target, byte[] package, string operation,
        string? archiveRootOverride = null)
    {
        if (!HasGitWorktree(target)) return false;
        if (HasSameContents(target, package)) return true;

        var incomingCommit = ReadCommit(package);
        if (incomingCommit != null && IsClean(target))
        {
            // The Git object may not yet exist on this device. Fetch only the
            // already configured origin; never trust a cloud ZIP to pick a URL.
            if (RunGit(target, "cat-file", "-t", incomingCommit) != "commit" &&
                !string.IsNullOrWhiteSpace(RunGit(target, "remote", "get-url", "origin")))
            {
                GitSucceeds(target, "fetch", "--no-tags", "origin");
            }
            if (RunGit(target, "cat-file", "-t", incomingCommit) == "commit" &&
                GitSucceeds(target, "merge-base", "--is-ancestor", "HEAD", incomingCommit))
            {
                // Git itself validates that this is a fast-forward. No ZIP content
                // is ever copied over tracked or untracked user files.
                if (GitSucceeds(target, "merge", "--ff-only", incomingCommit))
                {
                    HostAssets.AppendLog($"Git extension fast-forwarded: id={Path.GetFileName(target)}, commit={incomingCommit}");
                    return true;
                }
            }
        }

        var archivePath = PreserveIncoming(target, package, archiveRootOverride);
        throw new IOException($"Git 小程序有未合并的云端变更（{operation}）。已保留本地 .git 和源码；收到的快照保存在 {archivePath}。请通过 Git 合并后重新同步。");
    }

    internal static void GuardDeletion(string target, string operation)
    {
        if (HasGitWorktree(target))
            throw new IOException($"拒绝通过云同步{operation} Git 小程序 {Path.GetFileName(target)}；请先在本机明确处理 Git 工作目录。");
    }

    private static string PreserveIncoming(string target, byte[] package, string? archiveRootOverride)
    {
        var root = archiveRootOverride ?? Path.Combine(HostAssets.DataRootPath, "Backups", "git-extension-incoming");
        var safeId = Path.GetFileName(Path.TrimEndingDirectorySeparator(target));
        var destination = Path.Combine(root, safeId);
        Directory.CreateDirectory(destination);
        var hash = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        var path = Path.Combine(destination, hash + ".zip");
        if (!File.Exists(path))
        {
            var temp = Path.Combine(destination, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temp, package);
                File.Move(temp, path, overwrite: false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return path;
    }

    private static string? ReadCommit(byte[] package)
    {
        try
        {
            using var stream = new MemoryStream(package, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry(ProvenanceEntry);
            if (entry is not { Length: > 0 and < 1024 }) return null;
            using var reader = new StreamReader(entry.Open());
            var data = JsonSerializer.Deserialize<Provenance>(reader.ReadToEnd());
            return data is { SchemaVersion: 1 } && IsCommit(data.Commit) ? data.Commit : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return null;
        }
    }

    private static bool HasSameContents(string root, byte[] package)
    {
        try
        {
            using var stream = new MemoryStream(package, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var archived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                if (entry.FullName == ProvenanceEntry) continue;
                var relative = entry.FullName.Replace('\\', '/');
                if (relative == ".." || relative.StartsWith("../") || Path.IsPathRooted(relative)) return false;
                var path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    return false;
                archived.Add(relative);
                using var input = entry.Open();
                using var original = File.OpenRead(path);
                if (entry.Length != original.Length) return false;
                using var sha = SHA256.Create();
                var expected = sha.ComputeHash(input);
                original.Position = 0;
                if (!expected.SequenceEqual(sha.ComputeHash(original))) return false;
            }
            foreach (var file in ExtensionSyncIgnorePolicy.EnumerateIncludedFiles(root))
                if (ExtensionPackageService.ShouldIncludeInPackage(root, file) &&
                    !archived.Contains(Path.GetRelativePath(root, file).Replace('\\', '/')))
                    return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsClean(string directory) =>
        HasGitWorktree(directory) && RunGit(directory, "status", "--porcelain", "--untracked-files=all") == "";

    private static bool IsCommit(string? candidate) =>
        candidate is { Length: 40 or 64 } && candidate.All(Uri.IsHexDigit);

    private static bool GitSucceeds(string directory, params string[] arguments) =>
        InvokeGit(directory, arguments).ExitCode == 0;

    private static string? RunGit(string directory, params string[] arguments)
    {
        var result = InvokeGit(directory, arguments);
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    private static (int ExitCode, string Output) InvokeGit(string directory, string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("git")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            foreach (var arg in arguments) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process == null) return (-1, "");
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(25000))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "");
            }
            return (process.ExitCode, output);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, "");
        }
    }
}