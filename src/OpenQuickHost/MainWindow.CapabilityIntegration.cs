using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace OpenQuickHost;

public partial class MainWindow
{
    public bool IsCapabilityScopeActive =>
        string.Equals(_activeFilterScopeKey, SearchScopeCapability, StringComparison.OrdinalIgnoreCase);

    private static string CapabilityDocsUrl(int port) =>
        $"http://127.0.0.1:{port}/docs#capability-console";

    private void OpenCapabilityDocs_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.LoadCached();
        if (!settings.EnableAgentApi)
        {
            LastRunMessage = "本地 Agent API 尚未启用，请先在燕子设置中启用。";
            System.Windows.MessageBox.Show(this, LastRunMessage, "能力接入文档",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CapabilityDocsUrl(settings.AgentApiPort),
                UseShellExecute = true
            });
            LastRunMessage = "已打开能力接入文档；请在浏览器内完成本地 API 身份验证。";
        }
        catch (Exception ex)
        {
            LastRunMessage = $"打开能力接入文档失败：{ex.Message}";
        }
    }

    // Generate example values only for required fields; placeholders are intentionally
    // non-operational, and callers must check the schema and permissions before invocation.
    private static object? ExampleCapabilityValue(JsonElement schema, string name = "value", int depth = 0)
    {
        if (depth > 5 || schema.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>();

        if (schema.TryGetProperty("enum", out var enums) &&
            enums.ValueKind == JsonValueKind.Array && enums.GetArrayLength() > 0)
            return enums[0].Clone();

        var type = schema.TryGetProperty("type", out var typeElement) &&
                   typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() : null;

        if (type == "object" || schema.TryGetProperty("properties", out _))
        {
            var result = new Dictionary<string, object?>();
            if (!schema.TryGetProperty("properties", out var props) ||
                props.ValueKind != JsonValueKind.Object)
                return result;
            var required = schema.TryGetProperty("required", out var req) &&
                           req.ValueKind == JsonValueKind.Array
                ? req.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string?>(StringComparer.Ordinal);
            foreach (var property in props.EnumerateObject())
                if (required.Contains(property.Name))
                    result[property.Name] = ExampleCapabilityValue(property.Value, property.Name, depth + 1);
            return result;
        }

        return type switch
        {
            "string" => $"<填写{name}>",
            "integer" or "number" => 0,
            "boolean" => false,
            "array" => Array.Empty<object>(),
            "null" => null,
            _ => new Dictionary<string, object?>()
        };
    }

    private static string BuildCapabilityIntegrationText(CapabilityCatalogEntry entry, int port)
    {
        var cap = entry.Descriptor;
        var baseUrl = $"http://127.0.0.1:{port}";
        var example = JsonSerializer.Serialize(new
        {
            name = cap.Name,
            payload = ExampleCapabilityValue(cap.InputSchema)
        }, new JsonSerializerOptions { WriteIndented = true });
        // Single-quoted PowerShell literals preserve JSON quotes and Unicode. Escape apostrophes.
        var escapedBody = example.Replace("'", "''", StringComparison.Ordinal);
        var sb = new StringBuilder();

        sb.AppendLine("燕子能力接入说明")
            .AppendLine($"能力名称：{cap.Name}")
            .AppendLine($"能力说明：{cap.Description}")
            .AppendLine($"提供者：{entry.ProviderTitle} ({cap.ProviderExtensionId})")
            .AppendLine($"状态：{entry.Status}；可调用：{cap.Available}")
            .AppendLine($"权限：{(cap.Permissions.Count == 0 ? "无" : string.Join(", ", cap.Permissions))}")
            .AppendLine($"风险：{cap.RiskLevel}；需要确认：{cap.RequiresConfirmation}")
            .AppendLine($"本机服务地址：{baseUrl}")
            .AppendLine($"网页接入文档：{CapabilityDocsUrl(port)}")
            .AppendLine($"能力发现：GET {baseUrl}/v1/agent/catalog")
            .AppendLine($"OpenAPI 文档：GET {baseUrl}/v1/agent/openapi.json")
            .AppendLine($"能力调用：POST {baseUrl}/v1/capabilities/invoke")
            .AppendLine("认证方式：Authorization: Bearer <本地Agent API Token> 或 X-Yanzi-Token。")
            .AppendLine("Token 从燕子设置获取；不要将真实 Token 粘贴给他人或提交到仓库。")
            .AppendLine("请求格式：Content-Type: application/json; charset=utf-8，Body={\"name\":\"能力名\",\"payload\":{...}}。")
            .AppendLine("PowerShell 示例（必填参数为占位值，必须根据下方 Schema 修改；高风险能力请先确认）：")
            .AppendLine($"$baseUrl = '{baseUrl}'")
            .AppendLine("$token = '<在燕子设置中复制的Token>'")
            .AppendLine("$headers = @{ Authorization = \"Bearer $token\" }")
            .AppendLine($"$body = '{escapedBody}'")
            .AppendLine("$result = Invoke-RestMethod -Method Post -Uri \"$baseUrl/v1/capabilities/invoke\" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($body))")
            .AppendLine("$result | ConvertTo-Json -Depth 16")
            .AppendLine("结果：success=true 时读取 data；失败时查看 error/errorCode。常见状态：400 参数有误、401 认证失败、403 权限不足、404 未注册、500 执行失败。");

        if (!cap.Available)
        {
            sb.AppendLine("注意：当前能力未注册为可调用状态，不能直接执行上面的调用请求。");
            if (entry.ProviderCommand != null && cap.OnDemand)
                sb.AppendLine($"在用户授权后启动提供者：POST {baseUrl}/v1/extensions/{Uri.EscapeDataString(cap.ProviderExtensionId)}/run")
                    .AppendLine("请求体：{\"input\":\"\",\"launchSource\":\"app-startup\"}。启动后重新查询目录，确认 available=true 才能调用。");
            else
                sb.AppendLine("请确认提供者已安装、运行并注册 Handler，然后重新读取能力目录。");
        }

        sb.AppendLine("输入 Schema：").AppendLine(SchemaText(cap.InputSchema))
            .AppendLine("输出 Schema：").AppendLine(SchemaText(cap.OutputSchema))
            .AppendLine($"能力清单：{entry.ManifestPath ?? "(宿主内置，无单独小程序 manifest)"}")
            .AppendLine("还可以打开网页控制台登录后筛选能力、填写参数并测试调用。");
        return sb.ToString();
    }
}
