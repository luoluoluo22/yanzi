using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using OpenQuickHost;
using System.Reflection;
using System.Security.Cryptography;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

if (args.Length == 1 && args[0] == "--secure-lan") { await SecureLanVerification.RunAsync(); return; }
if (args.Length == 1 && args[0] == "--transfer-sessions") { await TransferSessionsVerification.RunAsync(); return; }
if (args.Length == 1 && args[0] == "--outbox") { await OutboxVerification.RunAsync(); return; }
YanziBuiltinCapabilityRegistration.Register();
if (args.Length == 2 && args[0] == "--send-mobile-lan")
{
    using var config = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
    var value = config.RootElement;
    using var isolatedRoot = value.TryGetProperty("dataRoot", out var dataRoot)
        ? (IDisposable)typeof(HostAssets).GetMethod("UseExistingDataRootForVerification", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [dataRoot.GetString()!])! : null;
    if (value.TryGetProperty("peerDeviceId", out var peerId))
        YanziPeerRegistry.ObserveAuthenticated(peerId.GetString()!, IPAddress.Parse(value.GetProperty("ip").GetString()!),
            value.GetProperty("port").GetInt32(), "Isolated Dev receive fixture");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    if (value.TryGetProperty("dropBlockAck", out var drop) && drop.GetBoolean())
    {
        await using var proxy = new LanAckLossProxy(IPAddress.Parse(value.GetProperty("ip").GetString()!), value.GetProperty("port").GetInt32());
        YanziPeerRegistry.ObserveAuthenticated(value.GetProperty("peerDeviceId").GetString()!, IPAddress.Loopback, proxy.Port, "Ack loss fixture");
        await YanziLanMobileTransfer.SendAsync(IPAddress.Loopback, proxy.Port, "", value.GetProperty("file").GetString()!, value.GetProperty("kind").GetString()!, value.GetProperty("id").GetString()!);
        if (proxy.BlockZeroRequests > 0 && (!proxy.Dropped || proxy.BlockZeroRequests != 1)) throw new Exception("Lost block ACK did not resume from durable block");
        // Restore the authenticated physical endpoint after the local fault injector stops.
        YanziPeerRegistry.ObserveAuthenticated(value.GetProperty("peerDeviceId").GetString()!, IPAddress.Parse(value.GetProperty("ip").GetString()!), value.GetProperty("port").GetInt32(), "Isolated Dev receive fixture");
        Console.WriteLine("REAL_PHONE_LOST_BLOCK_ACK_RESUME=PASSED; blockZeroRequests=" + proxy.BlockZeroRequests);
        return;
    }
    await YanziLanMobileTransfer.SendAsync(IPAddress.Parse(value.GetProperty("ip").GetString()!),
        value.GetProperty("port").GetInt32(), value.GetProperty("token").GetString()!,
        value.GetProperty("file").GetString()!, value.GetProperty("kind").GetString()!, value.GetProperty("id").GetString()!);
    Console.WriteLine("DESKTOP_BINARY_LAN_SEND=PASSED; durationMs=" + watch.ElapsedMilliseconds);
    return;
}
if (args.Length == 1 && args[0] == "--transfer-cloud-fixture")
{
    await TransferCloudFixture.RunAsync();
    return;
}
// A disposable localhost fixture for testing the real console HTML in a browser.
// It has its own test credential and does not share the user's Agent token.
if (args.Length == 1 && args[0] == "--console-fixture")
{
    using var fixture = new LocalAgentApiServer("http://127.0.0.1:53929/", "disposable-console-test", _ => { });
    fixture.Start();
    Console.WriteLine("Console fixture ready on 53929. Press Enter to stop.");
    Console.ReadLine();
    return;
}
// Use the capture extension's actual old context contract as a backward compatibility fixture.
var legacyDirectory = Path.Combine(Path.GetTempPath(), "yanzi-legacy-context-" + Guid.NewGuid().ToString("N"));
var legacyCommand = new CommandItem(glyph: "T", title: "Legacy runtime", subtitle: "", category: "test",
    accentHex: "#FF000000", openTarget: null, keywords: Array.Empty<string>(),
    extensionId: "legacy-context", extensionDirectoryPath: legacyDirectory);
