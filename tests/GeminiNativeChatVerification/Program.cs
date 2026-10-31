using System.Net;
using System.Text;
using System.Text.Json;
using OpenQuickHost;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
}
Check(GeminiNativeChatClient.IsGeminiApi("https://generativelanguage.googleapis.com/v1beta"), "native base recognized");
Check(GeminiNativeChatClient.IsGeminiApi("https://generativelanguage.googleapis.com/v1beta/openai"), "compat base recognized");
Check(!GeminiNativeChatClient.IsGeminiApi("https://api.openai.com/v1"), "non-Gemini unchanged");
Check(GeminiNativeChatClient.BuildEndpoint("https://generativelanguage.googleapis.com/v1beta/openai", "models/gemini-test").AbsolutePath == "/v1beta/models/gemini-test:generateContent", "native endpoint from compatible base");
var messages = new object[] {
    new { role = "system", content = "Follow the instructions." },
    new { role = "user", content = "hi" },
    new { role = "assistant", content = "hello" },
};
var body = GeminiNativeChatClient.BuildPayload(messages);
using var parsed = JsonDocument.Parse(body);
Check(parsed.RootElement.GetProperty("contents").GetArrayLength() == 2, "role transformation");
Check(parsed.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString() == "Follow the instructions.", "systemInstruction");
Check(parsed.RootElement.GetProperty("contents")[1].GetProperty("role").GetString() == "model", "assistant mapped to model");
Check(GeminiNativeChatClient.ParseAnswer("""{"candidates":[{"content":{"parts":[{"text":"Hello "},{"text":"World"}]}}]}""") == "Hello World", "native result parsed");
var recorded = new List<string>();
using var client = new HttpClient(new FakeHandler(async request => {
    Check(request.RequestUri?.AbsolutePath == "/v1beta/models/gemini-test:generateContent", "posted to native API");
    Check(request.Headers.TryGetValues("x-goog-api-key", out var values) && values.Single() == "synthetic-key", "key in header");
    Check(request.RequestUri?.Query.Length == 0, "no key in URL");
    var json = await request.Content!.ReadAsStringAsync();
    Check(json.Contains("systemInstruction") && !json.Contains("synthetic-key"), "payload has no key");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"pong"}]}}]}""") };
}));
var answer = await GeminiNativeChatClient.SendAsync(client, "https://generativelanguage.googleapis.com/v1beta", "synthetic-key", "gemini-test", messages, CancellationToken.None, recorded.Add);
Check(answer == "pong", "full native roundtrip");
Check(recorded.Any(x => x.StartsWith("native-completed")), "lifecycle diagnostic");
using var failClient = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
try {
    await GeminiNativeChatClient.SendAsync(failClient, "https://generativelanguage.googleapis.com/v1beta", "synthetic-key", "missing", messages, CancellationToken.None, _ => { });
    throw new Exception("Unexpected success");
}
catch (HttpRequestException e) {
    Check(e.StatusCode == HttpStatusCode.NotFound, "native 404 surfaced");
}
Console.WriteLine("GeminiNativeChatVerification completed");
sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handler(request);
}
