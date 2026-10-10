using System.IO;
using OpenQuickHost;
using OpenQuickHost.Sync;
using Microsoft.Win32;

var expectDev = args.Contains("--expect-dev");
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
Check(HostRuntimeProfile.IsDevelopment == expectDev, "Incorrect runtime channel");
Check(HostRuntimeProfile.AppId == (expectDev ? "Yanzi.OpenQuickHost.Dev.Shell" : "Yanzi.OpenQuickHost.Shell"), "Incorrect singleton namespace");
Check(Path.GetFileName(HostAssets.DataRootPath) == (expectDev ? "OpenQuickHost.Dev" : "OpenQuickHost"), "Incorrect data directory");

var temp = Path.Combine(Path.GetTempPath(), "yanzi-profile-check-" + Guid.NewGuid().ToString("N"));
try
{
    using var scope = HostAssets.UseIsolatedDataRootForVerification(temp);
    var input = new AppSettings { EnableAutoUpdate = true, LaunchAtStartup = true, EnableLanSync = true, EnableWebDavSync = true };
    var expectedWebDav = AppSettingsMigration.Normalize(input).EnableWebDavSync;
    AppSettingsStore.Save(input);
    var saved = AppSettingsStore.Load();
    Check(saved.AgentApiPort == (expectDev ? 53920 : 53919), "Incorrect API port");
    Check(saved.EnableLanSync == !expectDev && saved.EnableWebDavSync == (!expectDev && expectedWebDav), "Sync policy failed after save/reload");
    Check(saved.LaunchAtStartup == !expectDev && saved.EnableAutoUpdate == !expectDev, "Registration/update policy failed");
    // A build/test binary must never rebind the official startup entries,
    // even if compiled in Release mode without --dev.
    const string stableStartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static object? ReadStartup(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(stableStartupKey);
        return key?.GetValue(name);
    }
    var stableBefore = ReadStartup("Yanzi");
    var runtimeBefore = ReadStartup("Yanzi.Runtime");
    StartupRegistrationService.Apply(true);
    StartupRegistrationService.Apply(false);
    Check(Equals(stableBefore, ReadStartup("Yanzi")) &&
          Equals(runtimeBefore, ReadStartup("Yanzi.Runtime")),
          "Non-installed executable unexpectedly modified production startup registration");
    if (expectDev)
    {
        Check(!saved.PersonalSync.Enabled, "Dev enabled personal sync");
        Check(!SyncConfigLoader.Load().IsConfigured, "Dev connected to production cloud");
        InputHookService.Start(() => { });
        KeyboardDoubleTapService.Start(_ => { });
        YarnSelectService.Start(_ => { });
        MouseGestureService.Start();
        Check(!InputHookService.IsRunning && !KeyboardDoubleTapService.IsRunning && !MouseGestureService.IsRunning,
            "Dev enabled global input hooks");
        static object? RegistryValue(string path, string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name);
        }
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string protocolKey = @"Software\Classes\yanzi\shell\open\command";
        var startupBefore = RegistryValue(runKey, "Yanzi");
        var protocolBefore = RegistryValue(protocolKey, "");
        StartupRegistrationService.Apply(true);
        StartupRegistrationService.Apply(false);
        UriProtocolRegistrationService.EnsureRegistered(Environment.ProcessPath!);
        UriProtocolRegistrationService.Unregister();
        Check(Equals(startupBefore, RegistryValue(runKey, "Yanzi")), "Dev modified Stable startup registration");
        Check(Equals(protocolBefore, RegistryValue(protocolKey, "")), "Dev modified Stable URI registration");
    }
}
finally
{
    if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
}

// Exercise both identities simultaneously without taking the real applications' locks.
var prefix = "Yanzi.ProfileCheck." + Guid.NewGuid().ToString("N");
using var stable = new SingleInstanceService(prefix);
using var dev = new SingleInstanceService(prefix + ".Dev");
using var duplicate = new SingleInstanceService(prefix + ".Dev");
Check(stable.TryAcquirePrimaryInstance(), "Stable could not acquire its lock");
Check(dev.TryAcquirePrimaryInstance(), "Dev conflicted with Stable");
Check(!duplicate.TryAcquirePrimaryInstance(), "Same channel allowed duplicate instances");
var stableMessage = new TaskCompletionSource<string>();
var devMessage = new TaskCompletionSource<string>();
stable.StartServer(message => { stableMessage.TrySetResult(message); return Task.CompletedTask; });
dev.StartServer(message => { devMessage.TrySetResult(message); return Task.CompletedTask; });
Check(await stable.SendToPrimaryInstanceAsync("stable"), "Stable pipe failed");
Check(await dev.SendToPrimaryInstanceAsync("dev"), "Dev pipe failed");
Check(await stableMessage.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "stable", "Stable message crossed channels");
Check(await devMessage.Task.WaitAsync(TimeSpan.FromSeconds(3)) == "dev", "Dev message crossed channels");
Console.WriteLine($"Runtime profile verification PASSED: {(expectDev ? "Dev" : "Stable")}");