var legacyContext = typeof(ScriptExtensionRunner).GetMethod("CreateContext", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, new object?[] { legacyCommand, "test input", "verification", null });
var sessionType = typeof(ScriptExtensionRunner).Assembly.GetType("OpenQuickHost.YanziExtensionCapabilitySession")!;
using (var session = (IDisposable)Activator.CreateInstance(sessionType, legacyCommand)!)
{
    var legacyRuntime = (OpenQuickHost.CSharpRuntime.YanziActionContext)typeof(ScriptExtensionRunner)
        .GetMethod("CreateInProcessRuntimeContext", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new[] { typeof(OpenQuickHost.CSharpRuntime.YanziActionContext).Assembly,
            legacyContext, (object)"test-state.json", session })!;
    Check(legacyRuntime.InputText == "test input" && legacyRuntime.RegisterObject != null && legacyRuntime.GetRegisteredObject != null,
        "A precompiled legacy context without capability properties must still initialize its original APIs.");
}
var clipboard = (YanziCapabilityDescriptor)YanziCapabilityQueryService.Describe(" CLIPBOARD.SET ")!;
Check(clipboard.Permissions.Contains("clipboard") && clipboard.InputSchema.GetProperty("required")[0].GetString() == "text",
    "Capability description must expose its contract and permission.");
var denied = await YanziCapabilityInvocationService.InvokeAsync("clipboard.set", new { text = "must never be written" });
Check(!denied.Success && denied.ErrorCode == "permission_denied", "Anonymous clipboard write must be denied.");
var invalidClipboard = await YanziCapabilityInvocationService.InvokeAsync("clipboard.set", new { text = 1 },
    new YanziCapabilityCaller("test", new[] { "clipboard" }));
Check(!invalidClipboard.Success && invalidClipboard.ErrorCode == "invalid_parameters", "Invalid clipboard payload must fail before a write.");

var executed = 0;
YanziCapabilityProviderSdk.RegisterProvider("verification-source", new[]
{
    new YanziCapabilityProviderDefinition
    {
        Name = "verification.search", Permissions = new[] { "history.read" },
        InputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"query\"],\"additionalProperties\":false}"),
        OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}}}"),
        Handler = payload => { executed++; return Task.FromResult<object?>(new { url = "https://example.com" }); }
    }
});
YanziCapabilityProviderSdk.RegisterProvider("verification-destination", new[]
{
    new YanziCapabilityProviderDefinition
    {
        Name = "verification.create", Permissions = new[] { "reminder.write" },
        InputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}},\"required\":[\"url\"]}"),
        Handler = payload => Task.FromResult<object?>(new { created = ((JsonElement)payload!).GetProperty("url").GetString() })
    }
});
var caller = new YanziCapabilityCaller("verification", new[] { "history.read", "reminder.write" });
var badInputs = new object?[] { null, new { query = 3 }, new { query = "" }, new { query = "url", extra = true } };
foreach (var input in badInputs)
{
    var result = await YanziCapabilityInvocationService.InvokeAsync("verification.search", input, caller);
    Check(!result.Success && result.ErrorCode == "invalid_parameters", "Invalid input must be rejected.");
}
Check(executed == 0, "Validation failures must not run the provider.");
try
{
    YanziCapabilityProviderSdk.RegisterProvider("unsupported", new[]
    {
        new YanziCapabilityProviderDefinition { Name = "verification.unsupported",
            InputSchema = YanziCapabilitySchema.Parse("{\"type\":\"string\",\"pattern\":\"^safe$\"}") }
    });
    throw new InvalidOperationException("Unsupported schema constraints must not be silently ignored.");
}
catch (ArgumentException)
{
    Check(!YanziCapabilityRegistry.Contains("verification.unsupported") &&
        !YanziCapabilityRuntimeBinding.TryGet("verification.unsupported", out _), "Invalid schema must not leave a runtime binding.");
}
var nestedSchema = YanziCapabilitySchema.Parse("{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\",\"enum\":[1,2]}},\"required\":[\"count\"]}}");
try
{
    YanziCapabilitySchema.Validate(nestedSchema, JsonSerializer.SerializeToElement(new[] { new { count = 1.5 } }));
    throw new InvalidOperationException("Nested integer validation must reject fractions.");
}
catch (ArgumentException) { checks++; }
try
{
    YanziCapabilitySchema.Validate(nestedSchema, JsonSerializer.SerializeToElement(new[] { new { count = 3 } }));
    throw new InvalidOperationException("Nested enum validation must reject other values.");
}
catch (ArgumentException) { checks++; }
var source = await YanziCapabilityInvocationService.InvokeAsync(" VERIFICATION.SEARCH ", new { query = "url" }, caller);
var destination = await YanziCapabilityClient.InvokeAsync("verification.create", source.Data, caller);
Check(destination.Success && JsonSerializer.SerializeToElement(destination.Data).GetProperty("created").GetString() == "https://example.com",
    "Output from one provider must be usable as input to another.");
