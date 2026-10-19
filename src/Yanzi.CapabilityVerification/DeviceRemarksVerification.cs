using System.Reflection;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class DeviceRemarksVerification
{
    public static void Run()
    {
        var resolve = typeof(YanziDeviceCapabilityProvider).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Device resolver missing");
        var rows = new[] {
            new PeerDeviceInfo("remark-test-a", "Company PC", "desktop", ModelName: "Windows · TEST-MODEL-A", Remark: "Company PC"),
            new PeerDeviceInfo("remark-test-b", "Home PC", "desktop", ModelName: "Windows · TEST-MODEL-B", Remark: "Home PC")
        };
        PeerDeviceInfo Find(string name, PeerDeviceInfo[]? devices = null) =>
            (PeerDeviceInfo)resolve.Invoke(null, new object[] { devices ?? rows, name })!;
        void Check(bool value) { if (!value) throw new InvalidOperationException("Device remark verification failed"); }
        Check(Find("Company PC").DeviceId == "remark-test-a");
        Check(Find("windows · test-model-b").DeviceId == "remark-test-b");
        Check(Find("remark-test-b").DeviceId == "remark-test-b");
        var duplicate = new[] { rows[0], rows[1] with { DisplayName = "Company PC", Remark = "Company PC" } };
        try { Find("Company PC", duplicate); throw new InvalidOperationException("Duplicate remark was accepted"); }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { }
        var idCollision = new[] { rows[0], rows[1] with { DisplayName = "remark-test-a" } };
        Check(Find("remark-test-a", idCollision).DeviceId == "remark-test-a");
        var providers = YanziDeviceCapabilityProvider.Create().ToArray();
        Check(providers.Any(item => item.Name == "device.remark.set"));
        foreach (var provider in providers)
        {
            YanziCapabilitySchema.ValidateDefinition(provider.InputSchema);
            YanziCapabilitySchema.ValidateDefinition(provider.OutputSchema);
        }
        Console.WriteLine("Device remarks verification: 6 checks passed.");
    }
}
