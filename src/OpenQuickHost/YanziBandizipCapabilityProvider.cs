using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziBandizipCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "bandizip.status",
            Description = "查询 Bandizip 安装路径、版本和运行状态",
            Permissions = ["application.read"],
            Category = "bandizip",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "bandizip.compress",
            Description = "使用 Bandizip 创建 ZIP/7Z/TAR 等压缩包",
            Permissions = ["file.read", "file.write", "application.run"],
            Category = "bandizip",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "archive":{"type":"string","minLength":1},
                "inputs":{"type":"array","items":{"type":"string"}},
                "level":{"type":"integer","minimum":0,"maximum":9},
                "overwrite":{"type":"boolean"}
              },
              "required":["archive","inputs"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = CompressAsync
        };

        yield return new()
        {
            Name = "bandizip.extract",
            Description = "使用 Bandizip 解压归档；默认跳过已有同名文件",
            Permissions = ["file.read", "file.write", "application.run"],
            Category = "bandizip",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "archive":{"type":"string","minLength":1},
                "outputDirectory":{"type":"string","minLength":1},
                "overwrite":{"type":"boolean"}
              },
              "required":["archive","outputDirectory"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ExtractAsync
        };

        yield return new()
        {
            Name = "bandizip.test",
            Description = "使用 Bandizip 测试压缩包完整性",
            Permissions = ["file.read", "application.run"],
            Category = "bandizip",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"archive":{"type":"string","minLength":1}},"required":["archive"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = TestAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var exe = ResolveExe();
        var info = exe == null ? null : FileVersionInfo.GetVersionInfo(exe);
        return Task.FromResult<object?>(new
        {
            installed = exe != null,
            executable = exe,
            version = info?.FileVersion,
            productVersion = info?.ProductVersion,
            running = Process.GetProcessesByName("Bandizip").Length > 0,
            consoleExecutableAvailable = File.Exists(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bandizip", "bz.exe"))
        });
    }

    private static async Task<object?> CompressAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var exe = RequireExe();
        var archive = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("archive").GetString()!.Trim().Trim('"')));

        var inputs = input.GetProperty("inputs");
        if (inputs.ValueKind != JsonValueKind.Array || inputs.GetArrayLength() == 0 || inputs.GetArrayLength() > 1000)
            throw new ArgumentException("inputs 必须包含 1-1000 个文件或文件夹。");

        var resolved = new List<string>();
        foreach (var item in inputs.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new ArgumentException("inputs 必须为路径字符串。");
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables((item.GetString() ?? "").Trim().Trim('"')));
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("待压缩路径不存在。", path);
            resolved.Add(path);
        }

        var overwrite = input.TryGetProperty("overwrite", out var overwriteElement) && overwriteElement.ValueKind == JsonValueKind.True;
        if (File.Exists(archive) && !overwrite)
            throw new IOException("目标压缩包已存在；需要覆盖时设置 overwrite=true。");

        Directory.CreateDirectory(Path.GetDirectoryName(archive) ?? throw new ArgumentException("archive 路径无父目录。"));
        var level = input.TryGetProperty("level", out var levelElement) && levelElement.TryGetInt32(out var levelValue)
            ? Math.Clamp(levelValue, 0, 9) : 5;

        var args = new List<string> { "c", "-y", $"-l:{level}", archive };
        args.AddRange(resolved);
        var exitCode = await RunAsync(exe, args, 300);

        return new
        {
            success = exitCode == 0 && File.Exists(archive),
            exitCode,
            archive,
            bytes = File.Exists(archive) ? new FileInfo(archive).Length : 0,
            inputCount = resolved.Count
        };
    }

    private static async Task<object?> ExtractAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var exe = RequireExe();
        var archive = RequireFile(input.GetProperty("archive").GetString()!);
        var output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("outputDirectory").GetString()!.Trim().Trim('"')));
        Directory.CreateDirectory(output);

        var overwrite = input.TryGetProperty("overwrite", out var overwriteElement) && overwriteElement.ValueKind == JsonValueKind.True;
        var policy = overwrite ? "-aoa" : "-aos";
        var exitCode = await RunAsync(exe, ["x", "-y", policy, $"-o:{output}", archive], 300);

        return new
        {
            success = exitCode == 0,
            exitCode,
            archive,
            outputDirectory = output,
            overwrite
        };
    }

    private static async Task<object?> TestAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var exe = RequireExe();
        var archive = RequireFile(input.GetProperty("archive").GetString()!);
        var exitCode = await RunAsync(exe, ["t", "-y", archive], 300);
        return new { valid = exitCode == 0, exitCode, archive };
    }

    private static async Task<int> RunAsync(string exe, IEnumerable<string> args, int timeoutSeconds)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Bandizip 启动失败。");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"Bandizip 超过 {timeoutSeconds} 秒未完成。");
        }
        return process.ExitCode;
    }

    private static string? ResolveExe()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Bandizip", "Bandizip.exe");
        return File.Exists(path) ? path : null;
    }

    private static string RequireExe()
        => ResolveExe() ?? throw new FileNotFoundException("未找到 Bandizip。");

    private static string RequireFile(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (!File.Exists(path)) throw new FileNotFoundException("文件不存在。", path);
        return path;
    }
}
