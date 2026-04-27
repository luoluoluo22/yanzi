using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed class LocalAgentApiServer : IDisposable
{
    private readonly string _prefix;
    private readonly string _token;
    private readonly Action _onMutated;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public LocalAgentApiServer(string prefix, string token, Action onMutated)
    {
        _prefix = prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
        _token = token;
        _onMutated = onMutated;
        _listener.Prefixes.Add(_prefix);
    }

    public void Start()
    {
        _listener.Start();
        _loopTask = Task.Run(ListenLoopAsync);
        HostAssets.AppendLog($"Local Agent API started at {_prefix}");
    }

    public void Stop()
    {
        _cts.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore graceful shutdown errors.
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
            }
            catch (HttpListenerException)
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Local Agent API listen error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Yanzi-Token";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";

        try
        {
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (!IsAuthorized(request))
            {
                await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                return;
            }

            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            if (request.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonAsync(response, 200, new { ok = true, service = "yanzi-local-agent-api" });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/extensions/template")
            {
                await WriteJsonAsync(response, 200, new { template = LocalExtensionCatalog.CreateTemplateJson() });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/extensions")
            {
                var items = LocalExtensionCatalog.LoadCommands().Select(ToDto).ToList();
                await WriteJsonAsync(response, 200, new { items });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/sync/webdav-config")
            {
                await WriteJsonAsync(response, 200, GetWebDavConfigDto());
                return;
            }

            if (request.HttpMethod == "GET" && path.StartsWith("/v1/storage/", StringComparison.Ordinal))
            {
                var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..]);
                var key = GetQueryString(request, "key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    await WriteJsonAsync(response, 400, new { error = "key_required" });
                    return;
                }

                var scope = GetQueryString(request, "scope");
                var result = await ExtensionStorageService.ReadTextAsync(extensionId, key, scope);
                await WriteJsonAsync(response, 200, new
                {
                    found = result.Found,
                    content = result.Content,
                    source = result.Source,
                    localPath = result.LocalPath
                });
                return;
            }

            if (request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
                var manifest = LocalExtensionCatalog.LoadManifestJson(id);
                await WriteJsonAsync(response, 200, new { id, manifest });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/extensions")
            {
                var payload = await ReadJsonBodyAsync(request);
                var manifest = GetString(payload, "manifest");
                if (string.IsNullOrWhiteSpace(manifest))
                {
                    await WriteJsonAsync(response, 400, new { error = "manifest_required" });
                    return;
                }

                var command = LocalExtensionCatalog.SaveJsonExtension(manifest);
                _onMutated();
                await WriteJsonAsync(response, 201, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "PUT" && path.StartsWith("/v1/storage/", StringComparison.Ordinal))
            {
                var extensionId = Uri.UnescapeDataString(path["/v1/storage/".Length..]);
                var payload = await ReadJsonBodyAsync(request);
                var key = GetString(payload, "key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    await WriteJsonAsync(response, 400, new { error = "key_required" });
                    return;
                }

                var content = GetString(payload, "content") ?? string.Empty;
                var scope = GetString(payload, "scope");
                var result = await ExtensionStorageService.WriteTextAsync(extensionId, key, content, scope);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    localPath = result.LocalPath,
                    cloudSaved = result.CloudSaved,
                    result.Scope,
                    cloudMessage = result.CloudMessage
                });
                return;
            }

            if (request.HttpMethod == "PUT" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
                var payload = await ReadJsonBodyAsync(request);
                var manifest = GetString(payload, "manifest");
                if (string.IsNullOrWhiteSpace(manifest))
                {
                    await WriteJsonAsync(response, 400, new { error = "manifest_required" });
                    return;
                }

                using var document = JsonDocument.Parse(manifest);
                if (!document.RootElement.TryGetProperty("id", out var idElement) ||
                    !string.Equals(idElement.GetString(), id, StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(response, 400, new { error = "id_mismatch" });
                    return;
                }

                var command = LocalExtensionCatalog.SaveJsonExtension(manifest);
                _onMutated();
                await WriteJsonAsync(response, 200, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "PATCH" && path.EndsWith("/rename", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/rename".Length]);
                var payload = await ReadJsonBodyAsync(request);
                var name = GetString(payload, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    await WriteJsonAsync(response, 400, new { error = "name_required" });
                    return;
                }

                var command = LocalExtensionCatalog.RenameExtension(id, name);
                _onMutated();
                await WriteJsonAsync(response, 200, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "PATCH" && path.EndsWith("/shortcut", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/shortcut".Length]);
                var payload = await ReadJsonBodyAsync(request);
                var shortcut = GetString(payload, "shortcut");
                var command = LocalExtensionCatalog.SetGlobalShortcut(id, shortcut);
                _onMutated();
                await WriteJsonAsync(response, 200, new { item = ToDto(command) });
                return;
            }

            if (request.HttpMethod == "DELETE" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..]);
                LocalExtensionCatalog.DeleteExtension(id);
                _onMutated();
                await WriteJsonAsync(response, 200, new { ok = true, id });
                return;
            }

            await WriteJsonAsync(response, 404, new { error = "not_found" });
        }
        catch (FileNotFoundException ex)
        {
            await WriteJsonAsync(response, 404, new { error = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            await WriteJsonAsync(response, 404, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            await WriteJsonAsync(response, 400, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Local Agent API request error: {ex.Message}");
            await WriteJsonAsync(response, 500, new { error = "internal_error", detail = ex.Message });
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            return true;
        }

        var bearer = request.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(bearer) &&
            bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(bearer["Bearer ".Length..].Trim(), _token, StringComparison.Ordinal))
        {
            return true;
        }

        var incoming = request.Headers["X-Yanzi-Token"];
        return string.Equals(incoming, _token, StringComparison.Ordinal);
    }

    private static WebDavConfigDto GetWebDavConfigDto()
    {
        var settings = AppSettingsStore.Load();
        var credential = WebDavCredentialStore.Load();

        return new WebDavConfigDto
        {
            Enabled = settings.EnableWebDavSync,
            ServerUrl = settings.WebDavServerUrl,
            RootPath = settings.WebDavRootPath,
            Username = settings.WebDavUsername,
            Password = string.IsNullOrWhiteSpace(credential?.Password) ? null : credential.Password
        };
    }

    private static object ToDto(CommandItem x)
    {
        return new
        {
            id = x.ExtensionId,
            title = x.Title,
            subtitle = x.Subtitle,
            category = x.Category,
            source = x.Source.ToString(),
            version = x.DeclaredVersion,
            globalShortcut = x.GlobalShortcut,
            runtime = x.Runtime,
            entry = x.EntryPoint,
            permissions = x.Permissions
        };
    }

    private static async Task<JsonElement> ReadJsonBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? GetQueryString(HttpListenerRequest request, string key)
    {
        return request.QueryString[key];
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        response.StatusCode = statusCode;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
