using System.Security.Cryptography;
using System.Text;
using OpenQuickHost.Sync;

static void Assert(bool good, string name)
{
    if (!good) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

const string extensionId = "yanzi-qa-index-batch";
var backend = new FakeBackend();
var service = new PersonalSyncService(backend);
var assetKeys = Enumerable.Range(0, 40).Select(i => $"photo/{i:D2}.png").ToArray();
var assetBytes = assetKeys.ToDictionary(k => k, k => Encoding.UTF8.GetBytes($"file:{k}"));
var indexPath = $"state/extension-data/key-index/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(extensionId))).ToLowerInvariant()}.json";

// Same sequence as ReconcileCloudDataAsync: publish complete set before any binary.
await service.PublishExtensionDataKeysAsync(extensionId, assetKeys);
Assert(backend.Reads(indexPath) == 2 && backend.Writes(indexPath) == 1,
    "batch index is written and verified once");
foreach (var key in assetKeys)
    Assert(await service.PublishPreIndexedExtensionBinaryAssetAsync(extensionId, key, assetBytes[key]),
        "new preindexed binary published");
Assert(backend.Reads(indexPath) == 2, "40 binary writes do not re-read index");
Assert(backend.Writes(indexPath) == 1, "40 binary writes do not rewrite index");
Assert((await service.GetExtensionDataKeysAsync(extensionId)).SequenceEqual(assetKeys),
    "all 40 binary keys discoverable for a second device");
foreach (var key in assetKeys)
    Assert((await service.TryReadExtensionBinaryAssetAsync(extensionId, key))!
        .SequenceEqual(assetBytes[key]), "binary contents preserved");

backend.ResetCounters();
await service.PublishExtensionDataKeysAsync(extensionId, assetKeys);
foreach (var key in assetKeys)
    Assert(await service.PublishPreIndexedExtensionBinaryAssetAsync(extensionId, key, assetBytes[key]),
        "unchanged asset accepted");
Assert(backend.Reads(indexPath) == 1 && backend.Writes(indexPath) == 0,
    "second unchanged sync reads index once without writing");
Assert(backend.TotalWrites == 0, "second unchanged sync never rewrites assets");

backend.ResetCounters();
var standalone = Encoding.UTF8.GetBytes("standalone");
Assert(await service.PublishExtensionBinaryAssetIfAbsentAsync(extensionId, "single.jpg", standalone),
    "standalone published with old API");
Assert(backend.Reads(indexPath) == 2 && backend.Writes(indexPath) == 1,
    "standalone API still publishes and verifies its key");
Assert((await service.GetExtensionDataKeysAsync(extensionId)).Count == 41,
    "standalone key remains discoverable");

backend.ResetCounters();
Assert(!await service.PublishPreIndexedExtensionBinaryAssetAsync(
    extensionId, "single.jpg", Encoding.UTF8.GetBytes("divergent")),
    "divergent cloud binary never overwritten");
Assert(backend.TotalWrites == 0 &&
       (await service.TryReadExtensionBinaryAssetAsync(extensionId, "single.jpg"))!
       .SequenceEqual(standalone), "remote divergent bytes preserved");

var failed = new FakeBackend { FailIndexWrites = true };
var isolated = new PersonalSyncService(failed);
try
{
    await isolated.PublishExtensionDataKeysAsync(extensionId, assetKeys);
    throw new Exception("Index confirmation gate was bypassed");
}
catch (IOException) { }
Assert(failed.TotalWrites == 0, "index failure aborts before binary publication");

Console.WriteLine("EXTENSION_DATA_INDEX_BATCH_PASS");
