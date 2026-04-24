using System.Text;
using System.Windows;

namespace OpenQuickHost;

public partial class ExecutionLogWindow : Window
{
    private readonly string _copyText;

    public ExecutionLogWindow(
        string title,
        bool success,
        string output,
        string error,
        int? exitCode = null,
        string? extraMeta = null)
    {
        InitializeComponent();
        Title = $"{title} 运行日志";
        TitleText.Text = $"{title} 运行日志";
        StatusText.Text = success ? "执行完成" : "执行失败";
        StatusText.Foreground = success
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.IndianRed;
        MetaText.Text = BuildMetaText(success, exitCode, extraMeta);
        OutputTextBox.Text = string.IsNullOrWhiteSpace(output) ? "无输出。" : output.Trim();
        ErrorTextBox.Text = string.IsNullOrWhiteSpace(error) ? "无错误输出。" : error.Trim();
        _copyText = BuildCopyText(title, success, output, error, exitCode, extraMeta);
    }

    private void CopyOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_copyText);
            HintText.Text = "已复制运行日志。";
        }
        catch (Exception ex)
        {
            HintText.Text = $"复制失败：{ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string BuildMetaText(bool success, int? exitCode, string? extraMeta)
    {
        var builder = new StringBuilder();
        builder.Append(success ? "状态：成功" : "状态：失败");
        if (exitCode.HasValue)
        {
            builder.Append("    退出码：");
            builder.Append(exitCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(extraMeta))
        {
            builder.AppendLine();
            builder.Append(extraMeta.Trim());
        }

        return builder.ToString();
    }

    private static string BuildCopyText(string title, bool success, string output, string error, int? exitCode, string? extraMeta)
    {
        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine(success ? "状态：成功" : "状态：失败");
        if (exitCode.HasValue)
        {
            builder.AppendLine($"退出码：{exitCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(extraMeta))
        {
            builder.AppendLine(extraMeta.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("标准输出：");
        builder.AppendLine(string.IsNullOrWhiteSpace(output) ? "无输出。" : output.Trim());
        builder.AppendLine();
        builder.AppendLine("错误输出：");
        builder.AppendLine(string.IsNullOrWhiteSpace(error) ? "无错误输出。" : error.Trim());
        return builder.ToString();
    }
}
