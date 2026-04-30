using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

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
        Loaded += (_, _) => CloseButton.Focus();
    }

    private void CopyOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CopyTextToClipboard(_copyText);
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

    private static void CopyTextToClipboard(string text)
    {
        if (TryCopyViaStaClipboard(text, out var staError))
        {
            return;
        }

        if (TryCopyViaClipExe(text, out var fallbackError))
        {
            return;
        }

        throw new InvalidOperationException($"复制到剪贴板失败：{fallbackError ?? staError}");
    }

    private static bool TryCopyViaStaClipboard(string text, out string? error)
    {
        error = null;
        Exception? threadError = null;
        var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText);
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
            finally
            {
                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        done.Wait(TimeSpan.FromSeconds(5));

        if (!done.IsSet)
        {
            error = "STA 剪贴板线程超时。";
            return false;
        }

        if (threadError == null)
        {
            return true;
        }

        error = threadError.Message;
        return false;
    }

    private static bool TryCopyViaClipExe(string text, out string? error)
    {
        error = null;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = Encoding.Unicode,
                CreateNoWindow = true
            };

            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                return true;
            }

            error = $"clip.exe 返回了退出码 {process.ExitCode}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
