using OpenQuickHost;

static void Verify(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + scenario);
    Console.WriteLine("PASS: " + scenario);
}

const string key = "fake-gemini-test-key";

using var native = SettingsAiController.BuildModelListRequest(new AiProviderSnapshot(
    "Gemini", "https://generativelanguage.googleapis.com/v1beta", key));
Verify(native.Method == HttpMethod.Get, "native list method");
Verify(native.RequestUri?.AbsolutePath == "/v1beta/models", "native models endpoint");
Verify(native.RequestUri!.Query.Contains("key=", StringComparison.Ordinal) ||
       native.Headers.Contains("x-goog-api-key"), "native Gemini credential attached");
Verify(native.Headers.Authorization == null, "native Gemini does not use Bearer");

using var compatible = SettingsAiController.BuildModelListRequest(new AiProviderSnapshot(
    "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", key));
Verify(compatible.RequestUri?.AbsolutePath == "/v1beta/openai/models", "compatible models endpoint");
Verify(compatible.Headers.Authorization?.Scheme == "Bearer", "compatible Bearer scheme");
Verify(compatible.Headers.Authorization?.Parameter == key, "compatible credential included");
Verify(!compatible.RequestUri!.Query.Contains("key=", StringComparison.Ordinal), "compatible URL has no API key");

using var openAi = SettingsAiController.BuildModelListRequest(new AiProviderSnapshot(
    "OpenAI", "https://api.openai.com/v1", key));
Verify(openAi.RequestUri?.AbsolutePath == "/v1/models" &&
       openAi.Headers.Authorization?.Scheme == "Bearer", "OpenAI unaffected");

var nativeModels = SettingsAiController.ParseAvailableModelNames(
    "Gemini", """{"models":[{"name":"models/gemini-test"},{"name":"models/gemini-next"}]}""");
Verify(nativeModels.SequenceEqual(["gemini-next", "gemini-test"]), "native models parsed");

var compatibleModels = SettingsAiController.ParseAvailableModelNames(
    "Gemini", """{"data":[{"id":"gemini-test"},{"id":"gemini-next"}]}""");
Verify(compatibleModels.SequenceEqual(["gemini-next", "gemini-test"]), "compatible models parsed");

Console.WriteLine("All Gemini provider verification checks passed.");
