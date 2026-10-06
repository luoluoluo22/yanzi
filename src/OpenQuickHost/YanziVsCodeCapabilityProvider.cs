using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziVsCodeCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "vscode.status",
            Description = "查询 Visual Studio Code 是否安装、版本、运行状态和真实 CLI 入口",
            Permissions = ["application.read"],
            Category = "vscode",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "vscode.open",
            Description = "用 VS Code 打开文件、目录或 workspace，可跳到指定行列，并选择复用或新建窗口",
            Permissions = ["application.run", "file.read"],
            Category = "vscode",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "line":{"type":"integer","minimum":1},
                "column":{"type":"integer","minimum":1},
                "newWindow":{"type":"boolean"},
                "reuseWindow":{"type":"boolean"}
              },
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };

        yield return new()
        {
            Name = "vscode.diff",
            Description = "在 VS Code 中比较两个文件",
            Permissions = ["application.run", "file.read"],
            Category = "vscode",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "left":{"type":"string","minLength":1},
                "right":{"type":"string","minLength":1}
              },
              "required":["left","right"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = DiffAsync
        };

        yield return new()
        {
            Name = "vscode.extensions.list",
            Description = "列出 VS Code 已安装扩展及版本",
            Permissions = ["application.read"],
            Category = "vscode",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ListExtensionsAsync
        };

        yield return new()
        {
            Name = "vscode.extensions.install",
            Description = "通过 VS Code 官方 CLI 安装或更新指定扩展",
            Permissions = ["application.run", "network.write"],
            Category = "vscode",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "extension":{"type":"string","minLength":1},
                "force":{"type":"boolean"}
              },
              "required":["extension"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = InstallExtensionAsync
        };
    }

    private static async Task<object?> StatusAsync(object? _)
    {
        var cli = ResolveCli();
        if (cli == null)
        {
            return new
            {
                installed = false,
                running = Process.GetProcessesByName("Code").Length > 0
            };
        }

        var result = await RunCliAsync(cli, ["--version"], 15);
        var lines = SplitLines(result.Stdout);
        return new
        {
            installed = true,
            running = Process.GetProcessesByName("Code").Length > 0,
            executable = cli.Executable,
            cliScript = cli.CliScript,
            version = lines.ElementAtOrDefault(0),
            commit = lines.ElementAtOrDefault(1),
            architecture = lines.ElementAtOrDefault(2),
            exitCode = result.ExitCode
        };
    }

    private static async Task<object?> OpenAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = NormalizeExistingPath(input.GetProperty("path").GetString()!);
        var line = input.TryGetProperty("line", out var lineElement) ? lineElement.GetInt32() : (int?)null;
        var column = input.TryGetProperty("column", out var columnElement) ? columnElement.GetInt32() : (int?)null;
        var newWindow = input.TryGetProperty("newWindow", out var nw) && nw.GetBoolean();
        var reuseWindow = !newWindow && (!input.TryGetProperty("reuseWindow", out var rw) || rw.GetBoolean());

        if (Directory.Exists(path) && (line.HasValue || column.HasValue))
            throw new ArgumentException("目录不能指定行列。");

        var args = new List<string>();
        if (newWindow) args.Add("--new-window");
        else if (reuseWindow) args.Add("--reuse-window");

        if (line.HasValue)
        {
            args.Add("--goto");
            args.Add($"{path}:{line.Value}:{column ?? 1}");
        }
        else
        {
            args.Add(path);
        }

        var cli = ResolveCli() ?? throw new FileNotFoundException("未找到 Visual Studio Code CLI。");
        var result = await RunCliAsync(cli, args, 20);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("VS Code 打开失败：" + result.Stderr);

        return new
        {
            opened = true,
            path,
            line,
            column,
            window = newWindow ? "new" : reuseWindow ? "reuse" : "default",
            exitCode = result.ExitCode
        };
    }

    private static async Task<object?> DiffAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var left = NormalizeExistingFile(input.GetProperty("left").GetString()!);
        var right = NormalizeExistingFile(input.GetProperty("right").GetString()!);
        var cli = ResolveCli() ?? throw new FileNotFoundException("未找到 Visual Studio Code CLI。");

        var result = await RunCliAsync(cli, ["--reuse-window", "--diff", left, right], 20);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("VS Code diff 失败：" + result.Stderr);

        return new { opened = true, left, right, exitCode = result.ExitCode };
    }

    private static async Task<object?> ListExtensionsAsync(object? _)
    {
        var cli = ResolveCli() ?? throw new FileNotFoundException("未找到 Visual Studio Code CLI。");
        var result = await RunCliAsync(cli, ["--list-extensions", "--show-versions"], 20);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("读取 VS Code 扩展失败：" + result.Stderr);

        var items = SplitLines(result.Stdout)
            .Select(line =>
            {
                var at = line.LastIndexOf('@');
                return at > 0
                    ? new { id = line[..at], version = line[(at + 1)..] }
                    : new { id = line, version = "" };
            })
            .ToArray();

        return new { count = items.Length, items };
    }

    private static async Task<object?> InstallExtensionAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var extension = input.GetProperty("extension").GetString()!.Trim();
        var force = input.TryGetProperty("force", out var forceElement) && forceElement.GetBoolean();
        if (extension.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("扩展标识无效。");

        var args = new List<string> { "--install-extension", extension };
        if (force) args.Add("--force");

        var cli = ResolveCli() ?? throw new FileNotFoundException("未找到 Visual Studio Code CLI。");
        var result = await RunCliAsync(cli, args, 120);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"VS Code 扩展安装失败：{result.Stderr}\n{result.Stdout}");

        return new
        {
            installed = true,
            extension,
            force,
            exitCode = result.ExitCode,
            output = result.Stdout.Trim()
        };
    }

    private sealed record CliLocation(string Executable, string CliScript);
    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    private static CliLocation? ResolveCli()
    {
        var candidateRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code")
        };

        foreach (var root in candidateRoots.Where(Directory.Exists))
        {
            var executable = Path.Combine(root, "Code.exe");
            if (!File.Exists(executable)) continue;

            string? cliScript = null;
            try
            {
                cliScript = Directory.EnumerateDirectories(root)
                    .Where(path => Path.GetFileName(path).Length >= 8)
                    .Select(path => Path.Combine(path, "resources", "app", "out", "cli.js"))
                    .Where(File.Exists)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .FirstOrDefault();
            }
            catch
            {
            }

            if (cliScript != null)
                return new CliLocation(executable, cliScript);
        }

        return null;
    }

    private static async Task<CliResult> RunCliAsync(CliLocation cli, IEnumerable<string> args, int timeoutSeconds)
    {
        var start = new ProcessStartInfo
        {
            FileName = cli.Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        start.ArgumentList.Add(cli.CliScript);
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动 VS Code CLI。");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"VS Code CLI 超过 {timeoutSeconds} 秒未完成。");
        }

        return new CliResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string NormalizeExistingPath(string raw)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (!File.Exists(full) && !Directory.Exists(full))
            throw new FileNotFoundException("路径不存在。", full);
        return full;
    }

    private static string NormalizeExistingFile(string raw)
    {
        var full = NormalizeExistingPath(raw);
        if (!File.Exists(full))
            throw new ArgumentException("该能力需要文件路径：" + full);
        return full;
    }

    private static string[] SplitLines(string text)
        => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
