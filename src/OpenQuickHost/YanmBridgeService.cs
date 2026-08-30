using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenQuickHost;

public sealed class YanmBridgeService
{
    private static readonly HttpClient WebFetchClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private readonly Func<IReadOnlyList<CommandItem>> _getAllCommands;
    private readonly Func<string, YanmComponentSettings?> _findCurrentComponent;
    private readonly Func<string, string, string> _getComponentState;
    private readonly Action<string, string> _sendComponentState;
    private readonly Action<string> _sendSystemInfo;
    private readonly Action<string, string, string> _queueComponentStateSave;
    private readonly Action<string, string, bool, object?, string?> _sendReply;
    private readonly Action<CommandItem, string?, string> _executeCommandExternally;
    private readonly Action<string> _log;

    public YanmBridgeService(
        Func<IReadOnlyList<CommandItem>> getAllCommands,
        Func<string, YanmComponentSettings?> findCurrentComponent,
        Func<string, string, string> getComponentState,
        Action<string, string> sendComponentState,
        Action<string> sendSystemInfo,
        Action<string, string, string> queueComponentStateSave,
        Action<string, string, bool, object?, string?> sendReply,
        Action<CommandItem, string?, string> executeCommandExternally,
        Action<string> log)
    {
        _getAllCommands = getAllCommands;
        _findCurrentComponent = findCurrentComponent;
        _getComponentState = getComponentState;
        _sendComponentState = sendComponentState;
        _sendSystemInfo = sendSystemInfo;
        _queueComponentStateSave = queueComponentStateSave;
        _sendReply = sendReply;
        _executeCommandExternally = executeCommandExternally;
        _log = log;
    }

    public void HandleInvoke(string componentId, JsonElement root)
    {
        var component = _findCurrentComponent(componentId);
        if (component == null)
        {
            _log($"Yanm: component invoke ignored because component is missing, id={componentId}.");
            return;
        }

        var invokeId = GetString(root, "id");
        var method = GetString(root, "method");
        var args = root.TryGetProperty("args", out var argsProperty) ? argsProperty.Clone() : default;

        // web.fetch 是同步 HTTP（超时可达 8 秒），必须离开 UI 线程执行，否则组件一发起
        // 网络请求整个应用就冻结；其余能力都是本地快速操作，保持原同步路径。
        // 注意 args 必须 Clone：调用方的 JsonDocument 离开 using 后即销毁。
        if (string.Equals(method, "web.fetch", StringComparison.Ordinal))
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            var componentTitle = component.Title;
            _ = Task.Run(() =>
            {
                try
                {
                    var result = Dispatch(method, componentId, component, args);
                    SendReplyOnUi(dispatcher, componentId, invokeId, true, result, null);
                }
                catch (Exception ex)
                {
                    _log($"Yanm: invoke failed, component={componentTitle}, method={method}, error={ex.Message}");
                    SendReplyOnUi(dispatcher, componentId, invokeId, false, null, ex.Message);
                }
            });
            return;
        }

