using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
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
        switch (command.Runtime?.ToLowerInvariant())
        {
            case "powershell":
            case "ps1":
            {
                var entryPath = isInline
                    ? await MaterializeInlineScriptAsync(command, ".ps1", cancellationToken)
                    : Path.Combine(command.ExtensionDirectoryPath!, command.EntryPoint!);
                if (!File.Exists(entryPath))
                {
                    return new ScriptExecutionResult(false, string.Empty, $"没有找到脚本入口：{entryPath}", -1);
                }

                try
                {
                    return await ExecutePowerShellAsync(command, entryPath, inputText, launchSource, cancellationToken);
                }
                finally
                {
                    if (isInline)
                    {
                        TryDeleteTempFile(entryPath);
                    }
                }
            }

            case "csharp":
            case "cs":
            case "c#":
            {
                var source = isInline
                    ? command.InlineScriptSource
                    : await ReadEntrySourceAsync(command, cancellationToken);
                return string.IsNullOrWhiteSpace(source)
                    ? new ScriptExecutionResult(false, string.Empty, "C# 扩展缺少源码入口。", -1)
                    : await ExecuteCSharpAsync(command, source, inputText, launchSource, cancellationToken);
            }

            default:
                return new ScriptExecutionResult(false, string.Empty, $"当前还不支持脚本运行时：{command.Runtime}", -1);
        }
    }

    private static async Task<string> MaterializeInlineScriptAsync(CommandItem command, string extension, CancellationToken cancellationToken)
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
        var tempScriptPath = Path.Combine(command.ExtensionDirectoryPath, $".yanzi-inline-{Guid.NewGuid():N}{extension}");
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
        var context = CreateContext(command, inputText, launchSource);
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
            ApplyRuntimeEnvironment(startInfo, command, inputText, contextPath, launchSource);

            return await RunProcessAsync(startInfo, "脚本", cancellationToken);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            TryDeleteTempFile(contextPath);
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteCSharpAsync(
        CommandItem command,
        string source,
        string? inputText,
        string launchSource,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(command, inputText, launchSource);
        var contextPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                contextPath,
                JsonSerializer.Serialize(context, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            var build = await EnsureCSharpBuildAsync(command, source, cancellationToken);
            if (!build.Success)
            {
                return build;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = Quote(build.Output.Trim()),
                WorkingDirectory = command.ExtensionDirectoryPath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ApplyRuntimeEnvironment(startInfo, command, inputText, contextPath, launchSource);

            return await RunProcessAsync(startInfo, "C# 扩展", cancellationToken);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            TryDeleteTempFile(contextPath);
        }
    }

    private static async Task<ScriptExecutionResult> EnsureCSharpBuildAsync(
        CommandItem command,
        string source,
        CancellationToken cancellationToken)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16].ToLowerInvariant();
        var buildRoot = Path.Combine(command.ExtensionDirectoryPath!, ".yanzi-csharp-cache", sourceHash);
        var projectPath = Path.Combine(buildRoot, "YanziExtension.csproj");
        var dllPath = Path.Combine(buildRoot, "bin", "Release", "net9.0", "YanziExtension.dll");
        if (File.Exists(dllPath))
        {
            return new ScriptExecutionResult(true, dllPath, string.Empty, 0);
        }

        Directory.CreateDirectory(buildRoot);
        await File.WriteAllTextAsync(projectPath, CSharpProjectSource, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "Action.cs"), source, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "Program.cs"), CSharpProgramSource, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "YanziRuntime.cs"), CSharpRuntimeSource, Encoding.UTF8, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build {Quote(projectPath)} -c Release -nologo",
            WorkingDirectory = buildRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var result = await RunProcessAsync(startInfo, "C# 扩展编译", cancellationToken);
        return result.Success && File.Exists(dllPath)
            ? new ScriptExecutionResult(true, dllPath, string.Empty, 0)
            : result;
    }

    private static async Task<string?> ReadEntrySourceAsync(CommandItem command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.EntryPoint) || string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return null;
        }

        var entryPath = Path.Combine(command.ExtensionDirectoryPath, command.EntryPoint);
        return File.Exists(entryPath)
            ? await File.ReadAllTextAsync(entryPath, Encoding.UTF8, cancellationToken)
            : null;
    }

    private static async Task<ScriptExecutionResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string label,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        return process.ExitCode == 0
            ? new ScriptExecutionResult(true, output, error, process.ExitCode)
            : new ScriptExecutionResult(false, output, string.IsNullOrWhiteSpace(error) ? $"{label}退出码：{process.ExitCode}" : error, process.ExitCode);
    }

    private static ScriptExecutionContext CreateContext(CommandItem command, string? inputText, string launchSource)
    {
        return new ScriptExecutionContext(
            command.ExtensionId,
            command.Title,
            command.ExtensionDirectoryPath!,
            inputText ?? string.Empty,
            launchSource,
            DateTimeOffset.Now,
            command.Permissions);
    }

    private static void ApplyRuntimeEnvironment(
        ProcessStartInfo startInfo,
        CommandItem command,
        string? inputText,
        string contextPath,
        string launchSource)
    {
        startInfo.Environment["YANZI_INPUT"] = inputText ?? string.Empty;
        startInfo.Environment["YANZI_CONTEXT_PATH"] = contextPath;
        startInfo.Environment["YANZI_EXTENSION_ID"] = command.ExtensionId;
        startInfo.Environment["YANZI_EXTENSION_DIR"] = command.ExtensionDirectoryPath!;
        startInfo.Environment["YANZI_LAUNCH_SOURCE"] = launchSource;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void TryDeleteTempFile(string path)
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

    private const string CSharpProjectSource =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net9.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>
        """;

    private const string CSharpProgramSource =
        """
        using OpenQuickHost.CSharpRuntime;

        var context = await YanziActionContext.LoadFromEnvironmentAsync();
        var result = await YanziAction.RunAsync(context);
        if (!string.IsNullOrEmpty(result))
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(result);
        }
        """;

    private const string CSharpRuntimeSource =
        """
        using System.Text.Json;

        namespace OpenQuickHost.CSharpRuntime;

        public sealed record YanziActionContext(
            string ExtensionId,
            string Title,
            string ExtensionDirectory,
            string InputText,
            string LaunchSource,
            DateTimeOffset Now,
            IReadOnlyList<string> Permissions)
        {
            public static async Task<YanziActionContext> LoadFromEnvironmentAsync()
            {
                var contextPath = Environment.GetEnvironmentVariable("YANZI_CONTEXT_PATH");
                if (string.IsNullOrWhiteSpace(contextPath) || !File.Exists(contextPath))
                {
                    throw new InvalidOperationException("YANZI_CONTEXT_PATH is missing.");
                }

                var json = await File.ReadAllTextAsync(contextPath);
                return JsonSerializer.Deserialize<YanziActionContext>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidOperationException("Failed to read Yanzi context.");
            }
        }
        """;
}

public sealed record ScriptExecutionResult(bool Success, string Output, string Error, int ExitCode);
