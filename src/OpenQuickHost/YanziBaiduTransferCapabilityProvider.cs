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
    private static readonly SemaphoreSlim UploadGate = new(1, 1);
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.transferStatus",
            Description = "只读查询百度网盘上传/下载的进行中进度、完成和失败记录；来源为本机客户端任务数据库，不是实时云端对象查询",
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

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.searchExactVisible",
            Description = "通过已登录百度网盘桌面客户端全局搜索文件名，只确认当前已渲染搜索页的精确匹配；不保证全云盘唯一，也不返回未经验证的云端 fid",
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = ["application.run", "file.read", "network.read"],
            RiskLevel = "low",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "filename":{"type":"string","minLength":3},
                "waitSeconds":{"type":"integer","minimum":2,"maximum":20}
              },
              "required":["filename"],
              "additionalProperties":false
            }
            """),
            OutputSchema = ObjectSchema,
            Handler = input =>
            {
                var json = (JsonElement)input!;
                var filename = json.GetProperty("filename").GetString()
                    ?? throw new ArgumentException("缺少搜索文件名");
                var waitSeconds = json.TryGetProperty("waitSeconds", out var wait)
                    && wait.TryGetInt32(out var seconds) ? seconds : 12;
                if (waitSeconds is < 2 or > 20)
                    throw new ArgumentOutOfRangeException(nameof(waitSeconds));
                return InvokeLocalBridgeAsync(new
                {
                    operation = "searchExact", filename, waitSeconds
                });
            }
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.downloadByName",
            Description = "仅凭精确文件名在官方百度客户端搜索并下载单个可见匹配文件，返回本次客户端历史的云端路径/大小/SHA-256；云端全局唯一性不保证",
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = ["application.run", "file.read", "file.write", "network.read"],
            RiskLevel = "medium",
            RequiresConfirmation = true,
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "filename":{"type":"string","minLength":3},
                "expectedSha256":{"type":"string","minLength":64},
                "copyToFolder":{"type":"string","minLength":1},
                "timeoutSeconds":{"type":"integer","minimum":15,"maximum":300},
                "confirm":{"type":"boolean","enum":[true]}
              },
              "required":["filename","confirm"],
              "additionalProperties":false
            }
            """),
            OutputSchema = ObjectSchema,
            Handler = input => DownloadByNameAsync((JsonElement)input!)
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.downloadExactVerified",
            Description = "百度官方客户端全网盘精确搜索、选中单一文件并下载，凭本次下载历史云端路径/大小和可选可信 SHA-256 校验；显式确认",
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = ["application.run", "file.read", "file.write", "network.read"],
            RiskLevel = "medium",
            RequiresConfirmation = true,
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "filename":{"type":"string","minLength":3},
                "expectedCloudPath":{"type":"string","minLength":2},
                "expectedSize":{"type":"integer","minimum":0},
                "expectedSha256":{"type":"string","minLength":64},
                "copyToFolder":{"type":"string","minLength":1},
                "timeoutSeconds":{"type":"integer","minimum":15,"maximum":300},
                "confirm":{"type":"boolean","enum":[true]}
              },
              "required":["filename","expectedCloudPath","expectedSize","confirm"],
              "additionalProperties":false
            }
            """),
            OutputSchema = ObjectSchema,
            Handler = input => DownloadVerifiedAsync((JsonElement)input!)
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "baiduNetdisk.uploadVerified",
            Description = "调用百度网盘官方上传入口并等待本机客户端的成功完成记录；仅客户端历史验证，非实时云端 API 校验",
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = ["application.run", "application.read", "file.read", "network.write"],
            RiskLevel = "medium",
            RequiresConfirmation = true,
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "confirm":{"type":"boolean","enum":[true]},
                "timeoutSeconds":{"type":"integer","minimum":10,"maximum":240}
              },
              "required":["path","confirm"],
              "additionalProperties":false
            }
            """),
            OutputSchema = ObjectSchema,
            Handler = payload => UploadVerifiedAsync((JsonElement)payload!)
        };
    }

    private static async Task<object?> DownloadByNameAsync(JsonElement input)
    {
        if (!input.TryGetProperty("confirm", out var approved)
            || approved.ValueKind != JsonValueKind.True)
            throw new UnauthorizedAccessException("百度按文件名下载需要明确 confirm=true");
        var filename = input.GetProperty("filename").GetString();
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("必须提供准确的文件名");
        var expectedSha256 = input.TryGetProperty("expectedSha256", out var hash)
            && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
        var copyToFolder = input.TryGetProperty("copyToFolder", out var dest)
            && dest.ValueKind == JsonValueKind.String ? dest.GetString() : null;
        var timeout = input.TryGetProperty("timeoutSeconds", out var time)
            && time.TryGetInt32(out var n) ? n : 90;
        if (timeout is < 15 or > 300)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!await DownloadGate.WaitAsync(0))
            throw new InvalidOperationException("百度下载任务正在执行，拒绝并发");
        try
        {
            return await InvokeLocalBridgeAsync(new
            {
                operation = "downloadByName", filename, expectedSha256,
                copyToFolder, timeoutSeconds = timeout, confirm = true
            }, timeout + 25);
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static async Task<object?> DownloadVerifiedAsync(JsonElement input)
    {
        if (!input.TryGetProperty("confirm", out var confirmation)
            || confirmation.ValueKind != JsonValueKind.True)
            throw new UnauthorizedAccessException("百度网盘自动下载必须明确 confirm=true");
        var filename = input.GetProperty("filename").GetString();
        var cloudPath = input.GetProperty("expectedCloudPath").GetString();
        if (string.IsNullOrWhiteSpace(filename) || string.IsNullOrWhiteSpace(cloudPath))
            throw new ArgumentException("必须提供精确文件名与可信云端绝对路径");
        if (!input.GetProperty("expectedSize").TryGetInt64(out var expectedSize)
            || expectedSize is < 0 or > 2147483648L)
            throw new ArgumentOutOfRangeException("expectedSize");
        var expectedSha256 = input.TryGetProperty("expectedSha256", out var hash)
            && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
        var copyToFolder = input.TryGetProperty("copyToFolder", out var folder)
            && folder.ValueKind == JsonValueKind.String ? folder.GetString() : null;
        var timeout = input.TryGetProperty("timeoutSeconds", out var time)
            && time.TryGetInt32(out var parsed) ? parsed : 90;
        if (timeout is < 15 or > 300)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        if (!await DownloadGate.WaitAsync(0))
            throw new InvalidOperationException("已有百度网盘自动下载任务在执行，拒绝并发操作");
        try
        {
            return await InvokeLocalBridgeAsync(new
            {
                operation = "downloadExact",
                filename,
                expectedCloudPath = cloudPath,
                expectedSize,
                expectedSha256,
                copyToFolder,
                timeoutSeconds = timeout,
                confirm = true
            }, timeout + 25);
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static async Task<object?> UploadVerifiedAsync(JsonElement input)
    {
        if (!input.TryGetProperty("confirm", out var confirmation)
            || confirmation.ValueKind != JsonValueKind.True)
            throw new UnauthorizedAccessException("百度网盘上传必须明确提供 confirm=true");
        var rawPath = input.GetProperty("path").GetString();
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new ArgumentException("缺少上传文件路径");
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath));
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 2L * 1024 * 1024 * 1024)
            throw new FileNotFoundException("上传文件必须存在且不超过实验能力 2GiB 限制", path);
        var timeoutSeconds = input.TryGetProperty("timeoutSeconds", out var wait)
            && wait.TryGetInt32(out var seconds) ? seconds : 90;
        if (timeoutSeconds is < 10 or > 240)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

        if (!await UploadGate.WaitAsync(0))
            throw new InvalidOperationException("百度上传验收已有任务运行，拒绝重复提交");
        try
        {
            var expectedBytes = file.Length;
            var startSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // This uses the existing official Windows Shell upload verb. Never
            // access Baidu credentials or send private HTTP requests.
            await YanziCapabilityRegistry.InvokeAsync(
                "baiduNetdisk.upload", new { path }, YanziCapabilityCaller.LocalAgent);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(900);
                var result = (JsonElement)(await InvokeLocalBridgeAsync(new
                {
                    operation = "transferStatus",
                    kind = "upload",
                    path,
                    afterSeconds = startSeconds
                }))!;
                if (!result.GetProperty("found").GetBoolean()) continue;
                if (result.TryGetProperty("failed", out var failed)
                    && failed.ValueKind == JsonValueKind.True)
                {
                    return new
                    {
                        status = "client_failed", triggered = true,
                        clientCompleted = false, clientFailed = true,
                        errorCode = result.GetProperty("errorCode").GetInt32(),
                        size = expectedBytes,
                        message = "百度客户端报告上传失败；不自动重新提交，可检查客户端任务详情。",
                        liveCloudExistenceVerified = false
                    };
                }
                if (!result.GetProperty("completed").GetBoolean()) continue;
                if (result.GetProperty("size").GetInt64() != expectedBytes)
                    throw new InvalidOperationException("百度已完成记录的文件大小与上传前不一致");
                file.Refresh();
                if (!file.Exists || file.Length != expectedBytes)
                    throw new InvalidOperationException("原始文件在上传期间发生变化");
                return new
                {
                    status = "client_completed", triggered = true, clientCompleted = true,
                    cloudPath = result.GetProperty("cloudPath").GetString(),
                    size = expectedBytes, finishedAt = result.GetProperty("finishedAt").GetInt64(),
                    evidence = "baidu-desktop-transfer-history",
                    liveCloudExistenceVerified = false
                };
            }
            // A previous Shell DoIt success cannot be represented as a completed
            // upload. Caller should query transferStatus rather than resubmit.
            return new
            {
                status = "pending_unconfirmed", triggered = true,
                clientCompleted = false,
                size = expectedBytes,
                message = "上传已交给百度客户端，但未在规定时间内观察到完成记录；先查询状态，不要重复上传。",
                liveCloudExistenceVerified = false
            };
        }
        finally
        {
            UploadGate.Release();
        }
    }

    private static async Task<object?> InvokeLocalBridgeAsync(object payload, int maxSeconds = 25)
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

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(maxSeconds));
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
