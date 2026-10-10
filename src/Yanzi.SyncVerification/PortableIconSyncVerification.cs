using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class PortableIconSyncVerification
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "yanzi-icon-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            var iconBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL/nwAAAABJRU5ErkJggg==");
            File.WriteAllBytes(Path.Combine(source, "icon.png"), iconBytes);
            File.WriteAllText(Path.Combine(source, "manifest.json"),
                "{\"id\":\"portable-icon-verification\",\"name\":\"Icon test\",\"version\":\"0.2.5\",\"icon\":\"icon.png\"}");

            var cloudUrl = "https://sync.example.invalid/v1/me/extensions/portable-icon-verification/icon?v=12345";
            var command = new CommandItem("T", "Icon test", "", "Extension", "#000000", null, [],
                source: CommandSource.LocalExtension,
                extensionId: "portable-icon-verification",
                extensionDirectoryPath: source,
                iconReference: "icon.png");
            var package = ExtensionPackageService.BuildPackage(command, "0.2.5", cloudUrl,
                includeUserShortcut: false);
            using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
            {
                var manifest = zip.GetEntry("manifest.json")
                    ?? throw new Exception("Portable package manifest missing");
                using var reader = new StreamReader(manifest.Open());
                using var document = JsonDocument.Parse(reader.ReadToEnd());
                if (document.RootElement.GetProperty("icon").GetString() != "icon.png" ||
                    zip.GetEntry("icon.png") == null)
                    throw new Exception("Cloud icon URL replaced an embedded portable icon");
            }

            // Older account archives already contain the private URL and the image bytes.
            // Installation must normalize them without an unauthenticated HTTP request.
            byte[] legacy;
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifest = zip.CreateEntry("manifest.json");
                    await using (var writer = manifest.Open())
                    {
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                        {
                            id = "portable-icon-verification",
                            name = "Icon test",
                            version = "0.2.5",
                            icon = cloudUrl
                        }));
                        await writer.WriteAsync(bytes);
                    }
                    var icon = zip.CreateEntry("icon.png");
                    await using var entryStream = icon.Open();
                    await entryStream.WriteAsync(iconBytes);
                }
                legacy = stream.ToArray();
            }

            using (HostAssets.UseIsolatedDataRootForVerification(Path.Combine(root, "device")))
            {
                var result = await ExtensionInstallService.InstallPackageAsync(
                    legacy, "portable-icon-verification");
                using var restored = JsonDocument.Parse(File.ReadAllText(
                    Path.Combine(result.DirectoryPath, "manifest.json")));
                if (restored.RootElement.GetProperty("icon").GetString() != "icon.png" ||
                    !File.ReadAllBytes(Path.Combine(result.DirectoryPath, "icon.png")).SequenceEqual(iconBytes))
                    throw new Exception("Legacy account icon failed to resolve to packaged bytes");
            }
            // A private account icon without a bundled image must use the signed-in
            // account transport rather than an anonymous HTTP request.
            byte[] remoteOnly;
            var remoteId = "remote-only-icon-verification";
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifest = zip.CreateEntry("manifest.json");
                    await using var writer = manifest.Open();
                    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        id = remoteId,
                        name = "Remote-only icon",
                        version = "0.2.5",
                        icon = "https://sync.example.invalid/v1/me/extensions/" + remoteId + "/icon?v=1"
                    }));
                    await writer.WriteAsync(body);
                }
                remoteOnly = stream.ToArray();
            }
            using (HostAssets.UseIsolatedDataRootForVerification(Path.Combine(root, "remote-device")))
            {
                var fetched = 0;
                var installed = await ExtensionInstallService.InstallPackageAsync(remoteOnly, remoteId,
                    privateIconDownload: (requestedId, _) =>
                    {
                        if (requestedId != remoteId)
                            throw new Exception("Authenticated icon requested for the wrong extension");
                        fetched++;
                        return Task.FromResult<(byte[] Content, string? ContentType)>((iconBytes, "image/png"));
                    });
                using var restored = JsonDocument.Parse(File.ReadAllText(
                    Path.Combine(installed.DirectoryPath, "manifest.json")));
                if (fetched != 1 || restored.RootElement.GetProperty("icon").GetString() != "icon.png" ||
                    !File.ReadAllBytes(Path.Combine(installed.DirectoryPath, "icon.png")).SequenceEqual(iconBytes))
                    throw new Exception("Authenticated private icon was not materialized for offline use");
            }

            if (ExtensionPackageService.ResolvePortableIconReference(source,
                "https://example.invalid/favicon.ico") != null)
                throw new Exception("Unrelated external URL incorrectly hijacked by a local file");
            Console.WriteLine("Portable icon sync passed: cloud metadata separation, offline package icon, legacy 401 URL recovery.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
