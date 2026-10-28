using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public sealed record CommandSearchProviderDefinition(
    string Type,
    string? Path,
    bool IncludeSubdirectories,
    bool IncludeFiles,
    bool IncludeDirectories,
    int MaxResults,
    IReadOnlyList<string> Aliases);

public enum ResultItemKind
{
    None,
    File,
    Folder,
    Record,
    Url,
    ScriptItem,
    ApiItem
}

public sealed record ResultProviderItem(
    string Id,
    string Title,
    string Subtitle,
    ResultItemKind Kind,
    string? OpenTarget,
    IReadOnlyList<string> Keywords,
    string AccentHex,
    string? ProviderTitle = null);

public sealed class ResultProviderResponse
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<ResultProviderItem> Results { get; init; } = [];
}

public interface IExtensionResultProvider
{
    bool CanHandle(string providerType);

    Task<ResultProviderResponse> SearchAsync(CommandItem command, CommandSearchProviderDefinition provider, string query, CancellationToken cancellationToken = default);
}

public static class ExtensionSearchProviderService
{
    private static readonly IExtensionResultProvider[] Providers =
    [
        new FolderResultProvider(),
        new ScriptResultProvider()
    ];

    public static async Task<ResultProviderResponse> SearchAsync(
        CommandItem command,
        CommandSearchProviderDefinition provider,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (provider == null)
        {
            return new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = "搜索提供器未配置。"
            };
        }

