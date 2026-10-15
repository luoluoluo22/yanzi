using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziJianyingCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "jianying.status",
            Description = "查询剪映专业版安装路径、版本、运行状态和本机草稿目录",
            Permissions = ["application.read", "file.read"],
            Category = "jianying",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "jianying.open",
            Description = "启动当前实际安装版本的剪映专业版，不依赖可能失效的开始菜单快捷方式目标",
            Permissions = ["application.run"],
            Category = "jianying",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };

        yield return new()
        {
            Name = "jianying.drafts.list",
            Description = "列出本机剪映专业版草稿，返回名称、时长、fps、画布、轨道数、版本和更新时间",
            Permissions = ["file.read"],
            Category = "jianying",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ListDraftsAsync
        };

        yield return new()
        {
            Name = "jianying.draft.inspect",
            Description = "读取指定剪映草稿的结构化摘要，包括轨道和主要素材类别数量",
            Permissions = ["file.read"],
            Category = "jianying",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"draft":{"type":"string","minLength":1}},
              "required":["draft"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = InspectDraftAsync
        };

        yield return new()
        {
            Name = "jianying.draft.openFolder",
            Description = "在资源管理器中打开指定剪映草稿目录",
            Permissions = ["application.run", "file.read"],
            Category = "jianying",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"draft":{"type":"string","minLength":1}},
              "required":["draft"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenDraftFolderAsync
        };

        yield return new()
        {
            Name = "jianying.draft.backup",
            Description = "将指定剪映草稿完整压缩备份为 zip；用于 AI 自动修改前的安全快照",
            Permissions = ["file.read", "file.write"],
            Category = "jianying",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "draft":{"type":"string","minLength":1},
                "outputPath":{"type":"string","minLength":1},
                "overwrite":{"type":"boolean"}
              },
              "required":["draft","outputPath"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = BackupDraftAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var exe = ResolveExecutable();
        var draftRoot = ResolveDraftRoot();
        var info = exe == null ? null : FileVersionInfo.GetVersionInfo(exe);
        var processes = Process.GetProcessesByName("JianyingPro");
        return Task.FromResult<object?>(new
        {
            installed = exe != null,
            executable = exe,
            version = info?.FileVersion,
            running = processes.Length > 0,
            processIds = processes.Select(p => p.Id).ToArray(),
            draftRoot,
            draftCount = draftRoot == null
                ? 0
                : Directory.EnumerateDirectories(draftRoot).Select(TryReadSummary).Count(summary => summary != null)
        });
    }

    private static Task<object?> OpenAsync(object? _)
    {
        var exe = ResolveExecutable() ?? throw new FileNotFoundException("未找到剪映专业版。");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        }) ?? throw new InvalidOperationException("剪映专业版启动失败。");

        return Task.FromResult<object?>(new
        {
            opened = true,
            executable = exe,
            processId = process.Id
        });
    }

    private static Task<object?> ListDraftsAsync(object? _)
    {
        var root = ResolveDraftRoot();
        if (root == null)
            return Task.FromResult<object?>(new { count = 0, items = Array.Empty<object>() });

        var items = Directory.EnumerateDirectories(root)
            .Select(TryReadSummary)
            .Where(summary => summary != null)
            .OrderByDescending(summary => summary!.UpdatedAtUtc)
            .ToArray();

        return Task.FromResult<object?>(new
        {
            count = items.Length,
            draftRoot = root,
            items
        });
    }

    private static Task<object?> InspectDraftAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var directory = ResolveDraft(input.GetProperty("draft").GetString()!);
        var infoPath = Path.Combine(directory, "draft_info.json");
        if (!File.Exists(infoPath))
            throw new FileNotFoundException("草稿缺少 draft_info.json。", infoPath);

        using var document = JsonDocument.Parse(File.ReadAllText(infoPath));
        var root = document.RootElement;

        var materialCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("materials", out var materials) && materials.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in materials.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                    materialCounts[property.Name] = property.Value.GetArrayLength();
            }
        }

        var tracks = root.TryGetProperty("tracks", out var tracksElement) && tracksElement.ValueKind == JsonValueKind.Array
            ? tracksElement.EnumerateArray().Select((track, index) => new
            {
                index,
                type = GetString(track, "type"),
                id = GetString(track, "id"),
                segmentCount = track.TryGetProperty("segments", out var segments) && segments.ValueKind == JsonValueKind.Array
                    ? segments.GetArrayLength()
                    : 0
            }).ToArray()
            : Array.Empty<object>();

        return Task.FromResult<object?>(new
        {
            summary = TryReadSummary(directory),
            tracks,
            materialCounts = materialCounts
                .Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            files = Directory.EnumerateFiles(directory)
                .Select(file => new FileInfo(file))
                .Select(file => new { file.Name, file.Length, file.LastWriteTimeUtc })
                .ToArray()
        });
    }

    private static Task<object?> OpenDraftFolderAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var directory = ResolveDraft(input.GetProperty("draft").GetString()!);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            ArgumentList = { directory }
        });
        return Task.FromResult<object?>(new { opened = true, path = directory });
    }

    private static Task<object?> BackupDraftAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var directory = ResolveDraft(input.GetProperty("draft").GetString()!);
        var output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            input.GetProperty("outputPath").GetString()!.Trim().Trim('"')));
        if (!output.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            output += ".zip";

        var parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("备份路径无父目录。");
        Directory.CreateDirectory(parent);

        var overwrite = input.TryGetProperty("overwrite", out var overwriteElement) && overwriteElement.GetBoolean();
        if (File.Exists(output))
        {
            if (!overwrite)
                throw new IOException("备份文件已存在；需要覆盖时设置 overwrite=true。");
            File.Delete(output);
        }

        ZipFile.CreateFromDirectory(directory, output, CompressionLevel.Optimal, includeBaseDirectory: true);
        return Task.FromResult<object?>(new
        {
            backedUp = File.Exists(output),
            draftPath = directory,
            outputPath = output,
            bytes = new FileInfo(output).Length
        });
    }

    private sealed record DraftSummary(
        string Id,
        string Name,
        string DirectoryName,
        string Path,
        double DurationSeconds,
        int Fps,
        int? CanvasWidth,
        int? CanvasHeight,
        int TrackCount,
        string? Version,
        DateTime UpdatedAtUtc);

    private static DraftSummary? TryReadSummary(string directory)
    {
        try
        {
            var infoPath = Path.Combine(directory, "draft_info.json");
            var metaPath = Path.Combine(directory, "draft_meta_info.json");
            if (!File.Exists(infoPath)) return null;

            using var infoDoc = JsonDocument.Parse(File.ReadAllText(infoPath));
            var info = infoDoc.RootElement;

            string? metaName = null;
            string? metaId = null;
            if (File.Exists(metaPath))
            {
                using var metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath));
                metaName = GetString(metaDoc.RootElement, "draft_name");
                metaId = GetString(metaDoc.RootElement, "draft_id");
            }

            var name = FirstNonEmpty(metaName, GetString(info, "name"), Path.GetFileName(directory));
            var id = FirstNonEmpty(metaId, GetString(info, "id"), Path.GetFileName(directory));
            var duration = info.TryGetProperty("duration", out var durationElement) && durationElement.TryGetInt64(out var micros)
                ? micros / 1_000_000d
                : 0;
            var fps = info.TryGetProperty("fps", out var fpsElement) && fpsElement.TryGetInt32(out var fpsValue)
                ? fpsValue
                : 0;
            int? width = null;
            int? height = null;
            if (info.TryGetProperty("canvas_config", out var canvas) && canvas.ValueKind == JsonValueKind.Object)
            {
                if (canvas.TryGetProperty("width", out var widthElement) && widthElement.TryGetInt32(out var widthValue)) width = widthValue;
                if (canvas.TryGetProperty("height", out var heightElement) && heightElement.TryGetInt32(out var heightValue)) height = heightValue;
            }

            var trackCount = info.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array
                ? tracks.GetArrayLength()
                : 0;

            return new DraftSummary(
                id,
                name,
                Path.GetFileName(directory),
                directory,
                Math.Round(duration, 3),
                fps,
                width,
                height,
                trackCount,
                GetString(info, "new_version") ?? GetString(info, "version"),
                Directory.GetLastWriteTimeUtc(directory));
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveDraft(string value)
    {
        var root = ResolveDraftRoot() ?? throw new DirectoryNotFoundException("未找到剪映草稿目录。");
        var candidate = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(root, value));

        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(candidate))
            throw new DirectoryNotFoundException("草稿不存在，或路径不在剪映草稿根目录内。");

        return candidate;
    }

    private static string? ResolveDraftRoot()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JianyingPro", "User Data", "Projects", "com.lveditor.draft");
        return Directory.Exists(path) ? path : null;
    }

    private static string? ResolveExecutable()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JianyingPro");
        if (!Directory.Exists(root)) return null;

        return Directory.EnumerateFiles(root, "JianyingPro.exe", SearchOption.AllDirectories)
            .OrderByDescending(path => ParseVersion(FileVersionInfo.GetVersionInfo(path).FileVersion))
            .FirstOrDefault();
    }

    private static Version ParseVersion(string? value)
    {
        var numeric = new string((value ?? "0").TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).TrimEnd('.');
        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0);
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.First(value => !string.IsNullOrWhiteSpace(value))!;
}
