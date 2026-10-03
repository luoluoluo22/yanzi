using System.Net;
using System.Text.Json;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // Called only after the shared local Agent authentication check.
    private static async Task<bool> TryHandleCapabilityApiAsync(HttpListenerRequest request,
        HttpListenerResponse response, string path)
    {
        if (request.HttpMethod == "GET" && path == "/v1/agent/catalog")
        {
            response.Headers["Cache-Control"] = "no-store";
            await WriteJsonAsync(response, 200, YanziAgentCapabilityCatalog.Create());
            return true;
        }
        if (request.HttpMethod == "GET" && path == "/v1/agent/openapi.json")
        {
            await WriteJsonAsync(response, 200, YanziAgentCapabilityCatalog.OpenApi(request.Url!.GetLeftPart(UriPartial.Authority)));
            return true;
        }
        if (request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal)
            && path.EndsWith("/capabilities", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path["/v1/extensions/".Length..^"/capabilities".Length]);
            var command = OpenQuickHost.Sync.LocalExtensionCatalog.LoadCommands().FirstOrDefault(x =>
                string.Equals(x.ExtensionId, id, StringComparison.OrdinalIgnoreCase));
            response.Headers["Cache-Control"] = "no-store";
            await WriteJsonAsync(response, command == null ? 404 : 200,
                command == null ? (object)new { error = "extension_not_found" }
                    : new { extensionId = id, capabilities = YanziAgentCapabilityCatalog.ForExtension(command) });
            return true;
        }
        if (request.HttpMethod == "GET" && path == "/v1/capabilities")
        {
            await WriteJsonAsync(response, 200, new { capabilities = YanziCapabilityQueryService.ListCapabilities() });
            return true;
        }
        if (request.HttpMethod == "GET" && path == "/v1/capabilities/calls")
        {
            await WriteJsonAsync(response, 200, new { calls = YanziCapabilityCallLog.List() });
            return true;
        }
        if (request.HttpMethod == "GET" && path.StartsWith("/v1/capabilities/", StringComparison.Ordinal))
        {
            var name = Uri.UnescapeDataString(path["/v1/capabilities/".Length..]);
            var descriptor = YanziCapabilityQueryService.Describe(name);
            await WriteJsonAsync(response, descriptor == null ? 404 : 200,
                descriptor ?? (object)new { error = "capability_not_found" });
            return true;
        }
        if (request.HttpMethod == "POST" && path == "/v1/capabilities/invoke")
        {
            var body = await ReadJsonBodyAsync(request);
            var name = GetString(body, "name") ?? string.Empty;
            object? payload = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("payload", out var value)
                ? value.Clone() : null;
            // Token authentication is the authority. Never trust caller/permissions fields supplied in JSON.
            var result = await YanziCapabilityInvocationService.InvokeAsync(name, payload, YanziCapabilityCaller.LocalAgent);
            var status = result.Success ? 200 : result.ErrorCode switch
            {
                "capability_not_found" => 404, "permission_denied" => 403,
                "invalid_parameters" => 400, _ => 500
            };
            await WriteJsonAsync(response, status, result);
            return true;
        }
        return false;
    }
}
