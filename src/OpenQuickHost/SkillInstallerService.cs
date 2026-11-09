using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuickHost;

public static class SkillInstallerService
{
    public static SkillExportResult ExportSkills(
        string sourceSkillsRoot,
        string? destinationRoot,
        SkillExportTarget target,
        SkillExportScope scope)
    {
        if (string.IsNullOrWhiteSpace(sourceSkillsRoot) || !Directory.Exists(sourceSkillsRoot))
        {
            throw new DirectoryNotFoundException("没有找到当前运行版本内置的 skills 文件夹。");
        }

        var sourceRoot = Path.GetFullPath(sourceSkillsRoot);
        var skillDirectories = Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => File.Exists(Path.Combine(path, "SKILL.md")))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (skillDirectories.Length == 0)
        {
            throw new InvalidDataException("内置 skills 目录中没有包含 SKILL.md 的技能包。");
        }

        // Validate the metadata before touching the destination. Antigravity indexes these fields first.
        _ = skillDirectories.Select(ReadSkillContent).ToArray();
        var sourceManifest = CreateFileManifest(skillDirectories);
        if (sourceManifest.Count == 0)
        {
            throw new InvalidDataException("内置 Skill 没有可导出的文件。");
        }

        var exportPath = GetExportPath(destinationRoot, target, scope);
        Directory.CreateDirectory(exportPath);

