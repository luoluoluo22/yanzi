using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenQuickHost;

static void Require(bool test, string message)
{
    if (!test) throw new Exception("CHECK_FAILED: " + message);
    Console.WriteLine("PASS " + message);
}
const string Base = "http://127.0.0.1:18765";
YanziCookieApiBridge.EnsureStarted();
Require(YanziCookieApiBridge.IsRunning, "adapter started");
using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(90) };
var unauth = await client.GetAsync(Base + "/v1/models");
Require(unauth.StatusCode == HttpStatusCode.Unauthorized, "models rejects missing local key");
var cookieRoute = new HttpRequestMessage(HttpMethod.Get, Base + "/cookies?domain=google.com");
cookieRoute.Headers.Authorization = new AuthenticationHeaderValue("Bearer", YanziCookieApiBridge.GetLocalKey());
using var route = await client.SendAsync(cookieRoute);
Require(route.StatusCode == HttpStatusCode.NotFound, "credential routes not exposed");
using var listRequest = new HttpRequestMessage(HttpMethod.Get, Base + "/v1/models");
listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", YanziCookieApiBridge.GetLocalKey());
using var listResponse = await client.SendAsync(listRequest);
Require(listResponse.IsSuccessStatusCode, "authenticated model list");
var modelJson = await listResponse.Content.ReadAsStringAsync();
Require(modelJson.Contains("chatgpt-web") && modelJson.Contains("gemini-3.5-flash-lite"), "models advertised");
using var badRequest = new HttpRequestMessage(HttpMethod.Post, Base + "/v1/chat/completions");
badRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", YanziCookieApiBridge.GetLocalKey());
badRequest.Content = new StringContent("{\"model\":\"unapproved\",\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}]}", Encoding.UTF8, "application/json");
using var badResult = await client.SendAsync(badRequest);
Require(badResult.StatusCode == HttpStatusCode.NotFound, "unknown model rejected");
var (backend, ext) = await YanziCookieApiBridge.GetStatusAsync();
Require(backend, "back-end connected");
Require(ext, "Ego extension connected");
async Task<(int status, string answer)> CallModelAsync(string model, string prompt)
{
    using var message = new HttpRequestMessage(HttpMethod.Post, Base + "/v1/chat/completions");
    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", YanziCookieApiBridge.GetLocalKey());
    message.Content = new StringContent(JsonSerializer.Serialize(new
    {
        model, messages = new[] { new { role = "user", content = prompt } }, stream = false
    }), Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(message);
    if (!response.IsSuccessStatusCode) return ((int)response.StatusCode, "");
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return ((int)response.StatusCode, doc.RootElement.GetProperty("choices")[0]
        .GetProperty("message").GetProperty("content").GetString() ?? "");
}
var gpt = await CallModelAsync("chatgpt-web", "Reply exactly OK.");
Console.WriteLine("GPT_HTTP=" + gpt.status + " CHARS=" + gpt.answer.Length + " EXACT_OK=" + (gpt.answer.Trim() == "OK"));
Require((gpt.status == 200 && gpt.answer.Length > 0) || gpt.status == 502, "ChatGPT real call returned a valid answer or explicit upstream error");
var gemini = await CallModelAsync("gemini-3.5-flash-lite", "Compute 8*9. Reply only 72.");
Require(gemini.status == 502 || (gemini.status == 200 &&
    gemini.answer.Contains("72") && !gemini.answer.Contains("ElicitationsGroup")),
    "Gemini succeeds with clean answer or correctly signals upstream error");
Console.WriteLine("GEMINI_RESULT_HTTP=" + gemini.status);
Console.WriteLine("ALL_LOCAL_CHECKS_PASSED");
