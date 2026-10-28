using System.Diagnostics;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// 系统级能力 Provider。
/// 为后续 AI Agent 提供基础环境感知能力。
/// </summary>
public static class YanziSystemCapabilityProvider
{
    private static readonly IReadOnlyDictionary<string, string> LaunchTargets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad"] = "notepad.exe",
            ["记事本"] = "notepad.exe",
            ["calculator"] = "calc.exe",
            ["计算器"] = "calc.exe",
            ["explorer"] = "explorer.exe",
            ["文件资源管理器"] = "explorer.exe"
        };

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new YanziCapabilityProviderDefinition
        {
            Name = "system.process.list",
            Description = "获取当前运行进程列表",
            Permissions = new[] { "system.process.read" },
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"id\":{\"type\":\"integer\"}}}}"),
            Handler = _ => Task.FromResult<object?>(
                Process.GetProcesses()
                    .OrderBy(p => p.ProcessName)
                    .Take(50)
                    .Select(p => new
                    {
                        name = p.ProcessName,
                        id = p.Id
                    })
                    .ToList())
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "system.app.launch",
            Description = "启动受信任白名单中的 Windows 应用",
            Permissions = new[] { "system.app.launch" },
            InputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"target\"],\"additionalProperties\":false}"),
            OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\"},\"executable\":{\"type\":\"string\"},\"started\":{\"type\":\"boolean\"}},\"required\":[\"target\",\"executable\",\"started\"],\"additionalProperties\":false}"),
            Handler = payload =>
            {
                var element = (JsonElement)payload!;
                var target = element.GetProperty("target").GetString()?.Trim() ?? string.Empty;
                if (!LaunchTargets.TryGetValue(target, out var executable))
                    throw new ArgumentException($"不支持的应用目标：{target}");

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true
                });

                return Task.FromResult<object?>(new
                {
                    target,
                    executable,
                    started = process is not null
                });
            }
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "system.time.now",
            Description = "获取当前时间",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"local\":{\"type\":\"string\"},\"utc\":{\"type\":\"string\"}}}"),
            Handler = _ => Task.FromResult<object?>(new
            {
                local = DateTime.Now,
                utc = DateTime.UtcNow
            })
        };
    }
}
