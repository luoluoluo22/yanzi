using System.Text.Json;
using OpenQuickHost;

internal static class BaiduTransferCapabilityVerification
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string why)
        {
            if (!condition)
                throw new InvalidOperationException("Baidu verification failed: " + why);
            checks++;
        }

        YanziBuiltinCapabilityRegistration.Register();
        var names = new[]
        {
            "baiduNetdisk.transferStatus",
            "baiduNetdisk.roundtripVerify"
        };
        foreach (var name in names)
        {
            Check(YanziCapabilityRegistry.TryGet(name, out var def)
                  && def!.ProviderExtensionId == "yanzi-host", "registered " + name);
            Check(def!.RiskLevel == "low" && !def.RequiresConfirmation,
                  "correct read-only metadata " + name);
            Check(def.Permissions.Contains("file.read"), "requires file.read");
        }

        var caller = new YanziCapabilityCaller(
            "baidu-verification", ["application.read", "file.read"]);

        var source = @"F:\Desktop\cloud-drive-eval-20261009\AI-baidu-desktop-roundtrip-20261009.txt";
        var downloaded = @"F:\Backup\Downloads\AI-baidu-desktop-roundtrip-20261009.txt";

        var upload = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[0], new { kind = "upload", path = source }, caller))!;
        Check(upload.GetProperty("found").GetBoolean(), "real upload record");
        Check(upload.GetProperty("completed").GetBoolean(), "real uploaded FINISH");
        Check(upload.GetProperty("size").GetInt32() == 107, "upload size");
        Check(upload.GetProperty("errorCode").GetInt32() == 0, "upload error code zero");

        var download = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[0], new { kind = "download", path = downloaded }, caller))!;
        Check(download.GetProperty("found").GetBoolean(), "real download record");
        Check(download.GetProperty("completed").GetBoolean(), "download FINISH");
        Check(download.GetProperty("size").GetInt32() == 107, "download size");
        Check(download.GetProperty("cloudPath").GetString()
              == upload.GetProperty("cloudPath").GetString(), "same cloud path");

        var proof = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[1], new { originalLocalFile = source, downloadedFile = downloaded },
            caller))!;
        Check(proof.GetProperty("confirmed").GetBoolean(), "real complete roundtrip");
        Check(proof.GetProperty("bytes").GetInt32() == 107, "roundtrip bytes");
        Check(proof.GetProperty("sha256").GetString()?.Length == 64,
              "sha256 evidence");
        Check(proof.GetProperty("liveCloudExistenceVerified").GetBoolean() == false,
              "history is not live cloud existence");

        try
        {
            await YanziCapabilityRegistry.InvokeAsync(names[0],
                new { kind = "upload", path = source });
            throw new InvalidOperationException("Anonymous read unexpectedly authorized");
        }
        catch (UnauthorizedAccessException)
        {
            checks++;
        }

        Console.WriteLine("BAIDU_TRANSFER_CAPABILITY_VERIFICATION=PASS checks=" + checks);
    }
}
