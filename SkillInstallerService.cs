using System.IO;

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
            throw new DirectoryNotFoundException("没有找到程序内置的 skills 文件夹。");
        }

        var exportPath = GetExportPath(destinationRoot, target, scope);
        ReplaceDirectory(sourceSkillsRoot, exportPath);

        return new SkillExportResult(
            exportPath,
            CountSkills(exportPath),
            target,
            scope,
            GetDisplayRelativePath(target, scope));
    }

    public static string GetExportPath(string? destinationRoot, SkillExportTarget target, SkillExportScope scope)
    {
        if (scope == SkillExportScope.Global)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return target switch
            {
                SkillExportTarget.Codex => Path.Combine(userProfile, ".codex", "skills"),
                SkillExportTarget.Antigravity => Path.Combine(userProfile, ".gemini", "antigravity", "skills"),
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
            SkillExportTarget.Antigravity => Path.Combine(destinationRoot, ".agents", "skills"),
            SkillExportTarget.Trae => Path.Combine(destinationRoot, ".trae", "skills"),
            _ => throw new InvalidOperationException("不支持的导出目标。")
        };
    }

    public static string GetDisplayRelativePath(SkillExportTarget target, SkillExportScope scope)
    {
        return scope switch
        {
            SkillExportScope.Project when target == SkillExportTarget.Codex => Path.Combine(".codex", "skills"),
            SkillExportScope.Project when target == SkillExportTarget.Antigravity => Path.Combine(".agents", "skills"),
            SkillExportScope.Project when target == SkillExportTarget.Trae => Path.Combine(".trae", "skills"),
            SkillExportScope.Global when target == SkillExportTarget.Codex => Path.Combine(".codex", "skills"),
            SkillExportScope.Global when target == SkillExportTarget.Antigravity => Path.Combine(".gemini", "antigravity", "skills"),
            SkillExportScope.Global when target == SkillExportTarget.Trae => Path.Combine(".trae", "skills"),
            _ => throw new InvalidOperationException("不支持的导出目标。")
        };
    }

    private static int CountSkills(string root)
    {
        return Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories).Count();
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
        }

        CopyDirectory(source, destination);
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
            File.Copy(file, targetFile, true);
        }
    }
}

public sealed record SkillExportResult(
    string ExportedPath,
    int SkillCount,
    SkillExportTarget Target,
    SkillExportScope Scope,
    string RelativePath);

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
