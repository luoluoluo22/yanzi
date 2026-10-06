using System.IO;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class YanziAgentCapabilityCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<YanziAgentCapabilityDescriptor> ForExtension(CommandItem command)
    {
        var declarations = new List<YanziCapabilityDeclaration>();
        var path = Path.Combine(command.ExtensionDirectoryPath ?? string.Empty, "manifest.json");
        if (File.Exists(path))
        {
            try { declarations = JsonSerializer.Deserialize<YanziCapabilityManifest>(File.ReadAllText(path), Options)?.Provides ?? []; }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { HostAssets.AppendLog($"Agent capability catalog: manifest unavailable for {command.ExtensionId}: {ex.Message}"); }
        }
        var result = new Dictionary<string, YanziAgentCapabilityDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)))
        {
            result[declaration.Name.Trim()] = new YanziAgentCapabilityDescriptor(declaration.Name.Trim(),
                declaration.Description, command.ExtensionId, declaration.Version, declaration.InputSchema,
                declaration.OutputSchema, declaration.Permissions ?? [], false, true, declaration.Audience,
                declaration.Category, declaration.RiskLevel, declaration.RequiresConfirmation);
        }
        foreach (var capability in YanziCapabilityRegistry.List().Where(x =>
                     string.Equals(x.ProviderExtensionId, command.ExtensionId, StringComparison.OrdinalIgnoreCase)))
            result[capability.Name] = FromRegistered(capability);
        return result.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static YanziAgentCapabilityDescriptor FromRegistered(YanziCapabilityDescriptor capability)
        => new(capability.Name, capability.Description, capability.ProviderExtensionId, capability.Version,
            capability.InputSchema, capability.OutputSchema, capability.Permissions, true, false, capability.Audience,
            capability.Category, capability.RiskLevel, capability.RequiresConfirmation);

    public static object Create()
    {
        var commands = LocalExtensionCatalog.LoadCommands();
        var running = RunningExtensionRegistry.GetSnapshot().Select(x => x.ExtensionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedIds = commands.Select(x => x.ExtensionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new
        {
            schemaVersion = "1.0", generatedAt = DateTimeOffset.UtcNow,
            invokeEndpoint = "/v1/capabilities/invoke",
            extensions = commands.Select(x => new
            {
                id = x.ExtensionId, title = x.Title, description = x.Subtitle, version = x.DeclaredVersion,
                runtime = x.Runtime, permissions = x.Permissions, isRunning = running.Contains(x.ExtensionId),
                requires = YanziCapabilityRequirementResolver.GetRequirements(x),
                runEndpoint = $"/v1/extensions/{Uri.EscapeDataString(x.ExtensionId)}/run",
                capabilities = ForExtension(x)
            }).ToArray(),
            hostCapabilities = YanziCapabilityRegistry.List().Where(x => !installedIds.Contains(x.ProviderExtensionId))
                .Select(FromRegistered).ToArray()
        };
    }

    public static object OpenApi(string baseUrl)
    {
        object Response(string description) => new { description };
        object Get(string operationId, string description) => new
        {
            operationId, description,
            responses = new Dictionary<string, object> { ["200"] = Response("成功"), ["401"] = Response("需要本地 Agent 认证") }
        };
        return new
        {
            openapi = "3.1.0",
            info = new { title = "Yanzi Agent capability API", version = "1.0", description = "先读取能力目录，再按 inputSchema 调用 available=true 或 onDemand=true 的能力。onDemand 能力会由燕子自动启动对应小程序提供者。" },
            servers = new[] { new { url = baseUrl } },
            security = new object[] { new Dictionary<string, string[]> { ["bearerAuth"] = [] }, new Dictionary<string, string[]> { ["apiToken"] = [] } },
            components = new
            {
                securitySchemes = new Dictionary<string, object>
                {
                    ["bearerAuth"] = new { type = "http", scheme = "bearer" },
                    ["apiToken"] = new { type = "apiKey", name = "X-Yanzi-Token", @in = "header" }
                }
            },
            paths = new Dictionary<string, object>
            {
                ["/v1/agent/catalog"] = new { get = Get("getAgentCapabilityCatalog", "读取已安装小程序、对应能力、输入输出 Schema、权限和当前可用状态") },
                ["/v1/extensions"] = new { get = Get("listInstalledExtensions", "读取已安装小程序及对应声明/注册能力") },
                ["/v1/capabilities"] = new { get = Get("listRegisteredCapabilities", "读取当前实际注册、可调用的能力") },
                ["/v1/extensions/{extensionId}/capabilities"] = new
                {
                    parameters = new[] { new { name = "extensionId", @in = "path", required = true, schema = new { type = "string" } } },
                    get = Get("getExtensionCapabilities", "读取指定小程序的声明能力和可用状态")
                },
                ["/v1/extensions/{extensionId}/run"] = new
                {
                    parameters = new[] { new { name = "extensionId", @in = "path", required = true, schema = new { type = "string" } } },
                    post = new
                    {
                        operationId = "startCapabilityProvider", description = "经用户授权启动能力提供者；随后重新读取目录确认 available。",
                        requestBody = new { content = new Dictionary<string, object>
                        {
                            ["application/json"] = new { schema = new
                            {
                                type = "object", properties = new Dictionary<string, object>
                                {
                                    ["input"] = new { type = "string", @default = "" },
                                    ["launchSource"] = new { type = "string", @default = "app-startup" }
                                }
                            } }
                        } },
                        responses = new Dictionary<string, object> { ["200"] = Response("已提交启动请求；需重新检查目录"), ["401"] = Response("需要认证"), ["404"] = Response("小程序不存在") }
                    }
                },
                ["/v1/capabilities/invoke"] = new
                {
                    post = new
                    {
                        operationId = "invokeCapability", description = "按目录中的 inputSchema 传递 payload；成功返回 success/data，失败返回 error/errorCode。",
                        requestBody = new
                        {
                            required = true,
                            content = new Dictionary<string, object>
                            {
                                ["application/json"] = new { schema = new
                                {
                                    type = "object", required = new[] { "name", "payload" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["name"] = new { type = "string", description = "能力目录中的 name" },
                                        ["payload"] = new { description = "遵循对应 inputSchema 的 JSON 参数" }
                                    }
                                } }
                            }
                        },
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = Response("调用成功"), ["400"] = Response("参数不符合契约"),
                            ["401"] = Response("需要认证"), ["403"] = Response("权限不足"),
                            ["404"] = Response("能力未注册或提供者未运行"), ["500"] = Response("提供者执行失败")
                        }
                    }
                }
            }
        };
    }
}

public sealed record YanziAgentCapabilityDescriptor(string Name, string Description, string ProviderExtensionId,
    string Version, JsonElement InputSchema, JsonElement OutputSchema, IReadOnlyList<string> Permissions, bool Available,
    bool OnDemand = false, string Audience = "user", string Category = "general", string RiskLevel = "low",
    bool RequiresConfirmation = false);
