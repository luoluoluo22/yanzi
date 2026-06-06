using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenQuickHost.CSharpRuntime;

public sealed record YanziActionContext(
    string ExtensionId,
    string Title,
    string ExtensionDirectory,
    string ExtensionDataDirectory,
    string InputText,
    string LaunchSource,
    DateTimeOffset Now,
    IReadOnlyList<string> Permissions,
    IReadOnlyDictionary<string, string> State,
    string AgentApiBaseUrl,
    string AgentApiToken)
{
    private YanziStorageClient? _storage;
    private readonly Dictionary<string, string> _pendingStateUpdates = new(StringComparer.OrdinalIgnoreCase);
    private HostedViewStateProxy? _viewState;

    public YanziStorageClient Storage => _storage ??= new YanziStorageClient(this);
    public HostedViewStateProxy ViewState => _viewState ??= new HostedViewStateProxy(this);
    public string StateUpdatePath { get; set; } = Environment.GetEnvironmentVariable("YANZI_STATE_UPDATES_PATH") ?? string.Empty;
    public Action<string, object>? RegisterObject { get; set; }

    public async Task SetStateAsync(object values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var property in values.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            _pendingStateUpdates[property.Name] = property.GetValue(values)?.ToString() ?? string.Empty;
        }

        await FlushStateUpdatesAsync();
    }

    public async Task SetStateAsync(IReadOnlyDictionary<string, string> values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            _pendingStateUpdates[pair.Key] = pair.Value ?? string.Empty;
        }

        await FlushStateUpdatesAsync();
    }

    public static async Task<YanziActionContext> LoadFromEnvironmentAsync()
    {
        var contextPath = Environment.GetEnvironmentVariable("YANZI_CONTEXT_PATH");
        if (string.IsNullOrWhiteSpace(contextPath) || !File.Exists(contextPath))
        {
            throw new InvalidOperationException("YANZI_CONTEXT_PATH is missing.");
        }

        var json = await File.ReadAllTextAsync(contextPath);
        return JsonSerializer.Deserialize<YanziActionContext>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to read Yanzi context.");
    }

    private async Task FlushStateUpdatesAsync()
    {
        var stateUpdatePath = string.IsNullOrWhiteSpace(StateUpdatePath)
            ? Environment.GetEnvironmentVariable("YANZI_STATE_UPDATES_PATH")
            : StateUpdatePath;
        if (string.IsNullOrWhiteSpace(stateUpdatePath))
        {
            return;
        }

        await File.WriteAllTextAsync(stateUpdatePath, JsonSerializer.Serialize(_pendingStateUpdates));
    }

    public Task UpdateView()
    {
        return FlushStateUpdatesAsync();
    }

    public sealed class HostedViewStateProxy
    {
        private readonly YanziActionContext _context;

        public HostedViewStateProxy(YanziActionContext context)
        {
            _context = context;
        }

        public object? this[string key]
        {
            get
            {
                if (_context._pendingStateUpdates.TryGetValue(key, out var pending))
                {
                    return pending;
                }

                return _context.State.TryGetValue(key, out var value) ? value : null;
            }
            set
            {
                _context._pendingStateUpdates[key] = value?.ToString() ?? string.Empty;
            }
        }

        public bool TryGetValue(string key, out object? value)
        {
            if (_context._pendingStateUpdates.TryGetValue(key, out var pending))
            {
                value = pending;
                return true;
            }

            if (_context.State.TryGetValue(key, out var existing))
            {
                value = existing;
                return true;
            }

            value = null;
            return false;
        }
    }
}

public sealed class YanziStorageClient
{
    private readonly YanziActionContext _context;
    private readonly SemaphoreSlim _cloudWriteGate = new(1, 1);

    public YanziStorageClient(YanziActionContext context)
    {
        _context = context;
    }

