using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziBaiduNetdiskCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "baiduNetdisk.status",
            Description = "查询百度网盘客户端安装、版本和进程状态",
            Permissions = ["application.read"],
            Category = "cloud-drive",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "baiduNetdisk.open",
            Description = "启动百度网盘客户端",
            Permissions = ["application.run"],
            Category = "cloud-drive",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };

        yield return new()
        {
            Name = "baiduNetdisk.upload",
            Description = "调用 Windows Shell 中百度网盘官方“上传到百度网盘” Verb 交付本地文件；能力确认的是客户端已接管上传，不虚报云端传输完成",
            Permissions = ["application.run", "file.read", "network.write"],
            Category = "cloud-drive",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"path":{"type":"string","minLength":1}},
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = UploadAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var executable = ResolveExecutable();
        var info = executable == null ? null : FileVersionInfo.GetVersionInfo(executable);
        var processes = Process.GetProcesses()
            .Where(p => p.ProcessName.Contains("BaiduNetdisk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Task.FromResult<object?>(new
        {
            installed = executable != null,
            executable,
            version = info?.FileVersion,
            running = processes.Length > 0,
            processIds = processes.Select(p => p.Id).ToArray()
        });
    }

    private static async Task<object?> OpenAsync(object? _)
    {
        var result = await YanziCapabilityRegistry.InvokeAsync(
            "app.open",
            new { app = "百度网盘" },
            YanziCapabilityCaller.LocalAgent);
        return new { opened = true, result };
    }

    private static async Task<object?> UploadAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("path").GetString()!.Trim().Trim('"')));
        if (!File.Exists(path))
            throw new FileNotFoundException("百度网盘当前上传 Provider 只支持本地文件，且文件必须存在。", path);

        if (Process.GetProcesses()
            .All(p => !p.ProcessName.Contains("BaiduNetdisk", StringComparison.OrdinalIgnoreCase)))
        {
            try { await OpenAsync(null); } catch { }
            await Task.Delay(800);
        }

        var verbName = await RunStaAsync(() =>
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException("Windows Shell.Application 不可用。");
            dynamic? shell = null;
            dynamic? folder = null;
            dynamic? item = null;
            dynamic? matchedVerb = null;
            try
            {
                shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("无法创建 Windows Shell.Application 实例。");
                var directory = Path.GetDirectoryName(path)!;
                folder = shell.NameSpace(directory)
                    ?? throw new InvalidOperationException("无法打开文件所在 Shell 目录。");
                item = folder.ParseName(Path.GetFileName(path))
                    ?? throw new InvalidOperationException("Shell 无法解析上传文件。");

                foreach (dynamic verb in item.Verbs())
                {
                    string name = "";
                    try { name = ((string?)verb.Name ?? "").Replace("&", "").Trim(); } catch { }
                    if (name.Equals("上传到百度网盘", StringComparison.OrdinalIgnoreCase))
                    {
                        matchedVerb = verb;
                        break;
                    }
                    ReleaseCom(verb);
                }

                if (matchedVerb == null)
                    throw new InvalidOperationException("当前系统没有注册“上传到百度网盘” Shell Verb。");

                string rawName = matchedVerb.Name;
                matchedVerb.DoIt();
                return rawName.Replace("&", "").Trim();
            }
            finally
            {
                ReleaseCom(matchedVerb);
                ReleaseCom(item);
                ReleaseCom(folder);
                ReleaseCom(shell);
            }
        });

        await Task.Delay(500);
        return new
        {
            status = "triggered_unconfirmed",
            triggered = true,
            confirmed = false,
            path,
            verb = verbName,
            clientRunning = Process.GetProcesses()
                .Any(p => p.ProcessName.Contains("BaiduNetdisk", StringComparison.OrdinalIgnoreCase)),
            message = "文件已经交给百度网盘官方 Shell 上传入口；当前客户端没有暴露可靠的云端完成回执，因此不把“已触发”误报为“已上传完成”。"
        };
    }

    private static string? ResolveExecutable()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "baidu", "BaiduNetdisk", "BaiduNetdisk.exe");
        return File.Exists(path) ? path : null;
    }

    private static Task<T> RunStaAsync<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(action()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static void ReleaseCom(object? value)
    {
        if (value == null || !Marshal.IsComObject(value)) return;
        try { Marshal.FinalReleaseComObject(value); } catch { }
    }
}
