using System.Text.Json;
using WpfApplication = System.Windows.Application;

namespace OpenQuickHost;

public static class YanziBrowserCapabilityProvider
{
    private const int MaxSteps = 50;

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "browser.status",
            Description = "查询燕子浏览器助手是否启用、是否连接，以及当前连接的浏览器",
            Permissions = ["browser.read"],
            Category = "browser",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "browser.scrape",
            Description = "在浏览器中打开指定网页并按 CSS Selector 提取结构化内容",
            Permissions = ["browser.read", "network.read"],
            Category = "browser",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "url":{"type":"string","minLength":1},
                "selectors":{"type":"object"},
                "closeOnComplete":{"type":"boolean"},
                "timeoutSeconds":{"type":"integer","minimum":5,"maximum":120}
              },
              "required":["url","selectors"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ScrapeAsync
        };

        yield return new()
        {
            Name = "browser.autofill",
            Description = "在网页中按 CSS Selector 填写字段，并可点击一个提交元素",
            Permissions = ["browser.read", "browser.write", "network.read"],
            Category = "browser",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "url":{"type":"string","minLength":1},
                "fields":{"type":"array","items":{
                  "type":"object",
                  "properties":{
                    "selector":{"type":"string","minLength":1},
                    "value":{"type":"string"}
                  },
                  "required":["selector","value"],
                  "additionalProperties":false
                }},
                "clickSelector":{"type":"string"},
                "closeOnComplete":{"type":"boolean"},
                "timeoutSeconds":{"type":"integer","minimum":5,"maximum":120}
              },
              "required":["url","fields"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = AutofillAsync
        };

        yield return new()
        {
            Name = "browser.workflow",
            Description = "执行声明式网页工作流；仅允许 wait/fill/click/scroll/scrape 原子步骤，不执行任意 JavaScript",
            Permissions = ["browser.read", "browser.write", "network.read"],
            Category = "browser",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "url":{"type":"string","minLength":1},
                "steps":{"type":"array","items":{"type":"object"}},
                "closeOnComplete":{"type":"boolean"},
                "timeoutSeconds":{"type":"integer","minimum":5,"maximum":120}
              },
              "required":["url","steps"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = WorkflowAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var settings = AppSettingsStore.LoadCached();
        var server = GetServer();
        return Task.FromResult<object?>(new
        {
            enabled = settings.EnableBrowserHelper,
            connected = server?.IsBrowserConnected == true,
            browser = server?.ConnectedBrowserName ?? "",
            localApiListening = server?.IsListening == true,
            provider = "yanzi-browser-extension",
            supportedWorkflowSteps = new[] { "wait", "fill", "click", "scroll", "scrape" }
        });
    }

    private static async Task<object?> ScrapeAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var url = RequireHttpUrl(input.GetProperty("url").GetString()!);
        var selectors = input.GetProperty("selectors");
        if (selectors.ValueKind != JsonValueKind.Object || !selectors.EnumerateObject().Any())
            throw new ArgumentException("selectors 至少需要一个字段。");

        foreach (var property in selectors.EnumerateObject())
            ValidateSelectorConfig(property.Name, property.Value);

        return await ExecuteBrowserTaskAsync(input, new Dictionary<string, object?>
        {
            ["action"] = "scrape",
            ["url"] = url,
            ["selectors"] = selectors.Clone(),
            ["closeOnComplete"] = GetCloseOnComplete(input)
        });
    }

    private static async Task<object?> AutofillAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var url = RequireHttpUrl(input.GetProperty("url").GetString()!);
        var fields = input.GetProperty("fields");
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() == 0)
            throw new ArgumentException("fields 至少需要一个输入项。");
        if (fields.GetArrayLength() > 50)
            throw new ArgumentException("fields 最多支持 50 项。");

        foreach (var field in fields.EnumerateArray())
        {
            var selector = field.GetProperty("selector").GetString();
            ValidateSelector(selector, "field.selector");
            var value = field.GetProperty("value").GetString() ?? "";
            if (value.Length > 20000)
                throw new ArgumentException("单个输入值不能超过 20000 字符。");
        }

        string? clickSelector = null;
        if (input.TryGetProperty("clickSelector", out var clickElement) &&
            clickElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(clickElement.GetString()))
        {
            clickSelector = clickElement.GetString()!.Trim();
            ValidateSelector(clickSelector, "clickSelector");
        }

        var task = new Dictionary<string, object?>
        {
            ["action"] = "autofill",
            ["url"] = url,
            ["fields"] = fields.Clone(),
            ["closeOnComplete"] = GetCloseOnComplete(input)
        };
        if (clickSelector != null)
            task["clickSelector"] = clickSelector;

        return await ExecuteBrowserTaskAsync(input, task);
    }

    private static async Task<object?> WorkflowAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var url = RequireHttpUrl(input.GetProperty("url").GetString()!);
        var steps = input.GetProperty("steps");
        if (steps.ValueKind != JsonValueKind.Array ||
            steps.GetArrayLength() == 0 ||
            steps.GetArrayLength() > MaxSteps)
            throw new ArgumentException($"steps 必须包含 1-{MaxSteps} 个步骤。");

        var index = 0;
        foreach (var step in steps.EnumerateArray())
        {
            index++;
            ValidateWorkflowStep(step, index);
        }

        return await ExecuteBrowserTaskAsync(input, new Dictionary<string, object?>
        {
            ["action"] = "workflow",
            ["url"] = url,
            ["steps"] = steps.Clone(),
            ["closeOnComplete"] = GetCloseOnComplete(input)
        });
    }

    private static async Task<object?> ExecuteBrowserTaskAsync(
        JsonElement input,
        Dictionary<string, object?> task)
    {
        var server = GetServer()
            ?? throw new InvalidOperationException("燕子 Local Agent API 尚未启动。");
        if (!server.IsBrowserConnected)
            throw new InvalidOperationException("燕子浏览器助手未连接。请先打开 Edge/Chrome 并启用燕子浏览器助手。");

        var timeoutSeconds = input.TryGetProperty("timeoutSeconds", out var timeout) &&
                             timeout.TryGetInt32(out var timeoutValue)
            ? timeoutValue
            : 30;

        var result = await server.RunBrowserTaskAsync(task, timeoutSeconds);
        if (!result.success)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.message)
                    ? $"浏览器任务失败：{result.status}"
                    : result.message);

        return new
        {
            success = true,
            result.taskId,
            result.status,
            result.message,
            data = result.data
        };
    }

    private static LocalAgentApiServer? GetServer()
        => System.Windows.Application.Current is App app ? app.AgentApiServer : null;

    private static string RequireHttpUrl(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("url 仅支持 http/https 绝对地址。");

        return uri.AbsoluteUri;
    }

    private static bool GetCloseOnComplete(JsonElement input)
        => !input.TryGetProperty("closeOnComplete", out var value) ||
           value.ValueKind != JsonValueKind.False;

    private static void ValidateWorkflowStep(JsonElement step, int index)
    {
        if (step.ValueKind != JsonValueKind.Object ||
            !step.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"步骤 {index} 缺少 type。");

        var type = typeElement.GetString()?.Trim().ToLowerInvariant();
        if (type is not ("wait" or "fill" or "click" or "scroll" or "scrape"))
            throw new ArgumentException($"步骤 {index} 不支持类型：{type}");

        switch (type)
        {
            case "wait":
                if (step.TryGetProperty("selector", out var waitSelector) &&
                    waitSelector.ValueKind == JsonValueKind.String)
                    ValidateSelector(waitSelector.GetString(), $"步骤 {index}.selector");
                if (step.TryGetProperty("timeout", out var timeout) &&
                    (!timeout.TryGetInt32(out var timeoutValue) || timeoutValue < 0 || timeoutValue > 30000))
                    throw new ArgumentException($"步骤 {index}.timeout 必须在 0-30000ms。");
                break;

            case "fill":
                ValidateRequiredSelector(step, index);
                if (!step.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                    throw new ArgumentException($"步骤 {index} fill 缺少字符串 value。");
                if ((value.GetString() ?? "").Length > 20000)
                    throw new ArgumentException($"步骤 {index}.value 不能超过 20000 字符。");
                break;

            case "click":
                ValidateRequiredSelector(step, index);
                break;

            case "scroll":
                if (step.TryGetProperty("distance", out var distance) &&
                    (!distance.TryGetInt32(out var distanceValue) || Math.Abs((long)distanceValue) > 100000))
                    throw new ArgumentException($"步骤 {index}.distance 超出允许范围。");
                break;

            case "scrape":
                if (!step.TryGetProperty("selectors", out var selectors) ||
                    selectors.ValueKind != JsonValueKind.Object ||
                    !selectors.EnumerateObject().Any())
                    throw new ArgumentException($"步骤 {index} scrape 缺少 selectors。");
                foreach (var property in selectors.EnumerateObject())
                    ValidateSelectorConfig(property.Name, property.Value);
                break;
        }
    }

    private static void ValidateRequiredSelector(JsonElement step, int index)
    {
        if (!step.TryGetProperty("selector", out var selector) ||
            selector.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"步骤 {index} 缺少 selector。");
        ValidateSelector(selector.GetString(), $"步骤 {index}.selector");
    }

    private static void ValidateSelectorConfig(string key, JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 120)
            throw new ArgumentException("selectors 字段名无效。");

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString() ?? "";
            var selector = raw.Contains('|') ? raw.Split('|', 2)[0].Trim() : raw.Trim();
            ValidateSelector(selector, $"selectors.{key}");
            return;
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("selector", out var selectorValue) &&
            selectorValue.ValueKind == JsonValueKind.String)
        {
            ValidateSelector(selectorValue.GetString(), $"selectors.{key}.selector");
            return;
        }

        throw new ArgumentException($"selectors.{key} 必须是 selector 字符串或 selector 配置对象。");
    }

    private static void ValidateSelector(string? selector, string name)
    {
        if (string.IsNullOrWhiteSpace(selector) || selector.Length > 2000)
            throw new ArgumentException($"{name} 无效。");
        if (selector.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException($"{name} 包含非法字符。");
    }
}
