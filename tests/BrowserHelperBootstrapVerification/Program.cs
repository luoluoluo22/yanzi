using System.Text.Json;
using OpenQuickHost;

var root = Path.Combine(Path.GetTempPath(), "yanzi-browser-bootstrap-test-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    var prefs = Path.Combine(root, "Preferences");
    var settings = new Dictionary<string, object>
    {
        ["abcdefghijklmnopabcdefghijklmnop"] = new { state = 1, manifest = new { name = "燕子浏览器助手 (Yanzi Browser Helper)", version = "0.5.63" } },
        ["otherextensionidhere"] = new { state = 1, manifest = new { name = "Unrelated Helper", version = "1.0" } }
    };
    void WritePrefs(Dictionary<string, object> extensions) => File.WriteAllText(prefs,
        JsonSerializer.Serialize(new { extensions = new { settings = extensions } }));
    void Assert(bool value, string explanation)
    {
        if (!value) throw new Exception(explanation);
        Console.WriteLine("PASS: " + explanation);
    }

    WritePrefs(settings);
    var enabled = BrowserHelperBootstrap.InspectPreferences(prefs);
    Assert(enabled.Installed && enabled.Enabled && enabled.Version == "0.5.63", "enabled extension and version are detected");
    settings["abcdefghijklmnopabcdefghijklmnop"] = new { state = 0, manifest = new { name = "燕子浏览器助手", version = "0.5.63" } };
    WritePrefs(settings);
    var disabled = BrowserHelperBootstrap.InspectPreferences(prefs);
    Assert(disabled.Installed && !disabled.Enabled, "disabled extension is distinguished from missing");
    WritePrefs(new Dictionary<string, object>());
    Assert(!BrowserHelperBootstrap.InspectPreferences(prefs).Installed, "empty profile is missing");
    File.WriteAllText(prefs, "invalid-json");
    Assert(!BrowserHelperBootstrap.InspectPreferences(prefs).Installed, "invalid profile does not crash");

    // Edge Secure Preferences can omit manifest and state, but keeps the verified unpacked path.
    var secureDirectory = Path.Combine(root, "secure-unpacked");
    Directory.CreateDirectory(secureDirectory);
    File.WriteAllText(Path.Combine(secureDirectory, "manifest.json"),
        "{\\\"name\\\":\\\"燕子浏览器助手 (Yanzi Browser Helper)\\\",\\\"version\\\":\\\"0.5.63\\\"}");
    WritePrefs(new Dictionary<string, object> {
        ["secureedgeextension"] = new { path = secureDirectory, location = 4, disable_reasons = 0 }
    });
    var secure = BrowserHelperBootstrap.InspectPreferences(prefs);
    Assert(secure.Installed && secure.Enabled && secure.Version == "0.5.63", "Edge unpacked extension detected without embedded manifest or state");
    File.WriteAllText(Path.Combine(secureDirectory, "manifest.json"), "{\\\"name\\\":\\\"Unrelated extension\\\"}");
    Assert(!BrowserHelperBootstrap.InspectPreferences(prefs).Installed, "unrelated unpacked extension ignored");

    var source = Path.Combine(root, "package");
    var target = Path.Combine(root, "stable");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"name\":\"燕子浏览器助手\",\"version\":\"0.1\"}");
    File.WriteAllText(Path.Combine(source, "background.js"), "const a=1;");
    var result = BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(result == target && File.Exists(Path.Combine(target, "background.js")), "package deployed to stable directory");
    var configPath = BrowserHelperBootstrap.WriteRuntimeConfiguration(42980, target);
    Assert(JsonDocument.Parse(File.ReadAllText(configPath)).RootElement.GetProperty("apiPort").GetInt32() == 42980,
        "current desktop API port exported without credentials");
    BrowserHelperBootstrap.WriteRuntimeConfiguration(42980, target);
    Assert(File.ReadAllText(configPath) == "{\\\"apiPort\\\":42980}", "port configuration remains idempotent");
    var hash1 = File.ReadAllText(Path.Combine(target, ".yanzi-bundle-sha256"));
    BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(File.ReadAllText(Path.Combine(target, ".yanzi-bundle-sha256")) == hash1 && !Directory.Exists(target + ".previous"), "same bundle is idempotent");
    File.WriteAllText(Path.Combine(source, "background.js"), "const a=2;");
    BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(File.ReadAllText(Path.Combine(target, "background.js")) == "const a=2;" &&
           File.ReadAllText(Path.Combine(target + ".previous", "background.js")) == "const a=1;", "update keeps one rollback copy");
    File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"name\":\"Unknown Extension\",\"version\":\"0.2\"}");
    try
    {
        BrowserHelperBootstrap.EnsureStableBundle(source, target);
        throw new Exception("Invalid package accepted");
    }
    catch (InvalidDataException) { Console.WriteLine("PASS: package identity verified"); }
    Console.WriteLine("BROWSER_HELPER_BOOTSTRAP=PASSED");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}
