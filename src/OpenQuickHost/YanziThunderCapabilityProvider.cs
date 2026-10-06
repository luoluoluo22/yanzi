using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziThunderCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "thunder.status",
            Description = "查询迅雷安装路径、版本和运行状态",
            Permissions = ["application.read"],
            Category = "thunder",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };
        yield return new()
        {
            Name = "thunder.open",
            Description = "启动迅雷客户端",
            Permissions = ["application.run"],
            Category = "thunder",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };
        yield return new()
        {
            Name = "thunder.addDownload",
            Description = "把 HTTP/HTTPS/FTP/磁力/thunder/thunderx 链接交给迅雷创建下载任务",
            Permissions = ["application.run", "network.write"],
            Category = "thunder",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"url":{"type":"string","minLength":1}},"required":["url"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = AddDownloadAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var exe = ResolveThunderExe();
        var info = exe == null ? null : FileVersionInfo.GetVersionInfo(exe);
        var processes = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("Thunder", StringComparison.OrdinalIgnoreCase) ||
                        p.ProcessName.Contains("Xunlei", StringComparison.OrdinalIgnoreCase))
            .Select(p => new { p.Id, p.ProcessName })
            .ToArray();
        return Task.FromResult<object?>(new
        {
            installed = exe != null,
            executable = exe,
            startExecutable = ResolveStartExe(),
            version = info?.FileVersion,
            running = processes.Length > 0,
            processes,
            supportedSchemes = new[] { "http", "https", "ftp", "magnet", "thunder", "thunderx" }
        });
    }

    private static Task<object?> OpenAsync(object? _)
    {
        var start = ResolveStartExe() ?? ResolveThunderExe() ?? throw new FileNotFoundException("未找到迅雷。");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = start,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(start)!
        }) ?? throw new InvalidOperationException("迅雷启动失败。");
        return Task.FromResult<object?>(new { opened = true, executable = start, processId = process.Id });
    }

    private static Task<object?> AddDownloadAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var url = ValidateUrl(input.GetProperty("url").GetString()?.Trim() ?? "");
        var exe = ResolveThunderExe() ?? throw new FileNotFoundException("未找到迅雷主程序。");
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        info.ArgumentList.Add(url);
        info.ArgumentList.Add("-StartType:yanzi");
        var process = Process.Start(info) ?? throw new InvalidOperationException("无法把下载任务交给迅雷。");
        return Task.FromResult<object?>(new { accepted = true, url, executable = exe, processId = process.Id });
    }

    private static string ValidateUrl(string value)
    {
        if (value.Length == 0 || value.Length > 8192 || value.IndexOfAny(['\r','\n','\0']) >= 0)
            throw new ArgumentException("下载链接无效。");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new ArgumentException("下载链接必须是绝对 URI。");
        if (uri.Scheme.ToLowerInvariant() is not ("http" or "https" or "ftp" or "magnet" or "thunder" or "thunderx"))
            throw new ArgumentException("仅支持 HTTP/HTTPS/FTP/磁力/thunder/thunderx 链接。");
        return value;
    }

    private static string? ResolveThunderExe()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Thunder Network", "Thunder", "Program", "Thunder.exe");
        return File.Exists(path) ? path : null;
    }

    private static string? ResolveStartExe()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Thunder Network", "Thunder", "Program", "ThunderStart.exe");
        return File.Exists(path) ? path : null;
    }
}
