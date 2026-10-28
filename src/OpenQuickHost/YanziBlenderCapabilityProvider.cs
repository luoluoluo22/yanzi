using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziBlenderCapabilityProvider
{
    private const string JsonMarker = "__YANZI_BLENDER_JSON__";

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "blender.status",
            Description = "查询 Blender 安装路径、版本和运行状态",
            Permissions = ["application.read"],
            Category = "blender",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "blender.open",
            Description = "用 Blender 打开本地 .blend 工程",
            Permissions = ["application.run", "file.read"],
            Category = "blender",
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

        yield return new()
        {
            Name = "blender.scene.inspect",
            Description = "后台读取 .blend 场景信息：场景名、对象、相机、帧范围、渲染分辨率和渲染引擎",
            Permissions = ["application.run", "file.read"],
            Category = "blender",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"path":{"type":"string","minLength":1}},
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = InspectAsync
        };

        yield return new()
        {
            Name = "blender.renderFrame",
            Description = "使用 Blender 后台模式渲染指定 .blend 的单帧到明确输出文件",
            Permissions = ["application.run", "file.read", "file.write"],
            Category = "blender",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "outputPath":{"type":"string","minLength":1},
                "frame":{"type":"integer","minimum":0}
              },
              "required":["path","outputPath"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = RenderFrameAsync
        };

        yield return new()
        {
            Name = "blender.python.run",
            Description = "在 Blender 后台上下文中运行本地 Python 脚本；用于高级建模、批处理和资产流水线",
            Permissions = ["application.run", "file.read", "file.write", "code.execute"],
            Category = "blender",
            RiskLevel = "high",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "scriptPath":{"type":"string","minLength":1},
                "blendPath":{"type":"string"},
                "timeoutSeconds":{"type":"integer","minimum":5,"maximum":600}
              },
              "required":["scriptPath"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = RunPythonAsync
        };
    }

    private static async Task<object?> StatusAsync(object? _)
    {
        var exe = ResolveExecutable();
        if (exe == null)
            return new { installed = false, running = Process.GetProcessesByName("blender").Length > 0 };

        var result = await RunAsync(["--version"], 15);
        var versionLine = SplitLines(result.Stdout).FirstOrDefault(line => line.StartsWith("Blender ", StringComparison.OrdinalIgnoreCase));
        return new
        {
            installed = true,
            executable = exe,
            version = versionLine?.Replace("Blender ", "", StringComparison.OrdinalIgnoreCase).Trim(),
            running = Process.GetProcessesByName("blender").Length > 0,
            exitCode = result.ExitCode
        };
    }

    private static Task<object?> OpenAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = RequireBlend(input.GetProperty("path").GetString()!);
        var exe = ResolveExecutable() ?? throw new FileNotFoundException("未找到 Blender。");

        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        info.ArgumentList.Add(path);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Blender 启动失败。");
        return Task.FromResult<object?>(new { opened = true, path, processId = process.Id });
    }

    private static async Task<object?> InspectAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = RequireBlend(input.GetProperty("path").GetString()!);

        const string expression =
            "import bpy,json;" +
            "s=bpy.context.scene;" +
            "d={'scene':s.name,'objects':[{'name':o.name,'type':o.type} for o in s.objects]," +
            "'camera':s.camera.name if s.camera else None,'frameStart':s.frame_start,'frameEnd':s.frame_end," +
            "'frameCurrent':s.frame_current,'renderEngine':s.render.engine," +
            "'resolution':{'x':s.render.resolution_x,'y':s.render.resolution_y,'percentage':s.render.resolution_percentage}};" +
            "print('" + JsonMarker + "'+json.dumps(d,ensure_ascii=False))";

        var result = await RunAsync(["--background", path, "--python-exit-code", "1", "--python-expr", expression], 60);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Blender 场景读取失败：" + Tail(result.Stderr + result.Stdout));

        var line = SplitLines(result.Stdout).LastOrDefault(value => value.StartsWith(JsonMarker, StringComparison.Ordinal));
        if (line == null)
            throw new InvalidOperationException("Blender 未返回结构化场景信息。");

        using var document = JsonDocument.Parse(line[JsonMarker.Length..]);
        return new
        {
            path,
            scene = document.RootElement.Clone(),
            exitCode = result.ExitCode
        };
    }

    private static async Task<object?> RenderFrameAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var blend = RequireBlend(input.GetProperty("path").GetString()!);
        var output = PrepareOutput(input.GetProperty("outputPath").GetString()!);
        var frame = input.TryGetProperty("frame", out var frameElement) ? frameElement.GetInt32() : 1;

        var quotedOutput = JsonSerializer.Serialize(output);
        var expression =
            "import bpy;" +
            $"s=bpy.context.scene;s.frame_set({frame.ToString(CultureInfo.InvariantCulture)});" +
            $"s.render.filepath={quotedOutput};" +
            "bpy.ops.render.render(write_still=True)";

        var result = await RunAsync(["--background", blend, "--python-exit-code", "1", "--python-expr", expression], 300);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Blender 渲染失败：" + Tail(result.Stderr + result.Stdout));

        return new
        {
            rendered = File.Exists(output),
            blendPath = blend,
            outputPath = output,
            frame,
            bytes = File.Exists(output) ? new FileInfo(output).Length : 0,
            exitCode = result.ExitCode
        };
    }

    private static async Task<object?> RunPythonAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var script = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("scriptPath").GetString()!.Trim().Trim('"')));
        if (!File.Exists(script) || !script.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Blender Python 脚本不存在或不是 .py。", script);

        string? blend = null;
        if (input.TryGetProperty("blendPath", out var blendElement) &&
            !string.IsNullOrWhiteSpace(blendElement.GetString()))
            blend = RequireBlend(blendElement.GetString()!);

        var timeout = input.TryGetProperty("timeoutSeconds", out var timeoutElement)
            ? timeoutElement.GetInt32()
            : 120;

        var args = new List<string> { "--background" };
        if (blend != null) args.Add(blend);
        args.Add("--python-exit-code");
        args.Add("1");
        args.Add("--python");
        args.Add(script);

        var result = await RunAsync(args, timeout);
        return new
        {
            success = result.ExitCode == 0,
            exitCode = result.ExitCode,
            scriptPath = script,
            blendPath = blend,
            stdout = Tail(result.Stdout, 12000),
            stderr = Tail(result.Stderr, 12000)
        };
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private static async Task<ProcessResult> RunAsync(IEnumerable<string> args, int timeoutSeconds)
    {
        var exe = ResolveExecutable() ?? throw new FileNotFoundException("未找到 Blender。");
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Blender。");
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
            throw new TimeoutException($"Blender 超过 {timeoutSeconds} 秒未完成。");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string? ResolveExecutable()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Blender Foundation");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "blender.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string RequireBlend(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (!File.Exists(path) || !path.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Blender 工程不存在或不是 .blend。", path);
        return path;
    }

    private static string PrepareOutput(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("输出路径无父目录。");
        Directory.CreateDirectory(directory);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
            path += ".png";
        return path;
    }

    private static string[] SplitLines(string text)
        => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Tail(string text, int max = 4000)
        => text.Length <= max ? text : text[^max..];
}
