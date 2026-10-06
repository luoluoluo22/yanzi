using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziUnityCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        foreach (var prefix in new[] { "unity", "tuanjie" })
        {
            var display = prefix == "unity" ? "Unity" : "团结引擎";

            yield return new()
            {
                Name = $"{prefix}.status",
                Description = $"查询 {display} Editor 安装、版本和运行状态",
                Permissions = ["application.read"],
                Category = prefix,
                InputSchema = YanziCapabilitySchema.EmptyObject,
                OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
                Handler = _ => StatusAsync(prefix)
            };

            yield return new()
            {
                Name = $"{prefix}.openProject",
                Description = $"用 {display} Editor 打开已存在的项目目录",
                Permissions = ["application.run", "file.read"],
                Category = prefix,
                InputSchema = YanziCapabilitySchema.Parse("""
                {
                  "type":"object",
                  "properties":{"projectPath":{"type":"string","minLength":1}},
                  "required":["projectPath"],
                  "additionalProperties":false
                }
                """),
                OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
                Handler = payload => OpenProjectAsync(prefix, payload)
            };

            yield return new()
            {
                Name = $"{prefix}.batch.executeMethod",
                Description = $"使用 {display} batchmode/nographics 执行项目内静态 Editor 方法，并返回真实退出码和日志",
                Permissions = ["application.run", "file.read", "file.write", "code.execute"],
                Category = prefix,
                RiskLevel = "high",
                InputSchema = YanziCapabilitySchema.Parse("""
                {
                  "type":"object",
                  "properties":{
                    "projectPath":{"type":"string","minLength":1},
                    "method":{"type":"string","minLength":1},
                    "timeoutSeconds":{"type":"integer","minimum":10,"maximum":1800}
                  },
                  "required":["projectPath","method"],
                  "additionalProperties":false
                }
                """),
                OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
                Handler = payload => ExecuteMethodAsync(prefix, payload)
            };
        }
    }

    private static Task<object?> StatusAsync(string prefix)
    {
        var editor = ResolveEditor(prefix);
        if (editor == null)
            return Task.FromResult<object?>(new
            {
                installed = false,
                editor = prefix
            });

        var info = FileVersionInfo.GetVersionInfo(editor);
        var processName = prefix == "unity" ? "Unity" : "Tuanjie";
        var processes = Process.GetProcessesByName(processName);
        return Task.FromResult<object?>(new
        {
            installed = true,
            editor = prefix,
            executable = editor,
            fileVersion = info.FileVersion,
            productVersion = info.ProductVersion,
            running = processes.Length > 0,
            processIds = processes.Select(process => process.Id).ToArray()
        });
    }

    private static Task<object?> OpenProjectAsync(string prefix, object? payload)
    {
        var input = (JsonElement)payload!;
        var project = RequireProject(input.GetProperty("projectPath").GetString()!);
        var editor = ResolveEditor(prefix)
            ?? throw new FileNotFoundException($"未找到 {prefix} Editor。");

        var info = new ProcessStartInfo
        {
            FileName = editor,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(editor)!
        };
        info.ArgumentList.Add("-projectPath");
        info.ArgumentList.Add(project);

        var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{prefix} Editor 启动失败。");

        return Task.FromResult<object?>(new
        {
            opened = true,
            editor = prefix,
            projectPath = project,
            processId = process.Id
        });
    }

    private static async Task<object?> ExecuteMethodAsync(string prefix, object? payload)
    {
        var input = (JsonElement)payload!;
        var project = RequireProject(input.GetProperty("projectPath").GetString()!);
        var method = input.GetProperty("method").GetString()!.Trim();
        if (!IsSafeMethodName(method))
            throw new ArgumentException("executeMethod 必须是合法的 C# 静态方法完整名，例如 BuildTools.BuildWindows。");

        var timeout = input.TryGetProperty("timeoutSeconds", out var timeoutElement)
            ? timeoutElement.GetInt32()
            : 600;

        var editor = ResolveEditor(prefix)
            ?? throw new FileNotFoundException($"未找到 {prefix} Editor。");

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            HostRuntimeProfile.DataDirectoryName,
            "EngineLogs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory,
            $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");

        var info = new ProcessStartInfo
        {
            FileName = editor,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(editor)!
        };
        foreach (var arg in new[]
        {
            "-batchmode",
            "-nographics",
            "-projectPath", project,
            "-executeMethod", method,
            "-logFile", logPath,
            "-quit"
        })
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"{prefix} batchmode 启动失败。");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"{prefix} batchmode 超过 {timeout} 秒未结束。日志：{logPath}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var log = "";
        try { if (File.Exists(logPath)) log = await File.ReadAllTextAsync(logPath); } catch { }

        return new
        {
            success = process.ExitCode == 0,
            editor = prefix,
            exitCode = process.ExitCode,
            projectPath = project,
            method,
            logPath,
            logTail = Tail(log, 16000),
            stdoutTail = Tail(stdout, 6000),
            stderrTail = Tail(stderr, 6000)
        };
    }

    private static string? ResolveEditor(string prefix)
    {
        var candidates = InstalledApplicationCatalog.Load()
            .Select(entry => entry.DisplayPath)
            .Where(path => File.Exists(path))
            .Where(path => Path.GetFileName(path).Equals(
                prefix == "unity" ? "Unity.exe" : "Tuanjie.exe",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Current machine also contains editors in F:\备用软件 that may not always expose a direct shortcut.
        var known = prefix == "unity"
            ? @"F:\备用软件\2022.3.62f3c1-x86_64\Editor\Unity.exe"
            : @"F:\备用软件\unity\2022.3.61t13\Editor\Tuanjie.exe";
        if (File.Exists(known) && !candidates.Contains(known, StringComparer.OrdinalIgnoreCase))
            candidates.Add(known);

        return candidates
            .OrderByDescending(path => ParseVersion(FileVersionInfo.GetVersionInfo(path).FileVersion))
            .FirstOrDefault();
    }

    private static Version ParseVersion(string? value)
    {
        var numeric = new string((value ?? "0")
            .TakeWhile(ch => char.IsDigit(ch) || ch == '.')
            .ToArray()).TrimEnd('.');
        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0);
    }

    private static string RequireProject(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        var versionFile = Path.Combine(path, "ProjectSettings", "ProjectVersion.txt");
        if (!Directory.Exists(path) || !File.Exists(versionFile))
            throw new DirectoryNotFoundException("不是有效的 Unity/团结项目：缺少 ProjectSettings/ProjectVersion.txt。");
        return path;
    }

    private static bool IsSafeMethodName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240) return false;
        var segments = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;
        return segments.All(segment =>
            (char.IsLetter(segment[0]) || segment[0] == '_') &&
            segment.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_'));
    }

    private static string Tail(string text, int max)
        => text.Length <= max ? text : text[^max..];
}
