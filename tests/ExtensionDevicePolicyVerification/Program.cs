using OpenQuickHost.Sync;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "yanzi-device-policy-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
OpenQuickHost.HostAssets.ExtensionsPath = root;
DeviceIdentityStore.DeviceId = "desktop-66655fb4194741b3a3d8e82310551759";
var laptop = "desktop-1796efff0045464481585e629e2da232";
var assertions = 0;
void Check(bool success, string label) { assertions++; if (!success) throw new InvalidOperationException("FAIL: " + label); }
try
{
    var old = ExtensionDevicePolicy.FromManifestJson("{\"id\":\"legacy\"}");
    Check(old.AllowsDevice(DeviceIdentityStore.DeviceId) && old.AllowsDevice(laptop),"old apps remain shared");
    Check(old.SyncsPackage && old.SyncsData,"old apps remain cloud shared");

    string json = JsonSerializer.Serialize(new
    {
        id = "video-resume",
        devicePolicy = new { mode = "selected", deviceIds = new[] { DeviceIdentityStore.DeviceId },
                             packageSync = "targets", dataSync = "device" }
    });
    byte[] Package(string? manifest)
    {
        using var bytes = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bytes,
                   System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            if (manifest != null)
            {
                var entry = zip.CreateEntry("manifest.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(manifest);
            }
        }
        return bytes.ToArray();
    }
    Check(ExtensionDevicePolicy.CanSyncInstallArchive(Package(json)),
        "target desktop accepts account archive");
    Check(!ExtensionDevicePolicy.CanSyncInstallArchive(Package(null)),
        "missing manifest archive rejected");
    var policy = ExtensionDevicePolicy.FromManifestJson(json);
    Check(policy.AllowsDevice(DeviceIdentityStore.DeviceId),"desktop allowed");
    Check(!policy.AllowsDevice(laptop),"laptop denied");
    Check(policy.SyncsPackage && !policy.SyncsData,"package backup allowed, data isolated");
    Check(ExtensionDevicePolicy.CanInstall(json),"desktop can install");
    Check(ExtensionDevicePolicy.CanSyncInstall(json),"desktop can sync-install");
    DeviceIdentityStore.DeviceId = laptop;
    Check(!ExtensionDevicePolicy.CanInstall(json),"laptop may not install");
    Check(!ExtensionDevicePolicy.CanSyncInstall(json),"laptop may not sync-install");
    DeviceIdentityStore.DeviceId = policy.DeviceIds[0];
    DeviceIdentityStore.DeviceId = laptop;
    Check(!ExtensionDevicePolicy.CanSyncInstallArchive(Package(json)),
        "laptop rejects targeted account archive even without index metadata");
    DeviceIdentityStore.DeviceId = policy.DeviceIds[0];

    var dir = Path.Combine(root,"video-resume");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir,"manifest.json"),json);
    Check(ExtensionDevicePolicy.CanRun("video-resume"),"desktop local scope allowed");
    Check(ExtensionDevicePolicy.ShouldSyncPackage("video-resume"),"desktop package sync allowed");
    Check(!ExtensionDevicePolicy.ShouldSyncData("video-resume"),"desktop data never reaches cloud");
    DeviceIdentityStore.DeviceId = laptop;
    Check(!ExtensionDevicePolicy.CanRun("video-resume"),"laptop does not run when same package installed");
    Check(!ExtensionDevicePolicy.ShouldSyncPackage("video-resume"),"laptop never uploads a targeted package");
    Check(!ExtensionDevicePolicy.ShouldSyncData("video-resume"),"laptop data cannot sync");
    DeviceIdentityStore.DeviceId = policy.DeviceIds[0];

    var localOnly = json.Replace("\"targets\"","\"none\"");
    File.WriteAllText(Path.Combine(dir,"manifest.json"),localOnly);
    Check(ExtensionDevicePolicy.CanRun("video-resume"),"local only runs on allowed desktop");
    Check(!ExtensionDevicePolicy.ShouldSyncPackage("video-resume"),"local only excludes package cloud");
    Check(!ExtensionDevicePolicy.CanSyncInstall(localOnly),"local only not automatically installed via cloud");
    Check(ExtensionDevicePolicy.CanInstall(localOnly),"manual install still possible on target");

    // Same shared code on two PCs, but every PC persists its own data.
    var sharedCodePrivateData = new LocalExtensionDevicePolicy {
        Mode = "all", PackageSync = "all", DataSync = "device" };
    Check(sharedCodePrivateData.AllowsDevice(DeviceIdentityStore.DeviceId),
        "shared code visible on desktop");
    Check(sharedCodePrivateData.AllowsDevice(laptop),
        "shared code visible on laptop");
    Check(sharedCodePrivateData.SyncsPackage && !sharedCodePrivateData.SyncsData,
        "shared code never mixes device-local data");

    // A restricted miniapp can also intentionally allow both known PCs.
    var twoDevicePolicy = new LocalExtensionDevicePolicy {
        Mode = "selected", DeviceIds = new[] { DeviceIdentityStore.DeviceId, laptop },
        PackageSync = "targets", DataSync = "shared" };
    Check(twoDevicePolicy.AllowsDevice(DeviceIdentityStore.DeviceId) &&
        twoDevicePolicy.AllowsDevice(laptop), "two target device allowlist");
    Check(!twoDevicePolicy.AllowsDevice("desktop-unknown"),
        "unknown workstation excluded");

    var unknownData = new LocalExtensionDevicePolicy { DataSync = "unknown" };
    Check(!unknownData.SyncsData, "unknown data mode fails closed");

    var corrupt = ExtensionDevicePolicy.FromManifestJson("{\"devicePolicy\":[]}");
    Check(!corrupt.AllowsDevice(DeviceIdentityStore.DeviceId),"invalid policy denied");
    Check(!ExtensionDevicePolicy.CanRun(".."),"directory traversal denied");
    Check(!ExtensionDevicePolicy.CanRun("foo/bar"),"nested path denied");
    Check(new LocalExtensionDevicePolicy { Mode = "selected" }.AllowsDevice(DeviceIdentityStore.DeviceId) == false,
        "empty device allowlist defaults deny");
    Check(new LocalExtensionDevicePolicy { Mode = "unknown" }.AllowsDevice(DeviceIdentityStore.DeviceId) == false,
        "unknown mode defaults deny");

    Console.WriteLine($"EXTENSION_DEVICE_POLICY_PASS ({assertions} assertions)");
}
finally { Directory.Delete(root,recursive:true); }

namespace OpenQuickHost { internal static class HostAssets { public static string ExtensionsPath { get; set; } = ""; } }
namespace OpenQuickHost.Sync
{
    public static class DeviceIdentityStore
    {
        public static string DeviceId { get; set; } = "";
        public static string GetOrCreateDesktopDeviceId() => DeviceId;
    }
}