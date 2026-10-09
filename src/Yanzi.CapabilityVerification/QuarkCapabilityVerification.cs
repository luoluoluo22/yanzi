using System.Text.Json;
using OpenQuickHost;

internal static class QuarkCapabilityVerification
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool passed, string label)
        {
            if (!passed) throw new InvalidOperationException("Quark verification failed: " + label);
            checks++;
        }

        YanziBuiltinCapabilityRegistration.Register();
        var names = new[]
        {
            "quark.cloudDrive.adapterStatus",
            "quark.cloudDrive.transferStatus",
            "quark.cloudDrive.cachedLookup",
            "quark.cloudDrive.cachedFolder",
            "quark.cloudDrive.uploadVerified",
            "quark.cloudDrive.downloadVerified"
        };
        foreach (var name in names)
            Check(YanziCapabilityRegistry.TryGet(name, out var definition)
                  && definition?.ProviderExtensionId == "yanzi-host", "provider exists: " + name);

        foreach (var name in new[] { names[4], names[5] })
            Check(YanziCapabilityRegistry.TryGet(name, out var def)
                  && def!.RequiresConfirmation
                  && def.RiskLevel == "medium", "mutation confirmation metadata: " + name);

        try
        {
            await YanziCapabilityRegistry.InvokeAsync(names[0], new { });
            throw new Exception("anonymous caller unexpectedly got adapter status");
        }
        catch (UnauthorizedAccessException)
        {
            checks++;
        }

        var caller = new YanziCapabilityCaller(
            "quark-verification", ["application.read", "file.read"]);

        var probe = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[0], new { }, caller))!;
        Check(probe.GetProperty("available").GetBoolean(), "bundled python dependencies");

        var source = @"F:\Desktop\cloud-drive-eval-20261009\AI-quark-index-validation-20261009.txt";
        var status = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[1], new { path = source, kind = "upload" }, caller))!;
        Check(status.GetProperty("found").GetBoolean(), "actual upload task found");
        Check(status.GetProperty("completed").GetBoolean(), "real FINISH status");
        Check(status.GetProperty("fid").GetString()?.Length == 32, "real cloud fid");

        var candidate = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[2], new { filename = Path.GetFileName(source) }, caller))!;
        Check(candidate.GetProperty("found").GetBoolean(), "cached own test file");
        Check(!candidate.GetProperty("liveVerified").GetBoolean(), "cache not claimed live");
        Check(candidate.GetProperty("fid").GetString() == status.GetProperty("fid").GetString(),
              "cached fid agrees with actual upload");
        var parent = status.GetProperty("dirFid").GetString();
        var folder = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[3], new { parentFid = parent }, caller))!;
        Check(folder.GetProperty("count").GetInt32() >= 1, "cached parent folder");
        Check(!folder.GetProperty("liveVerified").GetBoolean(), "folder marked nonlive");

        try
        {
            await YanziCapabilityRegistry.InvokeAsync(names[4], new { path = source },
                new YanziCapabilityCaller("write-fixture", [
                    "application.run", "file.read", "network.write"]));
            throw new Exception("upload without confirm unexpectedly authorized");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            checks++;
        }

        Console.WriteLine("QUARK_CAPABILITY_VERIFICATION=PASS checks=" + checks);
    }
}