        try
        {
            var result = Dispatch(method, componentId, component, args);
            _sendReply(componentId, invokeId, true, result, null);
        }
        catch (Exception ex)
        {
            _log($"Yanm: invoke failed, component={component.Title}, method={method}, error={ex.Message}");
            _sendReply(componentId, invokeId, false, null, ex.Message);
        }
    }

    private void SendReplyOnUi(System.Windows.Threading.Dispatcher? dispatcher, string componentId, string invokeId, bool ok, object? result, string? error)
    {
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _sendReply(componentId, invokeId, ok, result, error);
            }
            catch (Exception ex)
            {
                _log($"Yanm: send reply failed, component={componentId}, invoke={invokeId}, error={ex.Message}");
            }
        }));
    }

    private object? Dispatch(string method, string componentId, YanmComponentSettings component, JsonElement args)
    {
        return method switch
        {
            "system.info" => BuildSystemInfoResult(),
            "state.get" => BuildStateGetResult(componentId, args),
            "state.set" => BuildStateSetResult(componentId, args),
            "clipboard.read" => ClipboardService.GetText() ?? string.Empty,
            "clipboard.write" => BuildClipboardWriteResult(args),
            "desktop.list" => BuildDesktopListResult(),
            "command.execute" => BuildCommandExecuteResult(args),
            "command.list" => BuildCommandListResult(args),
            "path.open" => BuildPathOpenResult(args),
            "path.downloads" => BuildDownloadsPathResult(),
            "file.read" => BuildFileReadResult(args),
            "file.write" => BuildFileWriteResult(args),
            "file.delete" => BuildFileDeleteResult(args),
            "file.exists" => BuildFileExistsResult(args),
            "file.list" => BuildFileListResult(args),
            "file.copy" => BuildFileCopyResult(args),
            "file.move" => BuildFileMoveResult(args),
            "web.fetch" => BuildWebFetchResult(args),
            _ => throw new InvalidOperationException($"未知能力：{method}")
        };
    }

    private object BuildSystemInfoResult()
    {
        var memory = GetMemoryStatus();
        return new
        {
            cpuCores = Environment.ProcessorCount,
            isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable(),
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            time = DateTime.Now.ToString("HH:mm"),
            date = DateTime.Now.ToString("yyyy-MM-dd"),
            totalMemoryMb = memory.totalMb,
            availableMemoryMb = memory.availableMb,
            usedMemoryPercent = memory.usedPercent
        };
    }

    private object BuildStateGetResult(string componentId, JsonElement args)
    {
        var key = GetArgString(args, "key");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("state.get 缺少 key。");
        }

        var value = _getComponentState(componentId, key);
        _sendComponentState(componentId, key);
        return new { key, value };
    }

    private object BuildStateSetResult(string componentId, JsonElement args)
    {
        var key = GetArgString(args, "key");
        var value = GetArgString(args, "value");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("state.set 缺少 key。");
        }

        _queueComponentStateSave(componentId, key, value);
        return new { key, value };
    }

    private object BuildClipboardWriteResult(JsonElement args)
    {
        var text = GetArgString(args, "text");
        ClipboardService.SetText(text);
        return new { ok = true, length = text.Length };
    }

    private object BuildDesktopListResult()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var items = Directory.Exists(desktop)
            ? Directory.EnumerateFileSystemEntries(desktop)
                .Take(200)
                .Select(path => new
                {
                    name = Path.GetFileName(path),
                    path,
                    isDirectory = Directory.Exists(path),
                    modifiedTime = File.Exists(path) ? File.GetLastWriteTime(path) : Directory.GetLastWriteTime(path)
                })
                .ToList()
            : [];

        return new { root = desktop, items };
    }

    private object BuildCommandExecuteResult(JsonElement args)
    {
        var targetId = GetArgString(args, "extensionId");
        if (string.IsNullOrWhiteSpace(targetId))
        {
            targetId = GetArgString(args, "commandId");
        }

        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException("command.execute 缺少 extensionId 或 commandId。");
        }

        var input = GetArgString(args, "input");
        var launchSource = string.IsNullOrWhiteSpace(GetArgString(args, "launchSource"))
            ? "yanm"
            : GetArgString(args, "launchSource");

        var command = _getAllCommands().FirstOrDefault(item =>
            item.ExtensionId.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            throw new InvalidOperationException($"未找到命令：{targetId}");
        }

        _executeCommandExternally(command, input, launchSource);
        return new { executed = true, extensionId = command.ExtensionId, title = command.Title };
    }

    private object BuildCommandListResult(JsonElement args)
    {
        var query = GetArgString(args, "query");
        var source = GetArgString(args, "source");
        var limit = Math.Clamp(GetArgInt(args, "limit", 120), 1, 500);
        var items = string.IsNullOrWhiteSpace(query)
            ? _getAllCommands()
            : _getAllCommands().Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.ExtensionId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        items = FilterCommandsBySource(items, source)
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        return new
        {
            items = items.Select(item => new
            {
                extensionId = item.ExtensionId,
                title = item.Title,
                subtitle = item.Subtitle,
                category = item.Category,
                icon = item.IconReference,
                iconDataUrl = BuildIconDataUrl(item.IconSource),
                hasHostedView = item.HasHostedView,
                hasScriptEntry = item.HasScriptEntry,
                uiMode = item.UiMode,
                runtime = item.Runtime
            }).ToList()
        };
    }

    private static IReadOnlyList<CommandItem> FilterCommandsBySource(IReadOnlyList<CommandItem> items, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return items;
        }

        return source.Trim().ToLowerInvariant() switch
        {
            "application" or "app" => items.Where(static item => item.Source == CommandSource.Application).ToList(),
            "extension" => items.Where(static item => item.Source != CommandSource.Application).ToList(),
            _ => items
        };
    }

    private object BuildPathOpenResult(JsonElement args)
    {
        var path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("path.open 缺少 path。");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });

        return new { opened = true, path };
    }

    private object BuildDownloadsPathResult()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var fallback = !Directory.Exists(downloads);
        var path = fallback ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : downloads;
        return new
        {
            path,
            fallbackUsed = fallback,
            exists = Directory.Exists(path)
        };
    }

    private object BuildFileReadResult(JsonElement args)
    {
        var path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file.read 缺少 path。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("文件不存在。", path);
        }

        var binary = GetArgBool(args, "binary");
        if (binary)
        {
            var bytes = File.ReadAllBytes(path);
            return new
            {
                path,
                binary = true,
                length = bytes.Length,
                contentBase64 = Convert.ToBase64String(bytes)
            };
        }

        var encoding = GetEncoding(args);
        var text = File.ReadAllText(path, encoding);
        return new
        {
            path,
            binary = false,
            encoding = encoding.WebName,
            length = text.Length,
            text
        };
    }

    private object BuildFileWriteResult(JsonElement args)
    {
        var path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file.write 缺少 path。");
        }

        var binary = GetArgBool(args, "binary");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        if (binary)
        {
            var contentBase64 = GetArgString(args, "contentBase64");
            if (string.IsNullOrWhiteSpace(contentBase64))
            {
                throw new InvalidOperationException("file.write(binary) 缺少 contentBase64。");
            }

            var bytes = Convert.FromBase64String(contentBase64);
            File.WriteAllBytes(path, bytes);
            return new { path, binary = true, bytesWritten = bytes.Length };
        }

        var text = GetArgString(args, "text");
        var encoding = GetEncoding(args);
        File.WriteAllText(path, text, encoding);
        return new { path, binary = false, encoding = encoding.WebName, bytesWritten = encoding.GetByteCount(text) };
    }

    private object BuildFileDeleteResult(JsonElement args)
    {
        var path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file.delete 缺少 path。");
        }

        var recursive = GetArgBool(args, "recursive");
        if (File.Exists(path))
        {
            File.Delete(path);
            return new { deleted = true, path, kind = "file" };
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
            return new { deleted = true, path, kind = "directory", recursive };
        }

        return new { deleted = false, path, reason = "not_found" };
    }

    private object BuildFileExistsResult(JsonElement args)
    {
        var path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file.exists 缺少 path。");
        }

        return new
        {
            path,
            exists = File.Exists(path) || Directory.Exists(path),
            isFile = File.Exists(path),
            isDirectory = Directory.Exists(path)
        };
    }

    private object BuildFileListResult(JsonElement args)
    {
        var path = GetArgString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("file.list 缺少 path。");
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        var recursive = GetArgBool(args, "recursive");
        var limit = Math.Clamp(GetArgInt(args, "limit", 200), 1, 1000);
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var items = Directory.EnumerateFileSystemEntries(path, "*", searchOption)
            .Take(limit)
            .Select(entry => new
            {
                name = Path.GetFileName(entry),
                path = entry,
                isDirectory = Directory.Exists(entry),
                size = File.Exists(entry) ? new FileInfo(entry).Length : 0L,
                modifiedTime = File.Exists(entry) ? File.GetLastWriteTime(entry) : Directory.GetLastWriteTime(entry)
            })
            .ToList();

        return new { path, recursive, items };
    }

    private object BuildFileCopyResult(JsonElement args)
    {
        var source = GetArgString(args, "source");
        var destination = GetArgString(args, "destination");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            throw new InvalidOperationException("file.copy 缺少 source 或 destination。");
        }

        var overwrite = GetArgBool(args, "overwrite");
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".");
            File.Copy(source, destination, overwrite);
            return new { copied = true, source, destination, kind = "file", overwrite };
        }

        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination, overwrite);
            return new { copied = true, source, destination, kind = "directory", overwrite };
        }

        throw new FileNotFoundException("源路径不存在。", source);
    }

    private object BuildFileMoveResult(JsonElement args)
    {
        var source = GetArgString(args, "source");
        var destination = GetArgString(args, "destination");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            throw new InvalidOperationException("file.move 缺少 source 或 destination。");
        }

        var overwrite = GetArgBool(args, "overwrite");
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".");
            if (overwrite && File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
            return new { moved = true, source, destination, kind = "file", overwrite };
        }

        if (Directory.Exists(source))
        {
            if (overwrite && Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            Directory.Move(source, destination);
            return new { moved = true, source, destination, kind = "directory", overwrite };
        }

        throw new FileNotFoundException("源路径不存在。", source);
    }

    private object BuildWebFetchResult(JsonElement args)
    {
        var urlText = GetArgString(args, "url");
        if (string.IsNullOrWhiteSpace(urlText) ||
            !Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("web.fetch 只支持 http/https URL。");
        }

        var maxChars = Math.Clamp(GetArgInt(args, "maxChars", 1200), 200, 20000);
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("OpenQuickHost-Yanm/1.0");
        using var response = WebFetchClient.Send(request);
        var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        stopwatch.Stop();

        if (html.Length > maxChars)
        {
            html = html[..maxChars];
        }

        var title = ExtractHtmlTitle(html);
        var snippet = ExtractHtmlSnippet(html, maxChars);
        return new
        {
            ok = response.IsSuccessStatusCode,
            url = url.ToString(),
            finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url.ToString(),
            status = (int)response.StatusCode,
            title,
            snippet,
            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snippet))),
            checkedAt = DateTime.UtcNow.ToString("O"),
            elapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private static string GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string GetArgString(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetArgBool(JsonElement args, string name, bool defaultValue = false)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var property))
        {
            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }

        return defaultValue;
    }

    private static int GetArgInt(JsonElement args, string name, int defaultValue = 0)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var property))
        {
            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt32(out var parsed) => parsed,
                JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
                _ => defaultValue
            };
        }

        return defaultValue;
    }

    private static Encoding GetEncoding(JsonElement args)
    {
        var name = GetArgString(args, "encoding");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static string ExtractHtmlTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var match = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : string.Empty;
    }

    private static string ExtractHtmlSnippet(string html, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = Regex.Replace(html, "<script[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<style[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length > maxChars ? text[..maxChars] : text;
    }

    private static string BuildIconDataUrl(ImageSource? imageSource)
    {
        if (imageSource is not BitmapSource bitmap)
        {
            return string.Empty;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var target = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destinationDirectory);
            File.Copy(file, target, overwrite);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }
    }

    private static (ulong totalMb, ulong availableMb, double usedPercent) GetMemoryStatus()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>();
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return (0, 0, 0);
        }

        var totalMb = status.ullTotalPhys / 1024 / 1024;
        var availableMb = status.ullAvailPhys / 1024 / 1024;
        var usedPercent = Math.Round((double)(status.ullTotalPhys - status.ullAvailPhys) / status.ullTotalPhys * 100, 1);
        return (totalMb, availableMb, usedPercent);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
