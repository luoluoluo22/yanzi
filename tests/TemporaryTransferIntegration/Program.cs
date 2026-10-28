using System.Security.Cryptography;
using System.Text.Json;
using Yanzi.Core.TemporaryTransfer;
using OpenQuickHost;

var fileName = Path.Combine(Path.GetTempPath(), "yanzi-temporary-runtime-" + Guid.NewGuid().ToString("N") + ".bin");
var sample = RandomNumberGenerator.GetBytes(131072);
await File.WriteAllBytesAsync(fileName, sample);
var expected = Convert.ToHexString(SHA256.HashData(sample));
var selected = args.Length > 0 ? args[0] : "sg-sftp";
var configured = YanziTemporaryTransferAdapters.LoadChannels().Select(c => c.Id == selected ? c : c with { Enabled = false });
var coordinator = new TemporaryTransferCoordinator(configured,
    YanziTemporaryTransferAdapters.Create());
try
{
    var plan = new TransferRequest(TransferEndpoint.Home, TransferEndpoint.Office, sample.Length,
        AllowPublicProvider: selected != "sg-sftp", PreferredChannel: selected);
    Console.WriteLine("PLAN:" + JsonSerializer.Serialize(coordinator.Plan(plan)));
    var published = await coordinator.PublishAsync(fileName, plan, CancellationToken.None);
    Console.WriteLine("PUBLISHED:" + JsonSerializer.Serialize(published.Receipt));
    var inbox = Path.Combine(Path.GetTempPath(), "yanzi-temporary-receive-" + Guid.NewGuid().ToString("N"));
    try
    {
        var result = await coordinator.ReceiveAsync(published.Receipt, inbox, CancellationToken.None);
        if (result.Sha256 != expected || result.Bytes != sample.Length)
            throw new IOException("invalid_end_to_end_transfer");
        Console.WriteLine("REAL_PROVIDER_ROUNDTRIP_VERIFIED:" + result.Bytes + ":" + result.Sha256);
    }
    finally { Directory.Delete(inbox, recursive: true); }
}
finally { File.Delete(fileName); }

namespace OpenQuickHost
{
    public static class HostAssets
    {
        public static string ResolveDataDirectoryPath(string relative) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost", relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