Check(YanziCapabilityCallLog.List().Any(x => x.Provider == "verification-source" && x.Caller == "verification" && x.Success),
    "SDK providers must retain provider and caller attribution.");
var json = await YanziCapabilityInvocationService.InvokeJsonAsync("{\"name\":\"capability.describe\",\"payload\":{\"name\":\"verification.search\"}}");
using (var document = JsonDocument.Parse(json))
    Check(document.RootElement.GetProperty("success").GetBoolean(), "JSON protocol must support camelCase input/output.");
var forged = await YanziCapabilityInvocationService.InvokeJsonAsync("{\"name\":\"verification.search\",\"payload\":{\"query\":\"url\"},\"permissions\":[\"history.read\"],\"isTrusted\":true}");
using (var document = JsonDocument.Parse(forged))
    Check(document.RootElement.GetProperty("errorCode").GetString() == "permission_denied", "JSON must not grant caller permissions.");
using (var document = JsonDocument.Parse(await YanziCapabilityInvocationService.InvokeJsonAsync("{")))
    Check(document.RootElement.GetProperty("errorCode").GetString() == "invalid_request", "Malformed JSON must return a structured error.");

// Discovery must preserve schema data after its JsonDocument is disposed.
var manifestPath = Path.Combine(Path.GetTempPath(), $"yanzi-capability-{Guid.NewGuid():N}.json");
try
{
    await File.WriteAllTextAsync(manifestPath, "{\"provides\":[{\"name\":\"verification.manifest\",\"version\":\"2.0\",\"permissions\":[\"test.read\"],\"inputSchema\":{\"type\":\"object\"},\"outputSchema\":{\"type\":\"string\"}}]}");
    YanziCapabilityDiscoveryService.Discover("verification-manifest", manifestPath);
    Check(!YanziCapabilityRegistry.Contains("verification.manifest"), "An unbound manifest must not advertise callable capabilities.");
    YanziCapabilityDiscoveryService.Discover("verification-manifest", manifestPath, (_, _, _) => Task.FromResult<object?>("bound"));
    var descriptor = (YanziCapabilityDescriptor)YanziCapabilityQueryService.Describe("verification.manifest")!;
    Check(descriptor.Version == "2.0" && descriptor.InputSchema.GetProperty("type").GetString() == "object", "Manifest metadata must survive discovery.");
    var result = await YanziCapabilityInvocationService.InvokeAsync("verification.manifest", new { }, new("manifest-client", new[] { "test.read" }));
    Check(result.Success && (string?)result.Data == "bound", "Manifest invocation must reach the injected runtime binding.");
}
finally { File.Delete(manifestPath); YanziCapabilityRuntimeRegistry.RemoveByExtension("verification-manifest"); }
YanziCapabilityProviderSdk.RegisterProvider("verification-replacement", new[]
{
    new YanziCapabilityProviderDefinition { Name = "verification.search", Handler = _ => Task.FromResult<object?>("replacement") }
});
YanziCapabilityRuntimeRegistry.RemoveByExtension("verification-source");
Check(YanziCapabilityRegistry.Contains("verification.search"), "Unregistering an old provider must retain a replacement.");
YanziCapabilityRuntimeRegistry.RemoveByExtension("verification-replacement");
Check(!YanziCapabilityRegistry.Contains("verification.search") && !YanziCapabilityRuntimeBinding.TryGet("verification.search", out _),
    "SDK unregister must clear registry, runtime and binding.");