        var transactionId = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(exportPath, $".yanzi-skill-stage-{transactionId}");
        var backupRoot = Path.Combine(exportPath, $".yanzi-skill-backup-{transactionId}");
        var installedSkillNames = new List<string>();
        var backedUpSkillNames = new List<string>();
        var committed = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);

            foreach (var sourceSkillDirectory in skillDirectories)
            {
                var skillName = GetSafeSkillDirectoryName(sourceSkillDirectory);
                CopyDirectory(sourceSkillDirectory, Path.Combine(stagingRoot, skillName));
            }

            var stagedDirectories = skillDirectories
                .Select(path => Path.Combine(stagingRoot, GetSafeSkillDirectoryName(path)))
                .ToArray();
            var stagedManifest = CreateFileManifest(stagedDirectories);
            EnsureManifestsMatch(sourceManifest, stagedManifest, "临时副本");

            // Replace only matching Skill folders. Other user-installed global Skills remain untouched.
            foreach (var sourceSkillDirectory in skillDirectories)
            {
                var skillName = GetSafeSkillDirectoryName(sourceSkillDirectory);
                var destinationSkillPath = Path.Combine(exportPath, skillName);

                if (File.Exists(destinationSkillPath))
                {
                    throw new IOException($"目标位置存在同名文件，无法导出 Skill：{destinationSkillPath}");
                }

                if (Directory.Exists(destinationSkillPath))
                {
                    Directory.CreateDirectory(backupRoot);
                    Directory.Move(destinationSkillPath, Path.Combine(backupRoot, skillName));
                    backedUpSkillNames.Add(skillName);
                }

                Directory.Move(Path.Combine(stagingRoot, skillName), destinationSkillPath);
                installedSkillNames.Add(skillName);
            }

            var exportedDirectories = skillDirectories
                .Select(path => Path.Combine(exportPath, GetSafeSkillDirectoryName(path)))
                .ToArray();
            var exportedManifest = CreateFileManifest(exportedDirectories);
            EnsureManifestsMatch(sourceManifest, exportedManifest, "最终导出目录");

            var exportedSkills = exportedDirectories.Select(ReadSkillContent).ToArray();
            var fileVerifications = exportedManifest
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new SkillFileVerification(entry.Key, entry.Value))
                .ToArray();

            committed = true;
            return new SkillExportResult(
                exportPath,
                sourceRoot,
                skillDirectories.Length,
                sourceManifest.Count,
                exportedManifest.Count,
                true,
                target,
                scope,
                GetDisplayRelativePath(target, scope),
                exportedSkills,
                fileVerifications);
        }
        catch (Exception exportException)
        {
            Exception? rollbackException = null;

            foreach (var skillName in installedSkillNames.AsEnumerable().Reverse())
            {
                try
                {
                    var destinationSkillPath = Path.Combine(exportPath, skillName);
                    if (Directory.Exists(destinationSkillPath))
                    {
                        Directory.Delete(destinationSkillPath, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    rollbackException ??= ex;
                }
            }

            foreach (var skillName in backedUpSkillNames.AsEnumerable().Reverse())
            {
                try
                {
                    var backupSkillPath = Path.Combine(backupRoot, skillName);
                    var destinationSkillPath = Path.Combine(exportPath, skillName);
                    if (Directory.Exists(backupSkillPath) && !Directory.Exists(destinationSkillPath))
                    {
                        Directory.Move(backupSkillPath, destinationSkillPath);
                    }
                }
                catch (Exception ex)
                {
                    rollbackException ??= ex;
                }
            }

            if (rollbackException is not null)
            {
                throw new IOException(
                    $"Skill 导出失败，且自动回滚未能完全完成。请保留备份目录：{backupRoot}",
                    new AggregateException(exportException, rollbackException));
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);

            // Keep a non-empty backup folder if rollback itself failed, so the previous Skills remain recoverable.
            if (committed ||
                !Directory.Exists(backupRoot) ||
                !Directory.EnumerateFileSystemEntries(backupRoot).Any())
            {
                TryDeleteDirectory(backupRoot);
            }
        }
    }

    public static string GetExportPath(string? destinationRoot, SkillExportTarget target, SkillExportScope scope)
    {
        if (scope == SkillExportScope.Global)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
            {
                throw new InvalidOperationException("无法确定当前用户目录，不能导出全局 Skill。");
            }

            return target switch
            {
                SkillExportTarget.Codex => Path.Combine(userProfile, ".codex", "skills"),
                SkillExportTarget.Antigravity => Path.Combine(userProfile, ".gemini", "config", "skills"),
                SkillExportTarget.Trae => Path.Combine(userProfile, ".trae", "skills"),
                _ => throw new InvalidOperationException("不支持的导出目标。")
            };
        }

        if (string.IsNullOrWhiteSpace(destinationRoot) || !Directory.Exists(destinationRoot))
        {
            throw new DirectoryNotFoundException("没有找到项目根目录。");
        }

        return target switch
        {
            SkillExportTarget.Codex => Path.Combine(destinationRoot, ".codex", "skills"),
            SkillExportTarget.Antigravity => Path.Combine(destinationRoot, ".agent", "skills"),
            SkillExportTarget.Trae => Path.Combine(destinationRoot, ".trae", "skills"),
            _ => throw new InvalidOperationException("不支持的导出目标。")
        };
    }

    public static string GetDisplayRelativePath(SkillExportTarget target, SkillExportScope scope)
    {
        return scope switch
        {
            SkillExportScope.Project when target == SkillExportTarget.Codex => Path.Combine(".codex", "skills"),
            SkillExportScope.Project when target == SkillExportTarget.Antigravity => Path.Combine(".agent", "skills"),
            SkillExportScope.Project when target == SkillExportTarget.Trae => Path.Combine(".trae", "skills"),
            SkillExportScope.Global when target == SkillExportTarget.Codex => Path.Combine(".codex", "skills"),
            SkillExportScope.Global when target == SkillExportTarget.Antigravity => Path.Combine(".gemini", "config", "skills"),
            SkillExportScope.Global when target == SkillExportTarget.Trae => Path.Combine(".trae", "skills"),
            _ => throw new InvalidOperationException("不支持的导出目标。")
        };
    }

    private static string GetSafeSkillDirectoryName(string skillDirectory)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(skillDirectory));
        if (string.IsNullOrWhiteSpace(name) ||
            name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"Skill 目录名称无效：{skillDirectory}");
        }

        return name;
    }

    private static SortedDictionary<string, string> CreateFileManifest(IEnumerable<string> skillDirectories)
    {
        var manifest = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skillDirectory in skillDirectories)
        {
            var skillName = GetSafeSkillDirectoryName(skillDirectory);
            foreach (var file in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                var relativeFilePath = Path.GetRelativePath(skillDirectory, file).Replace('\\', '/');
                var relativePath = $"{skillName}/{relativeFilePath}";
                using var stream = File.OpenRead(file);
                var sha256 = Convert.ToHexString(SHA256.HashData(stream));
                manifest.Add(relativePath, sha256);
            }
        }

        return manifest;
    }

    private static void EnsureManifestsMatch(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        string stage)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidDataException(
                $"{stage}文件数量校验失败：源文件 {expected.Count} 个，目标文件 {actual.Count} 个。");
        }

        foreach (var expectedEntry in expected)
        {
            if (!actual.TryGetValue(expectedEntry.Key, out var actualHash))
            {
                throw new InvalidDataException($"{stage}缺少文件：{expectedEntry.Key}");
            }

            if (!string.Equals(expectedEntry.Value, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{stage} SHA-256 校验失败：{expectedEntry.Key}");
            }
        }
    }

    private static SkillExportedContent ReadSkillContent(string skillDirectory)
    {
        var skillName = GetSafeSkillDirectoryName(skillDirectory);
        var skillMarkdownPath = Path.Combine(skillDirectory, "SKILL.md");
        var markdownContent = File.ReadAllText(skillMarkdownPath, Encoding.UTF8);
        var (name, description) = ParseSkillMetadata(markdownContent, skillMarkdownPath);

        return new SkillExportedContent(
            name,
            description,
            $"{skillName}/SKILL.md",
            markdownContent);
    }

    private static (string Name, string Description) ParseSkillMetadata(string markdownContent, string skillMarkdownPath)
    {
        var lines = markdownContent.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 3 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Skill 缺少 YAML frontmatter：{skillMarkdownPath}");
        }

        var frontmatterEnd = Array.FindIndex(lines, 1, line =>
            string.Equals(line.Trim(), "---", StringComparison.Ordinal));
        if (frontmatterEnd < 0)
        {
            throw new InvalidDataException($"Skill 的 YAML frontmatter 没有结束标记：{skillMarkdownPath}");
        }

        string? name = null;
        string? description = null;

        for (var index = 1; index < frontmatterEnd; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
            {
                name = UnquoteYamlScalar(value);
            }
            else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
            {
                if (value is "|" or "|-" or "|+" or ">" or ">-" or ">+")
                {
                    var descriptionLines = new List<string>();
                    for (var nestedIndex = index + 1; nestedIndex < frontmatterEnd; nestedIndex++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[nestedIndex]))
                        {
                            continue;
                        }

                        if (!char.IsWhiteSpace(lines[nestedIndex][0]))
                        {
                            break;
                        }

                        descriptionLines.Add(lines[nestedIndex].Trim());
                        index = nestedIndex;
                    }

                    description = string.Join(" ", descriptionLines).Trim();
                }
                else
                {
                    description = UnquoteYamlScalar(value);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidDataException(
                $"Skill 的 YAML frontmatter 必须同时包含非空 name 和 description：{skillMarkdownPath}");
        }

        return (name, description);
    }

    private static string UnquoteYamlScalar(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1].Trim();
        }

        return value.Trim();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // A cleanup failure must not hide the original export or rollback result.
        }
    }
}

public sealed record SkillExportResult(
    string ExportedPath,
    string SourcePath,
    int SkillCount,
    int FileCount,
    int VerifiedFileCount,
    bool Sha256Verified,
    SkillExportTarget Target,
    SkillExportScope Scope,
    string RelativePath,
    IReadOnlyList<SkillExportedContent> Skills,
    IReadOnlyList<SkillFileVerification> FileVerifications);

public sealed record SkillExportedContent(
    string Name,
    string Description,
    string RelativePath,
    string MarkdownContent);

public sealed record SkillFileVerification(
    string RelativePath,
    string Sha256);

public enum SkillExportTarget
{
    Codex,
    Antigravity,
    Trae
}

public enum SkillExportScope
{
    Project,
    Global
}