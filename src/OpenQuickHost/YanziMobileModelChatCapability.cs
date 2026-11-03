using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Same-account mobile chat uses a deliberately narrow, authenticated desktop
/// capability. No provider URL or API key can be supplied by the phone.
/// </summary>
internal static class YanziMobileModelChatCapability
{
    internal const string Name = "ai.chat.complete";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(105) };

    internal static void Register() =>
        YanziCapabilityRegistry.Register(Name, CompleteAsync, providerExtensionId: "host",
            description: "Run chat on a desktop-owned model service without exporting credentials.");

    private static async Task<object?> CompleteAsync(object? input)
    {
        if (input is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("invalid_chat_payload");
        if (!root.TryGetProperty("model", out var requested) ||
            requested.ValueKind != JsonValueKind.String)
            throw new ArgumentException("model_required");
        var model = requested.GetString()?.Trim() ?? "";
        if (model.Length is < 3 or > 240 || !model.Contains('/'))
            throw new ArgumentException("invalid_model_id");

        var settings = AppSettingsStore.Load();
        if (!settings.EnableAgentApi || settings.AgentApiPort is < 1 or > 65535)
            throw new InvalidOperationException("desktop_model_gateway_disabled");

        var provider = (settings.AiServiceProviders ?? []).FirstOrDefault(p =>
            p.IsEnabled && model.StartsWith(p.Id + "/", StringComparison.OrdinalIgnoreCase) &&
            (p.Models ?? []).Contains(model[(p.Id.Length + 1)..], StringComparer.OrdinalIgnoreCase));
        if (provider == null)
            throw new InvalidOperationException("model_not_available_on_desktop");

        if (!root.TryGetProperty("messages", out var incoming) ||
            incoming.ValueKind != JsonValueKind.Array ||
            incoming.GetArrayLength() is < 1 or > 48)
            throw new ArgumentException("invalid_messages");

        var messages = new List<object>();
        var length = 0;
        foreach (var item in incoming.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("role", out var roleElement) ||
                !item.TryGetProperty("content", out var contentElement) ||
                roleElement.ValueKind != JsonValueKind.String ||
                contentElement.ValueKind != JsonValueKind.String)
                throw new ArgumentException("text_chat_only");
            var role = roleElement.GetString() ?? "";
            var content = contentElement.GetString() ?? "";
            if (role is not ("system" or "user" or "assistant") ||
                content.Length > 20000)
                throw new ArgumentException("invalid_chat_message");
            length += content.Length;
            if (length > 32000)
                throw new ArgumentException("chat_context_too_large");
            messages.Add(new { role, content });
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"http://127.0.0.1:{settings.AgentApiPort}/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", LocalLlmApiTokenStore.LoadOrCreate());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model, messages, stream = false }),
            Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            HostAssets.AppendLog($"[MobileAI] Model gateway HTTP {(int)response.StatusCode}: provider={provider.Id}");
            throw new InvalidOperationException($"desktop_model_http_{(int)response.StatusCode}");
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var choice = json.RootElement.GetProperty("choices")[0].GetProperty("message");
        var answer = choice.GetProperty("content").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("empty_model_response");

        return new { content = answer.Length > 12000 ? answer[..12000] : answer, model };
    }
}