var loopRuns = 0;
YanziCapabilityProviderSdk.RegisterProvider("verification-loop", new[]
{
    new YanziCapabilityProviderDefinition
    {
        Name = "verification.loop", Handler = async _ =>
        {
            loopRuns++;
            var nested = await YanziCapabilityInvocationService.InvokeAsync("verification.loop");
            if (!nested.Success) throw new InvalidOperationException(nested.Error);
            return nested.Data;
        }
    }
});
var loop = await YanziCapabilityInvocationService.InvokeAsync("verification.loop");
Check(!loop.Success && loopRuns == 1, "A recursive capability must be rejected before running its handler again.");
YanziCapabilityRuntimeRegistry.RemoveByExtension("verification-loop");

// Use an isolated authenticated HTTP server; never read the user's real token.
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var token = Guid.NewGuid().ToString("N");
var transferCalls = 0;
using var server = new LocalAgentApiServer($"http://127.0.0.1:{port}/", token, _ => { }, onMobileMessage: message =>
{
    transferCalls++;
    var local = YanziLanTransferStore.Resolve(message.Payload["lanAttachmentId"].GetString()!);
    if (!File.Exists(local)) throw new InvalidOperationException("LAN attachment must be ready before delivery.");
    return Task.FromResult((true, "delivered"));
});
server.Start();
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
using (var response = await http.GetAsync("v1/capabilities"))
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "Capabilities API requires authentication.");
foreach (var endpoint in new[] { "v1/agent/catalog", "v1/agent/openapi.json", "v1/extensions/test/capabilities" })
{
    using var response = await http.GetAsync(endpoint);
    Check(response.StatusCode == HttpStatusCode.Unauthorized, "Agent discovery endpoints must require authentication.");
}
http.DefaultRequestHeaders.Add("X-Yanzi-Token", token);
using (var response = await http.GetAsync("v1/agent/catalog"))
{
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Check(response.IsSuccessStatusCode && body.GetProperty("schemaVersion").GetString() == "1.0" &&
        body.GetProperty("hostCapabilities").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "system.time.now" && x.GetProperty("available").GetBoolean()),
        "Catalog must expose available host capabilities.");
    var programs = body.GetProperty("extensions").EnumerateArray().ToArray();
    Check(programs.All(x => x.TryGetProperty("capabilities", out _) && x.TryGetProperty("runEndpoint", out _)),
        "Each installed app must carry its capability association and startup endpoint.");
    var declared = programs.SelectMany(x => x.GetProperty("capabilities").EnumerateArray()).ToArray();
    Check(declared.All(x => !x.GetProperty("available").GetBoolean()),
        "Installed manifest declarations must not imply callable handlers in an isolated process.");
}
using (var response = await http.GetAsync("v1/agent/openapi.json"))
{
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Check(response.IsSuccessStatusCode && body.GetProperty("openapi").GetString() == "3.1.0" &&
        body.GetProperty("paths").TryGetProperty("/v1/extensions/{extensionId}/run", out _) &&
        body.GetProperty("paths").TryGetProperty("/v1/capabilities/invoke", out _) && !body.ToString().Contains(token),
        "OpenAPI must describe discovery/start/invoke without embedding credentials.");
}
using (var response = await http.GetAsync("v1/extensions/does-not-exist/capabilities"))
    Check(response.StatusCode == HttpStatusCode.NotFound, "An unknown provider must return 404.");
using (var response = await http.GetAsync("v1/capabilities"))
{
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Check(response.IsSuccessStatusCode && body.GetProperty("capabilities").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "capability.describe"),
        "Authenticated list must include introspection capabilities.");
}
using (var response = await http.GetAsync("v1/capabilities/clipboard.set"))
    Check(response.IsSuccessStatusCode, "HTTP describe must expose built-in contracts.");
using (var response = await http.PostAsJsonAsync("v1/capabilities/invoke", new { name = "system.time.now" }))
{
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Check(response.IsSuccessStatusCode && body.GetProperty("success").GetBoolean(), "Authenticated Agent invocation must succeed.");
}
using (var response = await http.PostAsJsonAsync("v1/capabilities/invoke", new { name = "clipboard.set", payload = new { text = 2 } }))
    Check(response.StatusCode == HttpStatusCode.BadRequest, "HTTP invalid input must return 400 before clipboard side effects.");
