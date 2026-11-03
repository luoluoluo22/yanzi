using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenQuickHost;

var root = Path.Combine(Path.GetTempPath(), "yanzi-model-registry-" + Guid.NewGuid().ToString("N"));
var agy = Path.Combine(root, "agy-model-service");
var custom = Path.Combine(root, "custom-llm");
Directory.CreateDirectory(agy);
Directory.CreateDirectory(custom);

var registry = Assembly.Load("Yanzi").GetType("OpenQuickHost.ExtensionModelServiceRegistry", true)!;
var reconcile = registry.GetMethod("Reconcile", BindingFlags.Static | BindingFlags.NonPublic)!;
void Refresh(AppSettings s) => reconcile.Invoke(null, [s, root]);

try
{
    File.WriteAllText(Path.Combine(agy, "manifest.json"), """
        {"id":"agy-model-service","modelServices":[{"id":"agy-cli","name":"AGY CLI","providerType":"AGY CLI","baseUrl":"http://127.0.0.1:18764/v1","credential":"agy-local","models":["agy-default"],"selectedModel":"agy-default"}]}
        """);
    File.WriteAllText(Path.Combine(custom, "manifest.json"), """
        {"id":"custom-llm","modelServices":[{"id":"openai","name":"Custom LLM","providerType":"OpenAI","baseUrl":"http://127.0.0.1:12222/v1","models":["custom-1"]}]}
        """);
    var s = new AppSettings
    {
        AiServiceProviders = [new AiServiceProviderSettings { Id = "manual-1", Name = "Manual User Provider", ProviderType = "OpenAI" }],
        ActiveServiceProviderId = "miniapp:agy-model-service:agy-cli"
    };
    Refresh(s);
    if (s.AiServiceProviders.Count != 3) throw new Exception("Register: expected 3 providers");
    var agyProvider = s.AiServiceProviders.Single(p => p.OwnerExtensionId == "agy-model-service");
    if (agyProvider.ApiKey.Length < 40 || agyProvider.Models.Single() != "agy-default")
        throw new Exception("AGY local credential/model missing");
    if (!s.AiServiceProviders.Any(p => p.Id == "miniapp:custom-llm:openai"))
        throw new Exception("Generic provider registration missing");
    agyProvider.IsEnabled = false;
    Refresh(s);
    if (s.AiServiceProviders.Count != 3 || agyProvider.IsEnabled)
        throw new Exception("Reconcile not idempotent, or user preference overwritten");
    File.Delete(Path.Combine(agy, "manifest.json"));
    Refresh(s);
    if (s.AiServiceProviders.Count != 2 || s.AiServiceProviders.Any(p => p.Id.StartsWith("miniapp:agy-model-service")))
        throw new Exception("Uninstall did not remove owned provider");
    if (s.ActiveServiceProviderId == "miniapp:agy-model-service:agy-cli" ||
        !s.AiServiceProviders.Any(p => p.Id == "manual-1"))
        throw new Exception("Selection fallback or manual provider preservation failed");
    File.Delete(Path.Combine(custom, "manifest.json"));
    Refresh(s);
    if (s.AiServiceProviders.Count != 1 || s.AiServiceProviders[0].Id != "manual-1")
        throw new Exception("Uninstall did not preserve manual provider");
    Console.WriteLine("PASS: manifest auto register / generic provider / idempotency / remove-on-delete / manual preservation");
}
finally
{
    Directory.Delete(root, recursive: true);
}
