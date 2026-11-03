using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

var assembly = Assembly.Load("Yanzi");
var store = assembly.GetType("OpenQuickHost.LocalLlmApiTokenStore", throwOnError: true)!;
var get = store.GetMethod("LoadOrCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
var localToken = (string)get.Invoke(null, null)!;
using var handler = new HttpClientHandler { UseProxy = false };
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", localToken);
var uri = "http://127.0.0.1:42980";
using var modelsResult = await http.GetAsync(uri + "/v1/models");
modelsResult.EnsureSuccessStatusCode();
using var catalog = JsonDocument.Parse(await modelsResult.Content.ReadAsStringAsync());
var registered = catalog.RootElement.GetProperty("data").EnumerateArray().Where(m =>
    m.GetProperty("provider_id").GetString() == "miniapp:agy-model-service:agy-cli").ToList();
if (registered.Count != 1) throw new Exception("AGY registered model count = " + registered.Count);
var model = registered[0].GetProperty("id").GetString()!;
Console.WriteLine("REGISTERED=" + model);
var payload = JsonSerializer.Serialize(new {
    model,
    stream = false,
    messages = new[] { new { role = "user", content = "Reply exactly OK." } }
});
using var request = new StringContent(payload, Encoding.UTF8, "application/json");
var start = DateTime.UtcNow;
using var response = await http.PostAsync(uri + "/v1/chat/completions", request);
var result = await response.Content.ReadAsStringAsync();
if (!response.IsSuccessStatusCode) throw new Exception("LLM HTTP " + (int)response.StatusCode + ": " + result.Substring(0, Math.Min(result.Length, 220)));
using var parsed = JsonDocument.Parse(result);
var reply = parsed.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
if (reply != "OK") throw new Exception("Unexpected response " + reply);
Console.WriteLine("E2E_PASS: reply=OK, elapsed_s=" + Math.Round((DateTime.UtcNow - start).TotalSeconds, 1));
