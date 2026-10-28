using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

// The browser supplies a capability name, never a command line or executable.
internal static class BrowserOpenCliReader
{
    internal static string[] Command(string site, string operation, int page)
    {
        if (site != "xiaohongshu" || page != 1)
            throw new ArgumentException("本次 OpenCLI 接入仅支持小红书只读操作。");
        return operation switch
        {
            "profile" => ["creator-profile"],
            "stats" => ["creator-stats", "--period", "seven"],
            "notes" => ["creator-notes", "--limit", "20"],
            _ => throw new ArgumentException("不支持的 OpenCLI 读取操作。")
        };
    }

    internal static string SelectProfile(string listing, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!Regex.IsMatch(requested, @"\A[A-Za-z0-9_-]{1,64}\z"))
                throw new ArgumentException("浏览器配置名称只能包含字母、数字、下划线和短横线。");
            return requested;
        }
        var profiles = Regex.Matches(listing, @"(?m)^\s*([A-Za-z0-9_-]+)[^\r\n]* — connected v")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        return profiles.Length switch
        {
            1 => profiles[0],
            0 => throw new InvalidOperationException("OpenCLI Browser Bridge 未连接，请在浏览器启用 OpenCLI 扩展。"),
            _ => throw new InvalidOperationException("检测到多个浏览器配置，请在读取区填写要使用的 OpenCLI 配置名称或 ID。")
        };
    }

    internal static JsonElement ParseOutput(string output)
    {
        if (output.Length > 256_000) throw new InvalidOperationException("OpenCLI 结果超过 256KB。");
        var bytes = Encoding.UTF8.GetBytes(output.Trim());
        var reader = new Utf8JsonReader(bytes);
        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new JsonException();
            // OpenCLI appends upgrade notices after JSON. Only accept known notices.
            var tail = Encoding.UTF8.GetString(bytes.AsSpan((int)reader.BytesConsumed)).Trim();
            if (tail.Length > 0 && !tail.StartsWith("Update available:", StringComparison.Ordinal) &&
                !tail.StartsWith("Extension update available:", StringComparison.Ordinal))
                throw new JsonException();
            return doc.RootElement.Clone();
        }
        catch (JsonException) { throw new InvalidOperationException("OpenCLI 未返回有效的 JSON 数据。"); }
    }

    internal static async Task<object> ReadAsync(string site, string operation, int page,
        string? requestedProfile, CancellationToken cancellation)
    {
        var command = Command(site, operation, page);
        var listing = string.IsNullOrWhiteSpace(requestedProfile)
            ? await RunAsync(["profile", "list"], cancellation) : "";
        var profile = SelectProfile(listing, requestedProfile);
        var output = await RunAsync(["--profile", profile, site, .. command, "--format", "json"], cancellation);
        return new
        {
            data = ParseOutput(output),
            source = "opencli",
            profile,
            command = site + " " + string.Join(" ", command),
            period = operation == "stats" ? "seven" : null,
            collectedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<string> RunAsync(string[] arguments, CancellationToken cancellation)
    {
        var script = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@jackwener", "opencli", "dist", "src", "main.js");
        if (!File.Exists(script)) throw new FileNotFoundException("本机尚未安装 OpenCLI。");
        var node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        var start = new ProcessStartInfo(File.Exists(node) ? node : "node.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动 OpenCLI。");
        // Bound each stream while draining it, rather than checking after unlimited buffering.
        async Task<string> ReadBoundedAsync(StreamReader stream)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(), cancellation)) > 0)
            {
                if (text.Length + count > 256_000)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("OpenCLI 输出超过 256KB。");
                }
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }
        var stdout = ReadBoundedAsync(process.StandardOutput);
        var stderr = ReadBoundedAsync(process.StandardError);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(cancellation), stdout, stderr);
            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr.Result) ? stdout.Result : stderr.Result;
                throw new InvalidOperationException("OpenCLI 读取失败：" + message.Trim()[..Math.Min(400, message.Trim().Length)]);
            }
            return stdout.Result;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
