using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Quark desktop transfer operations. Executes ONLY our bundled, tested Windows
/// client adapter. No auth extraction and no direct cloud protocol replay.
/// </summary>
public static class YanziQuarkTransferCapabilityProvider
{
    private static readonly JsonElement ObjectOutput = YanziCapabilitySchema.Parse("""{"type":"object"}""");

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return Definition("quark.cloudDrive.adapterStatus",
            "查询本机夸克桌面传输适配器是否就绪（无需读取账户凭证）",
            """{"type":"object","additionalProperties":false}""",
            ["application.read"], _ => RunAsync("probe", new { }, 15));

        yield return Definition("quark.cloudDrive.transferStatus",
            "只读查询指定本地路径对应的夸克传输完成状态与云端 fid",
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"kind":{"type":"string","enum":["upload","download"]}},"required":["path","kind"],"additionalProperties":false}""",
            ["application.read", "file.read"],
            input => RunAsync("transferStatus", new
            {
                path = Required(input, "path"),
                kind = Required(input, "kind")
            }, 15));

        yield return Definition("quark.cloudDrive.cachedLookup",
            "按精确文件名只读查找夸克历史缓存候选 fid（并非实时云端结果）",
            """{"type":"object","properties":{"filename":{"type":"string","minLength":1},"parentFid":{"type":"string","minLength":32}},"required":["filename"],"additionalProperties":false}""",
            ["application.read", "file.read"],
            input => RunAsync("cachedLookup", new
            {
                filename = Required(input, "filename"),
                parentFid = Optional(input, "parentFid")
            }, 15));

        yield return Definition("quark.cloudDrive.cachedFolder",
            "按父目录 fid 只读列出夸克本地缓存的候选文件（明确标注非实时）",
            """{"type":"object","properties":{"parentFid":{"type":"string","minLength":32}},"required":["parentFid"],"additionalProperties":false}""",
            ["application.read", "file.read"],
            input => RunAsync("cachedFolder", new
            {
                parentFid = Required(input, "parentFid")
            }, 15));

        yield return Definition("quark.cloudDrive.uploadVerified",
            "通过已登录夸克桌面客户端上传指定本地文件，等待 FINISH 并返回云端 fid；要求确认",
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"confirm":{"type":"boolean","enum":[true]},"timeoutSeconds":{"type":"integer","minimum":10,"maximum":240}},"required":["path","confirm"],"additionalProperties":false}""",
            ["application.run", "file.read", "network.write"],
            input => RunAsync("uploadVerified", new
            {
                path = Required(input, "path"),
                confirm = Confirmed(input),
                timeoutSeconds = Timeout(input)
            }, Timeout(input) + 20),
            write: true);

        yield return Definition("quark.cloudDrive.downloadVerified",
            "在夸克当前可见文件夹按可信原始上传文件自动选中并下载，核对 fid/FINISH/SHA-256；要求确认",
            """{"type":"object","properties":{"originalLocalFile":{"type":"string","minLength":1},"targetFolder":{"type":"string","minLength":1},"confirm":{"type":"boolean","enum":[true]},"timeoutSeconds":{"type":"integer","minimum":10,"maximum":240}},"required":["originalLocalFile","targetFolder","confirm"],"additionalProperties":false}""",
            ["application.run", "file.read", "file.write", "network.read"],
            input => RunAsync("downloadVerified", new
            {
                originalLocalFile = Required(input, "originalLocalFile"),
                targetFolder = Required(input, "targetFolder"),
                confirm = Confirmed(input),
                timeoutSeconds = Timeout(input)
            }, Timeout(input) + 20),
            write: true);

        yield return Definition("quark.cloudDrive.globalSearchDownloadVerified",
            "通过夸克桌面客户端搜索整个网盘，按可信文件名定位并下载，核对原上传 fid、下载 FINISH 和 SHA-256；要求确认",
            """{"type":"object","properties":{"originalLocalFile":{"type":"string","minLength":1},"targetFolder":{"type":"string","minLength":1},"confirm":{"type":"boolean","enum":[true]},"timeoutSeconds":{"type":"integer","minimum":10,"maximum":240}},"required":["originalLocalFile","targetFolder","confirm"],"additionalProperties":false}""",
            ["application.run", "file.read", "file.write", "network.read"],
            input => RunAsync("globalSearchDownloadVerified", new
            {
                originalLocalFile = Required(input, "originalLocalFile"),
                targetFolder = Required(input, "targetFolder"),
                confirm = Confirmed(input),
                timeoutSeconds = Timeout(input)
            }, Timeout(input) + 20),
            write: true);
    }

    private static YanziCapabilityProviderDefinition Definition(
        string name, string description, string schema, IReadOnlyList<string> permissions,
        Func<JsonElement, Task<object?>> action, bool write = false)
        => new()
        {
            Name = name,
            Description = description,
            Category = "cloud-drive",
            Version = "0.1.0",
            Permissions = permissions,
            RiskLevel = write ? "medium" : "low",
            RequiresConfirmation = write,
            InputSchema = YanziCapabilitySchema.Parse(schema),
            OutputSchema = ObjectOutput,
            Handler = input => action((JsonElement)input!)
        };

    private static string Required(JsonElement input, string name)
        => input.GetProperty(name).GetString()
           ?? throw new ArgumentException($"{name} 不能为空");

    private static string? Optional(JsonElement input, string name)
        => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
           ? value.GetString() : null;

    // The registry exposes confirmation in metadata, but does not itself present
    // a user consent dialog. Enforce explicit confirmation in the handler too.
    private static bool Confirmed(JsonElement input)
    {
        if (!input.TryGetProperty("confirm", out var value) ||
            value.ValueKind != JsonValueKind.True)
            throw new UnauthorizedAccessException("云盘传输必须明确传 confirm=true");
        return true;
    }

    private static int Timeout(JsonElement input)
    {
        var value = input.TryGetProperty("timeoutSeconds", out var raw) &&
                    raw.ValueKind == JsonValueKind.Number && raw.TryGetInt32(out var parsed)
                    ? parsed : 90;
        if (value < 10 || value > 240)
            throw new ArgumentOutOfRangeException("timeoutSeconds");
        return value;
    }

    private static async Task<object?> RunAsync(string operation, object arguments, int timeout)
    {
        var bundledScript = Path.Combine(
            AppContext.BaseDirectory, "CapabilityScripts", "QuarkDrive", "quark_capability_host.py");
        if (!File.Exists(bundledScript))
            throw new FileNotFoundException("夸克能力辅助文件未随宿主安装，请重新安装燕子。");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = Path.GetDirectoryName(bundledScript)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-I");
        process.StartInfo.ArgumentList.Add(bundledScript);
        if (!process.Start())
            throw new InvalidOperationException("Python 桥接进程启动失败");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        try
        {
            // The Python bridge expects a flat JSON object.
            var map = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments))
                      ?? new Dictionary<string, object?>();
            map["operation"] = operation;
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(map));
            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (stdout.Length > 1024 * 1024)
                throw new InvalidOperationException("夸克桥接响应超出安全上限");
            using var document = JsonDocument.Parse(stdout);
            var response = document.RootElement;
            if (response.GetProperty("ok").GetBoolean())
                return response.GetProperty("result").Clone();
            var code = response.TryGetProperty("code", out var kind) ? kind.GetString() : "unknown";
            throw new InvalidOperationException($"夸克能力失败（{code}）："+
                (response.TryGetProperty("error", out var error) ? error.GetString() : "未知错误"));
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("夸克桌面传输超时，任务可能仍在客户端执行；请先查询状态，勿直接重复上传");
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
