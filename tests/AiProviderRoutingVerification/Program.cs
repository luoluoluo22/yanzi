using System.Reflection;
using OpenQuickHost;

var resolver = typeof(MainWindow).GetMethod("ResolveAiRequestProfile",
    BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Active provider request resolver missing");

object? Profile(AppSettings settings) => resolver.Invoke(null, [settings]);
string Field(object profile, string name) => profile.GetType().GetProperty(name)!.GetValue(profile)?.ToString() ?? "";
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

var reverse = new AiServiceProviderSettings
{
    Id = "reverse-gpt",
    Name = "逆向 GPT",
    ProviderType = "OpenAI",
    IsEnabled = true,
    BaseUrl = "http://127.0.0.1:18444/v1",
    ApiKey = "unit-test-key",
    Models = ["gemini-3.8-flash", "gpt-4o"],
    SelectedModel = "gemini-3.8-flash"
};
var other = new AiServiceProviderSettings
{
    Id = "second",
    Name = "Other",
    IsEnabled = true,
    BaseUrl = "https://other.example/v1",
    ApiKey = "second-key",
    Models = ["other-model"],
    SelectedModel = "other-model"
};
var settings = new AppSettings
{
    AiServiceProviders = [reverse, other],
    ActiveServiceProviderId = reverse.Id,
    AiBaseUrl = "https://obsolete.example/v1",
    AiApiKey = "stale-legacy-key",
    AiModel = "obsolete-model"
};
var selected = Profile(settings)!;
Check(Field(selected, "BaseUrl") == reverse.BaseUrl, "active provider URL overrides stale legacy settings");
Check(Field(selected, "Model") == reverse.SelectedModel, "active provider model overrides stale legacy settings");
Check(Field(selected, "ApiKey") == reverse.ApiKey, "active provider credential overrides stale legacy settings");
Check(Field(selected, "IsChatGptPlan") == "False", "generic reverse API must not use special ChatGPT plan parameters");
settings.ActiveServiceProviderId = other.Id;
selected = Profile(settings)!;
Check(Field(selected, "BaseUrl") == other.BaseUrl &&
      Field(selected, "Model") == other.SelectedModel, "switching active provider updates the request");
other.IsEnabled = false;
Check(Profile(settings) is null, "disabled active provider cannot send through stale legacy fallback");
settings.ActiveServiceProviderId = "deleted";
Check(Profile(settings) is null, "deleted active provider cannot use stale legacy fallback");
settings.ActiveServiceProviderId = "";
Check(Profile(settings) is null, "multiple enabled providers without explicit selection cannot silently pick a channel");
settings.AiServiceProviders = [];
Check(Profile(settings) is not null, "old single-provider settings remain backward-compatible");
settings.ActiveServiceProviderId = reverse.Id;
Check(Profile(settings) is null, "deleted active id never silently uses legacy credentials");
settings.AiServiceProviders = [reverse];
settings.ActiveServiceProviderId = reverse.Id;
reverse.ApiKey = "";
Check(Profile(settings) is null, "incomplete active provider blocks request");
reverse.ApiKey = "unit-test-key";
reverse.SelectedModel = "";
reverse.Models = ["fallback-model"];
Check(Field(Profile(settings)!, "Model") == "fallback-model", "provider model list fallback is used");
Console.WriteLine("AI_PROVIDER_ROUTING_VERIFIED");
