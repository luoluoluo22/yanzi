using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

public static class YanziVisualStudioCapabilityProvider
{
    private static readonly Regex ErrorRegex = new(
        @"^(?<file>.*?)(?:\((?<line>\d+)(?:,(?<column>\d+))?\))?\s*:\s*error\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s*\[(?<project>.*?)\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "visualStudio.status",
            Description = "查询 Visual Studio Community、MSBuild 路径、版本和运行状态",
            Permissions = ["application.read"],
            Category = "visual-studio",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "visualStudio.open",
            Description = "使用 Visual Studio IDE 打开 .sln/.slnx 或工程文件",
            Permissions = ["application.run", "file.read"],
            Category = "visual-studio",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"path":{"type":"string","minLength":1}},
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };

        foreach (var target in new[] { "Build", "Rebuild", "Clean" })
        {
            var targetName = target.ToLowerInvariant();
            yield return new()
            {
                Name = $"visualStudio.{targetName}",
                Description = $"使用 Visual Studio 自带 MSBuild 对 solution/project 执行 {target}，返回退出码、错误和日志",
                Permissions = ["application.run", "file.read", "file.write"],
                Category = "visual-studio",
                RiskLevel = "medium",
                InputSchema = YanziCapabilitySchema.Parse("""
                {
                  "type":"object",
                  "properties":{
                    "path":{"type":"string","minLength":1},
                    "configuration":{"type":"string"},
                    "platform":{"type":"string"},
                    "timeoutSeconds":{"type":"integer","minimum":10,"maximum":1800},
                    "properties":{"type":"object"}
                  },
                  "required":["path"],
                  "additionalProperties":false
                }
                """),
                OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
                Handler = payload => BuildAsync(target, payload)
            };
        }
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var installation = ResolveInstallation();
        if (installation == null)
            return Task.FromResult<object?>(new
            {
                installed = false,
                running = Process.GetProcessesByName("devenv").Length > 0
            });

        return Task.FromResult<object?>(new
        {
            installed = true,
            displayName = installation.DisplayName,
            installationPath = installation.InstallationPath,
            installationVersion = installation.InstallationVersion,
            productDisplayVersion = installation.ProductDisplayVersion,
            devenv = installation.DevenvPath,
            msbuild = installation.MsBuildPath,
            running = Process.GetProcessesByName("devenv").Length > 0,
            processIds = Process.GetProcessesByName("devenv").Select(p => p.Id).ToArray()
        });
    }

    private static Task<object?> OpenAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = RequireVisualStudioInput(input.GetProperty("path").GetString()!);
        var installation = ResolveInstallation()
            ?? throw new FileNotFoundException("未找到 Visual Studio Community。");
        if (!File.Exists(installation.DevenvPath))
            throw new FileNotFoundException("Visual Studio devenv.exe 不存在。", installation.DevenvPath);

        var info = new ProcessStartInfo
        {
            FileName = installation.DevenvPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
        };
        info.ArgumentList.Add(path);

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("Visual Studio 启动失败。");

        return Task.FromResult<object?>(new
        {
            opened = true,
            path,
            processId = process.Id,
            devenv = installation.DevenvPath
        });
    }

    private static async Task<object?> BuildAsync(string target, object? payload)
    {
        var input = (JsonElement)payload!;
        var path = RequireVisualStudioInput(input.GetProperty("path").GetString()!);
        var configuration = input.TryGetProperty("configuration", out var configElement) &&
                            !string.IsNullOrWhiteSpace(configElement.GetString())
            ? configElement.GetString()!.Trim()
            : "Debug";
        var platform = input.TryGetProperty("platform", out var platformElement) &&
                       !string.IsNullOrWhiteSpace(platformElement.GetString())
            ? platformElement.GetString()!.Trim()
            : null;
        var timeoutSeconds = input.TryGetProperty("timeoutSeconds", out var timeoutElement)
            ? timeoutElement.GetInt32()
            : 600;

        ValidatePropertyValue(configuration, nameof(configuration));
        if (platform != null) ValidatePropertyValue(platform, nameof(platform));

        var customProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (input.TryGetProperty("properties", out var propertiesElement) &&
            propertiesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                if (!IsSafePropertyName(property.Name))
                    throw new ArgumentException("非法 MSBuild 属性名：" + property.Name);

                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => throw new ArgumentException("MSBuild 属性值仅支持字符串、数字或布尔值：" + property.Name)
                };
                ValidatePropertyValue(value, property.Name);
                customProperties[property.Name] = value;
            }
        }

        var installation = ResolveInstallation()
            ?? throw new FileNotFoundException("未找到 Visual Studio / MSBuild。");
        if (!File.Exists(installation.MsBuildPath))
            throw new FileNotFoundException("MSBuild.exe 不存在。", installation.MsBuildPath);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            HostRuntimeProfile.DataDirectoryName,
            "VisualStudioBuildLogs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory,
            $"msbuild-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");

        var info = new ProcessStartInfo
        {
            FileName = installation.MsBuildPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
        };

        info.ArgumentList.Add(path);
        info.ArgumentList.Add("/nologo");
        info.ArgumentList.Add("/m");
        info.ArgumentList.Add("/consoleloggerparameters:Summary;ForceNoAlign");
        info.ArgumentList.Add("/verbosity:minimal");
        if (!target.Equals("Clean", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add("/restore");
        info.ArgumentList.Add($"/target:{target}");
        info.ArgumentList.Add($"/property:Configuration={configuration}");
        if (!string.IsNullOrWhiteSpace(platform))
            info.ArgumentList.Add($"/property:Platform={platform}");
        foreach (var pair in customProperties)
            info.ArgumentList.Add($"/property:{pair.Key}={pair.Value}");
        info.ArgumentList.Add($"/fileLogger");
        info.ArgumentList.Add($"/fileLoggerParameters:LogFile={logPath};Verbosity=normal;Encoding=UTF-8");

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("无法启动 MSBuild。");

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
            throw new TimeoutException($"MSBuild 超过 {timeoutSeconds} 秒未完成。日志：{logPath}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var logText = "";
        try
        {
            if (File.Exists(logPath))
                logText = await File.ReadAllTextAsync(logPath);
        }
        catch
        {
        }

        var diagnosticText = string.IsNullOrWhiteSpace(logText)
            ? stdout + Environment.NewLine + stderr
            : logText + Environment.NewLine + stderr;
        var errors = ParseErrors(diagnosticText);

        return new
        {
            success = process.ExitCode == 0,
            exitCode = process.ExitCode,
            target,
            path,
            configuration,
            platform,
            msbuild = installation.MsBuildPath,
            logPath,
            errorCount = errors.Length,
            errors,
            logTail = Tail(logText, 16000),
            outputTail = Tail(stdout, 8000),
            errorTail = Tail(stderr, 8000)
        };
    }

    private sealed record VisualStudioInstallation(
        string DisplayName,
        string InstallationPath,
        string InstallationVersion,
        string? ProductDisplayVersion,
        string DevenvPath,
        string MsBuildPath);

    private sealed record BuildError(
        string? File,
        int? Line,
        int? Column,
        string? Code,
        string Message,
        string? Project,
        string Raw);

    private static VisualStudioInstallation? ResolveInstallation()
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return ResolveKnownInstallation();

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = vswhere,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "-latest", "-products", "Microsoft.VisualStudio.Product.Community", "-format", "json" })
                info.ArgumentList.Add(arg);

            using var process = Process.Start(info);
            if (process == null) return ResolveKnownInstallation();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return ResolveKnownInstallation();

            using var json = JsonDocument.Parse(output);
            var item = json.RootElement.ValueKind == JsonValueKind.Array &&
                       json.RootElement.GetArrayLength() > 0
                ? json.RootElement[0]
                : default;
            if (item.ValueKind != JsonValueKind.Object) return ResolveKnownInstallation();

            var installationPath = GetString(item, "installationPath");
            var productPath = GetString(item, "productPath");
            if (string.IsNullOrWhiteSpace(installationPath) || string.IsNullOrWhiteSpace(productPath))
                return ResolveKnownInstallation();

            var msbuild = Path.Combine(installationPath, "MSBuild", "Current", "Bin", "MSBuild.exe");
            if (!File.Exists(msbuild)) return ResolveKnownInstallation();

            string? productDisplayVersion = null;
            if (item.TryGetProperty("catalog", out var catalog) && catalog.ValueKind == JsonValueKind.Object)
                productDisplayVersion = GetString(catalog, "productDisplayVersion");

            return new VisualStudioInstallation(
                GetString(item, "displayName") ?? "Visual Studio",
                installationPath,
                GetString(item, "installationVersion") ?? "",
                productDisplayVersion,
                productPath,
                msbuild);
        }
        catch
        {
            return ResolveKnownInstallation();
        }
    }

    private static VisualStudioInstallation? ResolveKnownInstallation()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Visual Studio", "2022", "Community");
        var devenv = Path.Combine(root, "Common7", "IDE", "devenv.exe");
        var msbuild = Path.Combine(root, "MSBuild", "Current", "Bin", "MSBuild.exe");
        if (!File.Exists(devenv) || !File.Exists(msbuild)) return null;

        var version = FileVersionInfo.GetVersionInfo(devenv);
        return new VisualStudioInstallation(
            "Visual Studio Community 2022",
            root,
            version.FileVersion ?? "",
            version.ProductVersion,
            devenv,
            msbuild);
    }

    private static BuildError[] ParseErrors(string text)
    {
        var results = new List<BuildError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
                continue;

            var trimmed = Regex.Replace(line.Trim(), @"^\d+>", "");
            var match = ErrorRegex.Match(trimmed);
            BuildError error;
            if (match.Success)
            {
                error = new BuildError(
                    EmptyToNull(match.Groups["file"].Value),
                    ParseNullableInt(match.Groups["line"].Value),
                    ParseNullableInt(match.Groups["column"].Value),
                    EmptyToNull(match.Groups["code"].Value),
                    match.Groups["message"].Value.Trim(),
                    EmptyToNull(match.Groups["project"].Value),
                    trimmed);
            }
            else
            {
                error = new BuildError(null, null, null, null, trimmed, null, trimmed);
            }

            var identity = $"{error.File}|{error.Line}|{error.Column}|{error.Code}|{error.Message}|{error.Project}";
            if (!seen.Add(identity))
                continue;

            results.Add(error);
            if (results.Count >= 100) break;
        }

        return results.ToArray();
    }

    private static string RequireVisualStudioInput(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (!File.Exists(path))
            throw new FileNotFoundException("Visual Studio solution/project 不存在。", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".sln" and not ".slnx" and not ".csproj" and not ".vcxproj" and not ".fsproj")
            throw new ArgumentException("仅支持 Visual Studio solution/project 文件。");

        return path;
    }

    private static bool IsSafePropertyName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 120 &&
           (char.IsLetter(value[0]) || value[0] == '_') &&
           value.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.');

    private static void ValidatePropertyValue(string value, string name)
    {
        if (value.Length > 1000 || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException($"MSBuild 属性值无效：{name}");
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ParseNullableInt(string value)
        => int.TryParse(value, out var number) ? number : null;

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Tail(string text, int max)
        => text.Length <= max ? text : text[^max..];
}
