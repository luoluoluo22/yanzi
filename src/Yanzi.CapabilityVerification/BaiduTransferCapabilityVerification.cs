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
            "baiduNetdisk.roundtripVerify",
            "baiduNetdisk.uploadVerified",
            "baiduNetdisk.searchExactVisible"
        };
        foreach (var name in names)
        {
            Check(YanziCapabilityRegistry.TryGet(name, out var def)
                  && def!.ProviderExtensionId == "yanzi-host", "registered " + name);
            Check(def!.Permissions.Contains("file.read"), "requires file.read");
        }
        foreach (var name in new[] { names[0], names[1], names[3] })
        {
            Check(YanziCapabilityRegistry.TryGet(name, out var def)
                  && def!.RiskLevel == "low" && !def.RequiresConfirmation,
                  "correct read-only metadata " + name);
        }

        Check(YanziCapabilityRegistry.TryGet(names[2], out var uploadVerified)
              && uploadVerified!.RequiresConfirmation
              && uploadVerified.RiskLevel == "medium", "upload confirmation metadata");
        Check(YanziCapabilityRegistry.TryGet(names[3], out var searchDef)
              && searchDef!.Permissions.Contains("network.read")
              && searchDef.Permissions.Contains("application.run")
              && !searchDef.RequiresConfirmation, "read-only cloud search permissions");

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

        var failedPath =
            @"F:\Desktop\cloud-drive-eval-20261009\AI-baidu-verified-upload-20261009-174900.txt";
        var failed = (JsonElement)(await YanziCapabilityRegistry.InvokeAsync(
            names[0], new { kind = "upload", path = failedPath }, caller))!;
        Check(failed.GetProperty("found").GetBoolean(), "failed task history visible");
        Check(failed.GetProperty("failed").GetBoolean(), "failed upload recognized");
        Check(!failed.GetProperty("completed").GetBoolean(), "failed is not complete");
        Check(failed.GetProperty("errorCode").GetInt32() == 110000,
              "original client error code preserved");

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

        try
        {
            await YanziCapabilityRegistry.InvokeAsync(names[2],
                new { path = source, confirm = false },
                new YanziCapabilityCaller("baidu-write-check", [
                    "application.run", "application.read", "file.read", "network.write"]));
            throw new InvalidOperationException("Unconfirmed upload unexpectedly authorized");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            checks++;
        }

        Console.WriteLine("BAIDU_TRANSFER_CAPABILITY_VERIFICATION=PASS checks=" + checks);
    }

    public static async Task RunLiveUploadAsync()
    {
        YanziBuiltinCapabilityRegistration.Register();
        var folder = @"F:\Desktop\cloud-drive-eval-20261009";
        var path = Path.Combine(folder,
            "AI-baidu-verified-upload-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        await File.WriteAllTextAsync(path, "Baidu UploadVerified client completion probe for YanZi.");
        var caller = new YanziCapabilityCaller("baidu-live-upload", [
            "application.run", "application.read", "file.read", "network.write"]);
        var raw = await YanziCapabilityRegistry.InvokeAsync(
            "baiduNetdisk.uploadVerified",
            new { path, confirm = true, timeoutSeconds = 45 }, caller);
        var result = JsonSerializer.SerializeToElement(raw);
        if (result.GetProperty("status").GetString() != "client_completed"
            || !result.GetProperty("clientCompleted").GetBoolean())
            throw new InvalidOperationException("Client completion not established: " + result.ToString());
        Console.WriteLine("BAIDU_UPLOAD_VERIFIED_REAL=PASS bytes=" +
            result.GetProperty("size").GetInt64() +
            " cloudPathPresent=" +
            !string.IsNullOrEmpty(result.GetProperty("cloudPath").GetString()));
    }
}
