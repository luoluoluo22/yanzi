using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

var assembly = Assembly.Load("Yanzi");
var cls = assembly.GetType("OpenQuickHost.YanziAgyModelBridge", throwOnError: true)!;
object? Call(string method) => cls.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, null);
var key = (string)Call("GetLocalKey")!;
try
{
    Call("EnsureStarted");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(65) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    var status = await http.GetAsync("http://127.0.0.1:18764/health");
    status.EnsureSuccessStatusCode();
    var models = await http.GetStringAsync("http://127.0.0.1:18764/v1/models");
    using var doc = JsonDocument.Parse(models);
    if (doc.RootElement.GetProperty("data").GetArrayLength() < 2)
        throw new Exception("No model list");
    var payload = JsonSerializer.Serialize(new
    {
        model = "agy-default",
        messages = new[] { new { role = "user", content = "Reply exactly OK." } },
        stream = false
    });
    using var reply = await http.PostAsync("http://127.0.0.1:18764/v1/chat/completions",
        new StringContent(payload, Encoding.UTF8, "application/json"));
    reply.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
    var answer = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
    if (!string.Equals(answer, "OK", StringComparison.OrdinalIgnoreCase))
        throw new Exception("Unexpected answer: " + answer);
    Console.WriteLine("PASS: .NET AGY bridge models + real completion: OK");
}
finally
{
    Call("Stop");
}
