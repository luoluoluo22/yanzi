using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace OpenQuickHost.CSharpRuntime
{
    public sealed class YanziActionContext
    {
        public string ExtensionDataDirectory { get; set; } = "";
        public string AgentApiBaseUrl { get; set; } = "";
        public string LaunchSource { get; set; } = "";
        public StorageStub Storage { get; } = new();
    }
    public sealed class StorageStub
    {
        public Task<T?> ReadJsonAsync<T>(string key, string scope = "local") => Task.FromResult<T?>(default);
        public Task WriteJsonAsync<T>(string key, T value, string scope = "local") => Task.CompletedTask;
    }
}

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Contains("--install-core"))
        {
            var type = typeof(OpenQuickHost.AppEnvironmentVariableStore).Assembly.GetType("OpenQuickHost.YanziPortableMihomoInstaller")
                ?? throw new Exception("Mihomo installer missing");
            var method = type.GetMethod("EnsureAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new Exception("Mihomo EnsureAsync missing");
            var pending = (Task<string>)method.Invoke(null, new object?[] { new Version(1, 19, 31), CancellationToken.None })!;
            var path = await pending;
            if (!File.Exists(path) || File.ReadAllBytes(path).Length < 5_000_000) throw new Exception("Installed Mihomo binary invalid");
            if (File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "core.zip"))) throw new Exception("Installer retained download archive");
            var probe = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            probe.ArgumentList.Add("-v");
            using var process = System.Diagnostics.Process.Start(probe) ?? throw new Exception("Failed to launch Mihomo");
            var version = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !version.Contains("mihomo", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Mihomo version probe failed");
            var sandbox = Path.Combine(Path.GetTempPath(), "yanzi-vpn-validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            try
            {
                var yaml = VpnManagerWindow.SafeConfig(
                    "proxies:\n  - name: sample-direct\n    type: direct\nproxy-groups:\n  - name: Manual\n    type: select\n    proxies: [sample-direct]\nrules:\n  - MATCH,Manual\n",
                    new string('a', 64), "rule");
                var file = Path.Combine(sandbox, "config.yaml");
                File.WriteAllText(file, yaml);
                var validation = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                validation.ArgumentList.Add("-t");
                validation.ArgumentList.Add("-d"); validation.ArgumentList.Add(sandbox);
                validation.ArgumentList.Add("-f"); validation.ArgumentList.Add(file);
                using var validator = System.Diagnostics.Process.Start(validation) ?? throw new Exception("Mihomo -t failed to start");
                using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await validator.WaitForExitAsync(tokenSource.Token);
                if (validator.ExitCode != 0) throw new Exception("Mihomo rejected the sanitized config");
                Console.WriteLine("VPN_MIHOMO_CONFIG_VALIDATED");
            }
            finally { Directory.Delete(sandbox, recursive: true); }
            Console.WriteLine("VPN_CORE_INSTALL_VERIFIED");
        }
        if (args.Contains("--ui-smoke"))
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var ctx = new OpenQuickHost.CSharpRuntime.YanziActionContext { ExtensionDataDirectory = Path.GetTempPath() };
                    var opened = false;
                    var window = new VpnManagerWindow(ctx, new VpnSyncSettings());
                    window.Loaded += (_, _) => opened = true;
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                    timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                    timer.Start();
                    window.ShowDialog();
                    Assert(opened, "Native VPN window loaded");
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(10));
            if (thread.IsAlive) throw new Exception("UI smoke hung");
            if (error != null) throw error;
            Console.WriteLine("VPN_UI_SMOKE_PASSED");
        }
        const string secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string original = "external-controller: 0.0.0.0:9090\nsecret: compromised\n" +
            "tun:\n  enable: true\n  auto-route: true\nmixed-port: 7890\nmode: global\n" +
            "proxies:\n  - name: TEST\n    type: direct\nproxy-groups:\n  - name: A\n    type: select\n    proxies: [TEST]\n";
        var result = VpnManagerWindow.SafeConfig(original, secret, "rule");
        Assert(result.Contains("external-controller: 127.0.0.1:18990"), "controller localhost only");
        Assert(result.Contains("mixed-port: 17990"), "unique mixed port");
        Assert(result.Contains("tun:\n  enable: false"), "tun disabled");
        Assert(!result.Contains("0.0.0.0:9090"), "dangerous controller removed");
        Assert(!result.Contains("compromised"), "dangerous source secret removed");
        Assert(!result.Contains("auto-route"), "dangerous tun options removed");
        Assert(result.Contains("proxies:"), "proxy data preserved");
        Assert(result.Contains("mode: rule"), "mode fixed");
        var dangerous = VpnManagerWindow.SafeConfig(
            "listeners:\n  - name: bypass\n    type: mixed\n    port: 8443\nproxies:\n  - name: TEST\n    type: direct\n",
            secret, "rule");
        Assert(!dangerous.Contains("listeners:"), "listener injection removed");
        bool rejected = false;
        try { VpnManagerWindow.SafeConfig("'tun':\n  enable: true\nproxies:\n  - name: TEST\n    type: direct\n", secret, "rule"); }
        catch (System.IO.InvalidDataException) { rejected = true; }
        Assert(rejected, "quoted-root override rejected");
        var encrypted = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(result), null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        Assert(!System.Text.Encoding.UTF8.GetString(encrypted).Contains("external-controller:"), "DPAPI cache does not expose plaintext");
        var decrypted = System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
            encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        Assert(decrypted == result, "DPAPI roundtrip");
        var settings = new VpnSyncSettings { SubscriptionVariable = "VPN_URL", PreferredMode = "rule" };
        settings.Selections["PROXY"] = "Node A";
        var restored = JsonSerializer.Deserialize<VpnSyncSettings>(JsonSerializer.Serialize(settings))!;
        Assert(restored.Selections["PROXY"] == "Node A", "node-selection roundtrip");
        var json = JsonSerializer.Serialize(settings);
        Assert(json.Contains("VPN_URL") && !json.Contains("https://"), "sync metadata only");
        Console.WriteLine("VPN_SECURITY_CHECKS_PASSED");
    }
    private static void Assert(bool ok, string reason)
    {
        if (!ok) throw new Exception("Assertion failed: " + reason);
    }
}
