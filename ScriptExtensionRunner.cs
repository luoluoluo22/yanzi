using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

public static class ScriptExtensionRunner
{
    public static bool CanExecute(CommandItem command)
    {
        if (string.IsNullOrWhiteSpace(command.Runtime) || string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return false;
        }

        if (string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(command.InlineScriptSource);
        }

        return !string.IsNullOrWhiteSpace(command.EntryPoint);
    }

    public static async Task<ScriptExecutionResult> ExecuteAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(command))
        {
            return new ScriptExecutionResult(false, string.Empty, "扩展没有可执行脚本入口。", -1);
        }

        var isInline = string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase);
        var entryPath = isInline
            ? await MaterializeInlineScriptAsync(command, cancellationToken)
            : Path.Combine(command.ExtensionDirectoryPath!, command.EntryPoint!);
        if (!File.Exists(entryPath))
        {
            return new ScriptExecutionResult(false, string.Empty, $"没有找到脚本入口：{entryPath}", -1);
        }

        try
        {
            switch (command.Runtime?.ToLowerInvariant())
            {
                case "powershell":
                case "ps1":
                    return await ExecutePowerShellAsync(command, entryPath, inputText, launchSource, cancellationToken);
                default:
                    return new ScriptExecutionResult(false, string.Empty, $"当前还不支持脚本运行时：{command.Runtime}", -1);
            }
        }
        finally
        {
            if (isInline)
            {
                TryDeleteTempScript(entryPath);
            }
        }
    }

    private static async Task<string> MaterializeInlineScriptAsync(CommandItem command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            throw new InvalidOperationException("内联脚本缺少扩展目录。");
        }

        if (string.IsNullOrWhiteSpace(command.InlineScriptSource))
        {
            throw new InvalidOperationException("内联脚本缺少 script.source。");
        }

        Directory.CreateDirectory(command.ExtensionDirectoryPath);
        var tempScriptPath = Path.Combine(command.ExtensionDirectoryPath, $".yanzi-inline-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(
            tempScriptPath,
            command.InlineScriptSource,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        return tempScriptPath;
    }

    private static async Task<ScriptExecutionResult> ExecutePowerShellAsync(
        CommandItem command,
        string entryPath,
        string? inputText,
        string launchSource,
        CancellationToken cancellationToken)
    {
        var context = new ScriptExecutionContext(
            command.ExtensionId,
            command.Title,
            command.ExtensionDirectoryPath!,
            inputText ?? string.Empty,
            launchSource,
            DateTimeOffset.Now,
            command.Permissions);
        var contextPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                contextPath,
                JsonSerializer.Serialize(context, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -File {Quote(entryPath)} -InputText {Quote(inputText ?? string.Empty)} -ContextPath {Quote(contextPath)}",
                WorkingDirectory = command.ExtensionDirectoryPath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.Environment["YANZI_INPUT"] = inputText ?? string.Empty;
            startInfo.Environment["YANZI_CONTEXT_PATH"] = contextPath;
            startInfo.Environment["YANZI_EXTENSION_ID"] = command.ExtensionId;
            startInfo.Environment["YANZI_EXTENSION_DIR"] = command.ExtensionDirectoryPath!;
            startInfo.Environment["YANZI_LAUNCH_SOURCE"] = launchSource;

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            return process.ExitCode == 0
                ? new ScriptExecutionResult(true, output, error, process.ExitCode)
                : new ScriptExecutionResult(false, output, string.IsNullOrWhiteSpace(error) ? $"脚本退出码：{process.ExitCode}" : error, process.ExitCode);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            try
            {
                if (File.Exists(contextPath))
                {
                    File.Delete(contextPath);
                }
            }
            catch
            {
                // Ignore temp cleanup failures.
            }
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void TryDeleteTempScript(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    private sealed record ScriptExecutionContext(
        string ExtensionId,
        string Title,
        string ExtensionDirectory,
        string InputText,
        string LaunchSource,
        DateTimeOffset Now,
        IReadOnlyList<string> Permissions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}

public sealed record ScriptExecutionResult(bool Success, string Output, string Error, int ExitCode);
