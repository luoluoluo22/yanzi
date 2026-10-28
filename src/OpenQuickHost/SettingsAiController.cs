using System.Net.Http;
using System.Text.Json;
namespace OpenQuickHost;

internal sealed record AiProviderSnapshot(string ProviderType, string BaseUrl, string ApiKey);
internal static class SettingsAiController
{
    internal static List<string> IncludeSelectedModel(
        IEnumerable<string>? available, string? selectedModel, string? providerSelection)
    {
        var models = (available ?? [])
            .Where(static model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pinned = !string.IsNullOrWhiteSpace(selectedModel) ? selectedModel : providerSelection;
        if (!string.IsNullOrWhiteSpace(pinned) &&
            !models.Contains(pinned, StringComparer.OrdinalIgnoreCase))
        {
            models.Insert(0, pinned);
        }

        return models;
    }

    internal static bool SameModelIds(IEnumerable<string>? left, IEnumerable<string>? right) =>
        (left ?? []).SequenceEqual(right ?? [], StringComparer.OrdinalIgnoreCase);

    internal static async Task<IReadOnlyList<string>> FetchAvailableModelsAsync(AiProviderSnapshot provider)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        using var request = BuildModelListRequest(provider);
        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"服务器返回 {(int)response.StatusCode}：{responseBody}");
        }

        return ParseAvailableModelNames(provider.ProviderType, responseBody);
    }

    internal static HttpRequestMessage BuildModelListRequest(AiProviderSnapshot provider)
    {
        var baseUrl = provider.BaseUrl.Trim().TrimEnd('/');
        var apiKey = provider.ApiKey.Trim();
        var providerType = provider.ProviderType?.Trim() ?? string.Empty;

        if (string.Equals(providerType, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            var separator = baseUrl.Contains('?') ? "&" : "?";
            return new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models{separator}key={Uri.EscapeDataString(apiKey)}");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        if (string.Equals(providerType, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    internal static IReadOnlyList<string> ParseAvailableModelNames(string providerType, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var models = new List<string>();

        if (string.Equals(providerType, "Gemini", StringComparison.OrdinalIgnoreCase) &&
            document.RootElement.TryGetProperty("models", out var geminiModels) &&
            geminiModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in geminiModels.EnumerateArray())
            {
                if (!model.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                {
                    name = name["models/".Length..];
                }

                models.Add(name);
            }
        }
        else if (document.RootElement.TryGetProperty("data", out var dataModels) &&
                 dataModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in dataModels.EnumerateArray())
            {
                if (!model.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                var id = idElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    models.Add(id);
                }
            }
        }

        return models
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

}