using (var response = await http.PostAsJsonAsync("v1/capabilities/invoke", new { name = "verification.unknown" }))
    Check(response.StatusCode == HttpStatusCode.NotFound, "Unknown HTTP capability must return 404.");
using (var response = await http.GetAsync("v1/capabilities/calls"))
    Check(response.IsSuccessStatusCode, "Authenticated Agent must be able to inspect invocation logs.");
var transferId = Guid.NewGuid().ToString("N");
using (var response = await http.GetAsync("v1/me/devices/protocol"))
{
    var protocol = await response.Content.ReadFromJsonAsync<JsonElement>();
    Check(response.IsSuccessStatusCode && protocol.GetProperty("version").GetInt32() == 1,
        "The LAN server must advertise the device messaging protocol.");
}
using (var response = await http.PostAsJsonAsync("v1/me/mobile/messages", new {protocolVersion = 99, sourceDeviceId = "verification"}))
    Check((int)response.StatusCode == 426 && transferCalls == 0, "Unsupported LAN message versions must fail before execution.");
using (var response = await http.PostAsJsonAsync("v1/me/mobile/messages", new {targetDeviceId = "other-device", sourceDeviceId = "verification"}))
    Check(response.StatusCode == HttpStatusCode.NotFound && transferCalls == 0, "An explicit different LAN device target must not execute locally.");
var transferBytes = RandomNumberGenerator.GetBytes(600000);
var transferHash = Convert.ToHexString(SHA256.HashData(transferBytes)).ToLowerInvariant();
var transferPath = "v1/lan/transfers/" + transferId + "?kind=file&name=verification.bin&sourceDeviceId=verification";
async Task<HttpResponseMessage> Transfer(string path, byte[] bytes, string hash, bool authenticated = true)
{
    using var client = new HttpClient { BaseAddress = http.BaseAddress };
    if (authenticated) client.DefaultRequestHeaders.Add("X-Yanzi-Token", token);
    client.DefaultRequestHeaders.Add("X-Content-Sha256", hash);
    return await client.PostAsync(path, new ByteArrayContent(bytes));
}
try
{
    using (var response = await Transfer(transferPath, transferBytes, transferHash, false))
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "Binary LAN transfers require authentication.");
    using (var response = await Transfer(transferPath, transferBytes, transferHash))
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Check(response.IsSuccessStatusCode && body.GetProperty("transport").GetString() == "lan" && transferCalls == 1,
            "LAN transfer must deliver immediately through the mobile callback.");
        Check(File.ReadAllBytes(YanziLanTransferStore.Resolve(transferId)).SequenceEqual(transferBytes), "LAN binary bytes must survive without encoding changes.");
    }
    using (var response = await Transfer(transferPath, transferBytes, transferHash))
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Check(body.GetProperty("deduplicated").GetBoolean() && transferCalls == 1, "Retry must not deliver a LAN transfer twice.");
    }
    using (var response = await Transfer(transferPath, new byte[] { 1 }, Convert.ToHexString(SHA256.HashData(new byte[] { 1 }))))
        Check(response.StatusCode == HttpStatusCode.Conflict && transferCalls == 1, "A reused transfer ID cannot change content.");
    using (var response = await Transfer(transferPath, transferBytes, new string('0', 64)))
        Check(response.StatusCode == HttpStatusCode.BadRequest && transferCalls == 1, "Checksum mismatch must fail before delivery.");
    using (var response = await Transfer(transferPath.Replace("verification.bin", "..%2Fescape.bin"), transferBytes, transferHash))
        Check(response.StatusCode == HttpStatusCode.BadRequest, "LAN file names cannot escape their storage directory.");
    Check(!Directory.EnumerateFiles(YanziLanTransferStore.Root, "*.part").Any(), "Rejected transfers must remove staging files.");
}
finally
{
    File.Delete(YanziLanTransferStore.FilePath(transferId, "verification.bin"));
    File.Delete(Path.Combine(YanziLanTransferStore.Root, transferId + ".json"));
}
for (var i = 0; i < 1005; i++) YanziCapabilityCallLog.Add("verification", "test", true);
Check(YanziCapabilityCallLog.List().Count == 1000, "Call log must be bounded.");
Console.WriteLine($"Capability verification PASSED: {checks} checks.");
