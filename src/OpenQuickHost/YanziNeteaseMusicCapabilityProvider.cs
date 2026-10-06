using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziNeteaseMusicCapabilityProvider
{
    private const int SwRestore = 9;
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaStop = 0xB2;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyeventfKeyup = 0x0002;

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "netease.status",
            Description = "查询网易云音乐安装、运行状态及当前窗口暴露的歌曲/歌手信息",
            Permissions = ["application.read"],
            Category = "music",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return MediaKeyCapability("netease.playPause", "切换网易云音乐播放/暂停", VkMediaPlayPause);
        yield return MediaKeyCapability("netease.next", "播放网易云音乐下一首", VkMediaNextTrack);
        yield return MediaKeyCapability("netease.previous", "播放网易云音乐上一首", VkMediaPrevTrack);
        yield return MediaKeyCapability("netease.stop", "停止网易云音乐播放", VkMediaStop);

        yield return new()
        {
            Name = "netease.playFile",
            Description = "使用网易云音乐官方文件启动参数播放本地音频文件",
            Permissions = ["application.run", "file.read"],
            Category = "music",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"path":{"type":"string","minLength":1}},
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PlayFileAsync
        };

        yield return new()
        {
            Name = "netease.openUri",
            Description = "将已知 orpheus:// URI 交给网易云音乐注册协议处理",
            Permissions = ["application.run"],
            Category = "music",
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

    private static YanziCapabilityProviderDefinition MediaKeyCapability(string name, string description, byte key)
        => new()
        {
            Name = name,
            Description = description,
            Permissions = ["application.run"],
            Category = "music",
            Handler = async _ =>
            {
                var process = await EnsureRunningAsync();
                Focus(process);
                keybd_event(key, 0, 0, UIntPtr.Zero);
                keybd_event(key, 0, KeyeventfKeyup, UIntPtr.Zero);
                await Task.Delay(120);
                return new
                {
                    sent = true,
                    processId = process.Id,
                    key = name[(name.LastIndexOf('.') + 1)..],
                    windowTitle = SafeTitle(process)
                };
            }
        };

    private static Task<object?> StatusAsync(object? _)
    {
        var executable = ResolveExecutable();
        var process = FindMainProcess();
        var title = process == null ? "" : SafeTitle(process);
        var parsed = ParseTrackTitle(title);

        return Task.FromResult<object?>(new
        {
            installed = executable != null,
            executable,
            running = Process.GetProcessesByName("cloudmusic").Length > 0,
            hasMainWindow = process != null,
            windowTitle = title,
            track = parsed.Track,
            artist = parsed.Artist
        });
    }

    private static async Task<object?> PlayFileAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("path").GetString()!.Trim().Trim('"')));
        if (!File.Exists(path)) throw new FileNotFoundException("音频文件不存在。", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".wav", ".aac", ".ape", ".m4a", ".ogg", ".wma", ".ncm", ".cue", ".cda" };
        if (!supported.Contains(extension))
            throw new ArgumentException("网易云音乐文件关联中未发现该音频类型：" + extension);

        var executable = ResolveExecutable() ?? throw new FileNotFoundException("未找到网易云音乐。");
        var info = new ProcessStartInfo { FileName = executable, UseShellExecute = false };
        info.ArgumentList.Add("--play=" + path);
        var process = Process.Start(info) ?? throw new InvalidOperationException("网易云音乐启动失败。");

        await Task.Delay(500);
        return new { started = true, path, processId = process.Id, executable };
    }

    private static Task<object?> OpenUriAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var uri = input.GetProperty("uri").GetString()!.Trim();
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            !parsed.Scheme.Equals("orpheus", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("只允许 orpheus:// URI。");

        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });

        return Task.FromResult<object?>(new { opened = true, uri });
    }

    private static async Task<Process> EnsureRunningAsync()
    {
        var process = FindMainProcess();
        if (process != null) return process;

        await YanziCapabilityRegistry.InvokeAsync(
            "app.open",
            new { app = "网易云音乐" },
            YanziCapabilityCaller.LocalAgent);

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(150);
            process = FindMainProcess();
            if (process != null) return process;
        }

        throw new InvalidOperationException("网易云音乐已启动，但未找到主窗口。");
    }

    private static Process? FindMainProcess()
    {
        foreach (var process in Process.GetProcessesByName("cloudmusic"))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process;
            }
            catch { }
        }
        return null;
    }

    private static void Focus(Process process)
    {
        try
        {
            process.Refresh();
            if (process.MainWindowHandle == IntPtr.Zero) return;
            ShowWindow(process.MainWindowHandle, SwRestore);
            if (!WindowSensorHelper.ForceSetForegroundWindow(process.MainWindowHandle))
                SetForegroundWindow(process.MainWindowHandle);
        }
        catch { }
    }

    private static string? ResolveExecutable()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NetEase", "CloudMusic", "cloudmusic.exe");
        return File.Exists(path) ? path : null;
    }

    private static string SafeTitle(Process process)
    {
        try
        {
            process.Refresh();
            return process.MainWindowTitle ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static (string? Track, string? Artist) ParseTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return (null, null);
        var index = title.LastIndexOf(" - ", StringComparison.Ordinal);
        if (index <= 0 || index >= title.Length - 3) return (title.Trim(), null);
        return (title[..index].Trim(), title[(index + 3)..].Trim());
    }
}
