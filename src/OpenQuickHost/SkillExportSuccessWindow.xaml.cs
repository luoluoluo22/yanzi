using System.Diagnostics;
using System.Text;
using System.Windows;

namespace OpenQuickHost;

public partial class SkillExportSuccessWindow : Window
{
    private readonly SkillExportResult _result;

    public SkillExportSuccessWindow(SkillExportResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        InitializeComponent();

        var theme = AppSettingsStore.Load().ThemeMode;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(this,
            string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light
                : Yanzi.UI.Wpf.YanziTheme.Dark);

        ExportPathBox.Text = result.ExportedPath;
        VerificationSummaryText.Text =
            $"已从当前运行版本的内置技能包导出 {result.SkillCount} 个 Skill、{result.FileCount} 个文件；" +
            $"SHA-256 逐文件校验 {result.VerifiedFileCount}/{result.FileCount} 全部通过。";

        FileVerificationBox.Text = string.Join(
            Environment.NewLine,
            result.FileVerifications.Select(file => $"{file.RelativePath}    SHA-256: {file.Sha256}"));

        SkillContentBox.Text = BuildAiReadableContent(result.Skills);
    }

    private static string BuildAiReadableContent(IReadOnlyList<SkillExportedContent> skills)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 已导出的 Agent Skills");
        builder.AppendLine();
        builder.AppendLine("以下内容包含每个 Skill 的元数据和完整 SKILL.md 原文。");
        builder.AppendLine();

        foreach (var skill in skills)
        {
            builder.AppendLine($"===== Skill: {skill.Name} =====");
            builder.AppendLine($"description: {skill.Description}");
            builder.AppendLine($"path: {skill.RelativePath}");
            builder.AppendLine();
            builder.AppendLine(skill.MarkdownContent.TrimEnd());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_result.ExportedPath);
            CopyStatusText.Text = "已复制导出路径。";
        }
        catch (Exception ex)
        {
            CopyStatusText.Text = $"复制路径失败：{ex.Message}";
        }
    }

    private void CopySkillContentButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(SkillContentBox.Text);
            CopyStatusText.Text = "已复制完整 Skill 内容，可以直接粘贴给 AI。";
        }
        catch (Exception ex)
        {
            CopyStatusText.Text = $"复制 Skill 内容失败：{ex.Message}";
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_result.ExportedPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            CopyStatusText.Text = $"打开导出目录失败：{ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
