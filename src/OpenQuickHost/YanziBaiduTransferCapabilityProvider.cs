using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Read-only Baidu Netdisk desktop transfer history and integrity proof.
/// Reuses the official client's SQLite task logs. Never touches credentials.
/// </summary>
public static class YanziBaiduTransferCapabilityProvider
{
    private static readonly JsonElement ObjectSchema =
        YanziCapabilitySchema.Parse("""{"type":"object"}""");

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.transferStatus",
            Description = "只读查询百度网盘官方客户端上传或下载完成记录；结果来自本机传输历史而非实时云端列表",
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = ["application.read", "file.read"],
            RiskLevel = "low",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "kind":{"type":"string","enum":["upload","download"]},
                "path":{"type":"string","minLength":1},
                "afterSeconds":{"type":"integer","minimum":0}
              },
              "required":["kind","path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = ObjectSchema,
            Handler = input =>
            {
                var json = (JsonElement)input!;
                var kind = json.GetProperty("kind").GetString()
                    ?? throw new ArgumentException("缺少传输方向");
                var path = json.GetProperty("path").GetString()
                    ?? throw new ArgumentException("缺少本地文件路径");
                var after = json.TryGetProperty("afterSeconds", out var raw)
                    && raw.TryGetInt64(out var seconds) ? seconds : 0;
                return InvokeLocalBridgeAsync(new
                {
                    operation = "transferStatus",
                    kind, path, afterSeconds = after
                });
            }
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.roundtripVerify",
            Description = "只读交叉校验百度网盘上传及下载完成记录、相同云端路径、文件大小与 SHA-256",
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = ["application.read", "file.read"],
            RiskLevel = "low",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "originalLocalFile":{"type":"string","minLength":1},
                "downloadedFile":{"type":"string","minLength":1}
              },
              "required":["originalLocalFile","downloadedFile"],
              "additionalProperties":false
            }
            """),
            OutputSchema = ObjectSchema,
            Handler = input =>
            {
                var json = (JsonElement)input!;
                var originalLocalFile = json.GetProperty("originalLocalFile").GetString()
                    ?? throw new ArgumentException("缺少原始文件");
                var downloadedFile = json.GetProperty("downloadedFile").GetString()
                    ?? throw new ArgumentException("缺少下载文件");
                return InvokeLocalBridgeAsync(new
                {
                    operation = "roundtripVerify", originalLocalFile, downloadedFile
                });
            }
        };
    }

    private static async Task<object?> InvokeLocalBridgeAsync(object payload)
    {
        var script = Path.Combine(
            AppContext.BaseDirectory, "CapabilityScripts", "BaiduNetdisk",
            "baidu_capability_host.py");
        if (!File.Exists(script))
            throw new FileNotFoundException(
                "百度网盘只读能力辅助程序未随宿主安装，请更新燕子。");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = Path.GetDirectoryName(script)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-I");
        process.StartInfo.ArgumentList.Add(script);
        if (!process.Start())
            throw new InvalidOperationException("无法启动本机百度网盘只读校验器");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(payload));
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var data = await stdout;
            _ = await stderr;
            if (data.Length > 1024 * 1024)
                throw new InvalidOperationException("百度只读能力响应过大");
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.True)
                return root.GetProperty("result").Clone();
            var code = root.TryGetProperty("code", out var e)
                ? e.GetString() : "unknown";
            throw new InvalidOperationException(
                $"百度网盘任务查询失败（{code}）");
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("百度网盘只读任务查询超时");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }
    }
}
