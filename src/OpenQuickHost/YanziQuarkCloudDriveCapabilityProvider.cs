using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziQuarkCloudDriveCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "quark.status",
            Description = "查询夸克/夸克网盘安装、版本和运行状态",
            Permissions = ["application.read"],
            Category = "cloud-drive",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "quark.cloudDrive.open",
            Description = "使用夸克官方 --brand-clouddrive 启动模式打开夸克网盘",
            Permissions = ["application.run"],
            Category = "cloud-drive",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenCloudDriveAsync
        };

        yield return new()
        {
            Name = "quark.cloudDrive.openPath",
            Description = "使用夸克网盘注册的 --brand-clouddrive 模式打开本地文件或目录",
            Permissions = ["application.run", "file.read"],
            Category = "cloud-drive",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"path":{"type":"string","minLength":1}},
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenPathAsync
        };

        yield return new()
        {
            Name = "quark.cloudDrive.openUri",
            Description = "将 qkclouddrive:// URI 交给系统注册的夸克网盘协议处理",
            Permissions = ["application.run"],
            Category = "cloud-drive",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"uri":{"type":"string","minLength":1}},
              "required":["uri"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenUriAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var executable = ResolveExecutable();
        var info = executable == null ? null : FileVersionInfo.GetVersionInfo(executable);
        var processes = Process.GetProcessesByName("quark");
        return Task.FromResult<object?>(new
        {
            installed = executable != null,
            executable,
            version = info?.FileVersion,
            running = processes.Length > 0,
            processIds = processes.Select(p => p.Id).ToArray(),
            protocol = "qkclouddrive"
        });
    }

    private static Task<object?> OpenCloudDriveAsync(object? _)
    {
        var process = Start(["--brand-clouddrive"]);
        return Task.FromResult<object?>(new
        {
            opened = true,
            processId = process.Id,
            mode = "cloud-drive"
        });
    }

    private static Task<object?> OpenPathAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("path").GetString()!.Trim().Trim('"')));
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("目标路径不存在。", path);

        var process = Start(["--brand-clouddrive", path]);
        return Task.FromResult<object?>(new
        {
            opened = true,
            processId = process.Id,
            path,
            mode = "cloud-drive"
        });
    }

    private static Task<object?> OpenUriAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var uri = input.GetProperty("uri").GetString()!.Trim();
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            !parsed.Scheme.Equals("qkclouddrive", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("只允许 qkclouddrive:// URI。");

        Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        return Task.FromResult<object?>(new { opened = true, uri });
    }

    private static Process Start(IEnumerable<string> args)
    {
        var executable = ResolveExecutable() ?? throw new FileNotFoundException("未找到夸克。");
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException("夸克启动失败。");
    }

    private static string? ResolveExecutable()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Quark", "quark.exe");
        return File.Exists(path) ? path : null;
    }
}
