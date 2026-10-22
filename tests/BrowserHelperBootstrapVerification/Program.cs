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

    var source = Path.Combine(root, "package");
    var target = Path.Combine(root, "stable");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"name\":\"燕子浏览器助手\",\"version\":\"0.1\"}");
    File.WriteAllText(Path.Combine(source, "background.js"), "const a=1;");
    var result = BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(result == target && File.Exists(Path.Combine(target, "background.js")), "package deployed to stable directory");
    var hash1 = File.ReadAllText(Path.Combine(target, ".yanzi-bundle-sha256"));
    BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(File.ReadAllText(Path.Combine(target, ".yanzi-bundle-sha256")) == hash1 && !Directory.Exists(target + ".previous"), "same bundle is idempotent");
    File.WriteAllText(Path.Combine(source, "background.js"), "const a=2;");
    BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(File.ReadAllText(Path.Combine(target, "background.js")) == "const a=2;" &&
           File.ReadAllText(Path.Combine(target + ".previous", "background.js")) == "const a=1;", "update keeps one rollback copy");
    // Latest browser helper (0.5.71) must not be overwritten by an older (0.5.69) package.
    File.WriteAllText(Path.Combine(target, "manifest.json"), "{\"name\":\"燕子浏览器助手\",\"version\":\"0.5.71\"}");
    File.WriteAllText(Path.Combine(source, "manifest.json"), "{\"name\":\"燕子浏览器助手\",\"version\":\"0.5.69\"}");
    File.WriteAllText(Path.Combine(source, "background.js"), "const older=true;");
    BrowserHelperBootstrap.EnsureStableBundle(source, target);
    Assert(File.ReadAllText(Path.Combine(target, "background.js")) == "const a=2;" &&
           File.ReadAllText(Path.Combine(target, "manifest.json")).Contains("0.5.71"),
           "never downgrade browser helper during host upgrade");
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