    public async Task<string?> ReadTextAsync(string key, string scope = "local")
    {
        var normalizedScope = NormalizeScope(scope);
        if (string.Equals(normalizedScope, "local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_context.AgentApiBaseUrl))
        {
            return await ReadLocalTextAsync(key);
        }

        if (string.Equals(normalizedScope, "both", StringComparison.OrdinalIgnoreCase))
        {
            var localText = await ReadLocalTextAsync(key);
            _ = RefreshLocalFromCloudAsync(key);
            return localText;
        }

        return await ReadCloudTextAsync(key, normalizedScope);
    }

    private async Task<string?> ReadCloudTextAsync(string key, string scope)
    {
        using var client = CreateClient();
        var response = await client.GetAsync($"/v1/storage/{Uri.EscapeDataString(_context.ExtensionId)}?key={Uri.EscapeDataString(key)}&scope={Uri.EscapeDataString(scope)}");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<StorageReadResponse>();
        return payload?.Content;
    }

    public async Task WriteTextAsync(string key, string content, string scope = "local")
    {
        var normalizedScope = NormalizeScope(scope);
        if (string.Equals(normalizedScope, "local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_context.AgentApiBaseUrl))
        {
            await WriteLocalTextAsync(key, content);
            return;
        }

        if (string.Equals(normalizedScope, "both", StringComparison.OrdinalIgnoreCase))
        {
            await WriteLocalTextAsync(key, content);
            _ = TryWriteCloudTextAsync(key, content ?? string.Empty);
            return;
        }

        await WriteCloudTextAsync(key, content ?? string.Empty);
    }

    private async Task WriteCloudTextAsync(string key, string content)
    {
        using var client = CreateClient();
        using var response = await client.PutAsJsonAsync(
            $"/v1/storage/{Uri.EscapeDataString(_context.ExtensionId)}",
            new StorageWriteRequest(key, content ?? string.Empty, "cloud"));
        response.EnsureSuccessStatusCode();
    }

    private async Task<string?> ReadLocalTextAsync(string key)
    {
        var path = ResolveLocalPath(key);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    private async Task WriteLocalTextAsync(string key, string? content)
    {
        var path = ResolveLocalPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content ?? string.Empty, Encoding.UTF8);
    }

    private async Task RefreshLocalFromCloudAsync(string key)
    {
        try
        {
            var cloudText = await ReadCloudTextAsync(key, "cloud");
            if (cloudText != null)
            {
                await WriteLocalTextAsync(key, cloudText);
            }
        }
        catch
        {
            // Cloud refresh is opportunistic; local-first reads must stay fast.
        }
    }

    private async Task TryWriteCloudTextAsync(string key, string content)
    {
        await _cloudWriteGate.WaitAsync();
        try
        {
            await WriteCloudTextAsync(key, content);
        }
        catch
        {
            // Cloud writes are queued behind the local save for UI responsiveness.
        }
        finally
        {
            _cloudWriteGate.Release();
        }
    }

    public async Task<T?> ReadJsonAsync<T>(string key, string scope = "local")
    {
        var text = await ReadTextAsync(key, scope);
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, SerializerOptions);
    }

    public Task WriteJsonAsync<T>(string key, T value, string scope = "local")
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        return WriteTextAsync(key, json, scope);
    }

    private string ResolveLocalPath(string key)
    {
        var normalized = NormalizeKey(key);
        return Path.Combine(_context.ExtensionDataDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(_context.AgentApiBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(8)
        };

        if (!string.IsNullOrWhiteSpace(_context.AgentApiToken))
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _context.AgentApiToken);
        }

        return client;
    }

    private static string NormalizeScope(string? scope)
    {
        return string.Equals(scope, "cloud", StringComparison.OrdinalIgnoreCase)
            ? "cloud"
            : string.Equals(scope, "both", StringComparison.OrdinalIgnoreCase)
                ? "both"
                : "local";
    }

    private static string NormalizeKey(string key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Storage key is required.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("Storage key cannot contain . or .. segments.");
        }

        return string.Join("/", segments);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed record StorageReadResponse(bool Found, string? Content, string Source, string LocalPath);

    private sealed record StorageWriteRequest(string Key, string Content, string Scope);
}