        var handler = Providers.FirstOrDefault(item => item.CanHandle(provider.Type));
        if (handler == null)
        {
            return new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = $"暂不支持的搜索提供器类型：{provider.Type}"
            };
        }

        return await handler.SearchAsync(command, provider, query, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class FolderResultProvider : IExtensionResultProvider
{
    public bool CanHandle(string providerType) => string.Equals(providerType, "folder", StringComparison.OrdinalIgnoreCase);

    public Task<ResultProviderResponse> SearchAsync(CommandItem command, CommandSearchProviderDefinition provider, string query, CancellationToken cancellationToken = default)
    {
        var rootPath = provider.Path?.Trim();
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return Task.FromResult(new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = "文件夹搜索扩展没有配置 path。"
            });
        }

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = $"搜索目录不存在：{rootPath}"
            });
        }

        // 递归遍历可能扫整棵目录树（大目录可达数十秒），绝不能在 UI 线程同步执行
        return Task.Run(() => SearchCore(provider, rootPath, query, cancellationToken), cancellationToken);
    }

    private static ResultProviderResponse SearchCore(CommandSearchProviderDefinition provider, string rootPath, string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = (query ?? string.Empty).Trim();
        var results = new List<ResultProviderItem>();
        var maxResults = Math.Clamp(provider.MaxResults, 1, 512);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0 && results.Count < maxResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
                {
                    var isDirectory = Directory.Exists(entry);
                    if (isDirectory)
                    {
                        if (provider.IncludeSubdirectories)
                        {
                            pendingDirectories.Push(entry);
                        }

                        if (!provider.IncludeDirectories)
                        {
                            continue;
                        }
                    }
                    else if (!provider.IncludeFiles)
                    {
                        continue;
                    }

                    if (normalizedQuery.Length > 0 && !IsMatch(entry, normalizedQuery))
                    {
                        continue;
                    }

                    results.Add(new ResultProviderItem(
                        Id: entry,
                        Title: Path.GetFileName(entry),
                        Subtitle: Path.GetDirectoryName(entry) ?? rootPath,
                        Kind: isDirectory ? ResultItemKind.Folder : ResultItemKind.File,
                        OpenTarget: entry,
                        Keywords: [entry, Path.GetDirectoryName(entry) ?? rootPath, Path.GetFileName(entry)],
                        AccentHex: isDirectory ? "#FF3B82F6" : "#FF4B5563"));

                    if (results.Count >= maxResults)
                    {
                        break;
                    }
                }
            }
            catch (IOException)
            {
                // Skip transient filesystem errors so one bad branch does not abort the whole search.
            }
            catch (UnauthorizedAccessException)
            {
                // Skip protected directories.
            }
        }

        return new ResultProviderResponse
        {
            Success = true,
            Results = results
        };
    }

    private static bool IsMatch(string path, string query)
    {
        var name = Path.GetFileName(path);
        var fullPath = path;
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (!name.Contains(token, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class ScriptResultProvider : IExtensionResultProvider
{
    public bool CanHandle(string providerType) => string.Equals(providerType, "script", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultProviderResponse> SearchAsync(CommandItem command, CommandSearchProviderDefinition provider, string query, CancellationToken cancellationToken = default)
    {
        if (!ScriptExtensionRunner.CanExecute(command))
        {
            return new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = "script provider 需要扩展本身提供 runtime 和脚本入口。"
            };
        }

        var execution = await ScriptExtensionRunner.ExecuteAsync(command, query, "search-provider", cancellationToken).ConfigureAwait(false);
        if (!execution.Success)
        {
            return new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(execution.Error) ? "脚本结果提供器执行失败。" : execution.Error
            };
        }

        try
        {
            var json = execution.Output?.Trim() ?? string.Empty;
            if (json.Length == 0)
            {
                return new ResultProviderResponse
                {
                    Success = true,
                    Results = []
                };
            }

            var options = JsonDefaults.CaseInsensitive;

            if (json.StartsWith('{'))
            {
                var envelope = JsonSerializer.Deserialize<ScriptProviderEnvelope>(json, options);
                return new ResultProviderResponse
                {
                    Success = envelope?.Success ?? true,
                    ErrorMessage = envelope?.ErrorMessage,
                    Results = envelope?.Items?.Select(MapScriptItem).ToList() ?? []
                };
            }

            var items = JsonSerializer.Deserialize<List<ScriptProviderItem>>(json, options) ?? [];
            return new ResultProviderResponse
            {
                Success = true,
                Results = items.Select(MapScriptItem).ToList()
            };
        }
        catch (Exception ex)
        {
            return new ResultProviderResponse
            {
                Success = false,
                ErrorMessage = $"script provider 输出不是有效 JSON：{ex.Message}"
            };
        }
    }

    private static ResultProviderItem MapScriptItem(ScriptProviderItem item)
    {
        var kind = ParseResultItemKind(item.Kind);
        return new ResultProviderItem(
            Id: string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id!,
            Title: item.Title ?? "未命名结果",
            Subtitle: item.Subtitle ?? string.Empty,
            Kind: kind,
            OpenTarget: item.OpenTarget,
            Keywords: item.Keywords ?? [],
            AccentHex: string.IsNullOrWhiteSpace(item.AccentHex) ? DefaultAccentHex(kind) : item.AccentHex!,
            ProviderTitle: item.ProviderTitle);
    }

    private static ResultItemKind ParseResultItemKind(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "file" => ResultItemKind.File,
            "folder" => ResultItemKind.Folder,
            "url" => ResultItemKind.Url,
            "script" or "script-item" => ResultItemKind.ScriptItem,
            "api" or "api-item" => ResultItemKind.ApiItem,
            "record" => ResultItemKind.Record,
            _ => ResultItemKind.Record
        };
    }

    private static string DefaultAccentHex(ResultItemKind kind) => kind switch
    {
        ResultItemKind.File => "#FF4B5563",
        ResultItemKind.Folder => "#FF3B82F6",
        ResultItemKind.Url => "#FF06B6D4",
        ResultItemKind.ApiItem => "#FF10B981",
        ResultItemKind.ScriptItem => "#FFF59E0B",
        _ => "#FF64748B"
    };

    private sealed class ScriptProviderEnvelope
    {
        public bool Success { get; init; } = true;
        public string? ErrorMessage { get; init; }
        public List<ScriptProviderItem>? Items { get; init; }
    }

    private sealed class ScriptProviderItem
    {
        public string? Id { get; init; }
        public string? Title { get; init; }
        public string? Subtitle { get; init; }
        public string? Kind { get; init; }
        public string? OpenTarget { get; init; }
        public string[]? Keywords { get; init; }
        public string? AccentHex { get; init; }
        public string? ProviderTitle { get; init; }
    }
}
