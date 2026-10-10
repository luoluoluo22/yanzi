using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class ExtensionDataCatalogVerification
{
    public static async Task RunAsync()
    {
        var backend = new MemoryBackend();
        var desktop = new PersonalSyncService(backend);
        var notes = "yanzi-notes";
        var first = await desktop.WriteExtensionDataTextAsync(notes, "notes/article/one.md", "first");
        if (!first.Confirmed) throw new Exception("Object upload not confirmed");
        var laptop = new PersonalSyncService(backend);
        if (!(await laptop.GetExtensionDataKeysAsync(notes)).Contains("notes/article/one.md"))
            throw new Exception("Fresh-device catalog cannot discover previously written text");

        await desktop.PublishExtensionDataKeysAsync(notes, ["notes/article/two.md"]);
        var keys = await laptop.GetExtensionDataKeysAsync(notes);
        if (keys.Count != 2 || !keys.Contains("notes/article/one.md"))
            throw new Exception("Catalog merge lost a previously indexed key");

        var original = new byte[] { 137, 80, 78, 71, 1, 2 };
        if (!await desktop.PublishExtensionBinaryAssetIfAbsentAsync(notes, "notes/assets/a.png", original))
            throw new Exception("First binary publish failed");
        if (await laptop.PublishExtensionBinaryAssetIfAbsentAsync(notes, "notes/assets/a.png", [9, 9]))
            throw new Exception("Divergent binary was silently overwritten");
        if (!(await laptop.TryReadExtensionBinaryAssetAsync(notes, "notes/assets/a.png"))!.SequenceEqual(original))
            throw new Exception("Divergent binary altered original cloud bytes");
        var encodedDataUri = System.Text.Encoding.UTF8.GetBytes("data:image/png;base64,QQ==");
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(encodedDataUri).ToArray();
        if (!PersonalSyncService.BinaryAssetContentMatches(encodedDataUri, withBom))
            throw new Exception("BOM-only difference in text-encoded image was misclassified");
        if (PersonalSyncService.BinaryAssetContentMatches(encodedDataUri, System.Text.Encoding.UTF8.GetBytes("data:image/png;base64,Qg==")))
            throw new Exception("Image content divergence was ignored");
        if (!ExtensionStorageService.IsPortableDataForExtension("inspiration-board", "boards/我的灵感.yzboard") ||
            ExtensionStorageService.IsPortableDataForExtension("inspiration-board", "workspace-config.json"))
            throw new Exception("Board synchronization scope is incorrect");

        if (ExtensionStorageService.IsPortableExtensionFile("notes/sync-backups/backup.md"))
            throw new Exception("Internal backup was classified as syncable");
        if (!ExtensionStorageService.IsPortableExtensionFile("notes/assets/a.png"))
            throw new Exception("Binary note attachment is missing from restore coverage");
        if (!ExtensionStorageService.IsPortableDataForExtension("yanzi-album", "作品/example.jpg") ||
            !ExtensionStorageService.IsPortableDataForExtension("yanzi-album", "桌面钉图/pinned.png") ||
            ExtensionStorageService.IsPortableDataForExtension("yanzi-album", "private/session.json"))
            throw new Exception("Album personal assets must be portable without leaking device state");
        if (!ExtensionStorageService.IsPortableDataForExtension("taskbar-calendar", "calendar_reminders.json") ||
            ExtensionStorageService.IsPortableDataForExtension("taskbar-calendar", "calendar_cache.json") ||
            ExtensionStorageService.IsPortableDataForExtension("taskbar-calendar", "calendar_reminders.json.tmp"))
            throw new Exception("Calendar recovery must include primary reminders, not device-local state");
        const string job = "ext_e9e37068c8284b1e808f5454181fb418";
        if (!ExtensionStorageService.IsPortableDataForExtension(job, "resume.json") ||
            !ExtensionStorageService.IsPortableDataForExtension(job, "daily/2026-10-10.json") ||
            !ExtensionStorageService.IsPortableDataForExtension(job, "zhaopin-resume-history/20261006.json") ||
            ExtensionStorageService.IsPortableDataForExtension(job, "message-state.json") ||
            ExtensionStorageService.IsPortableDataForExtension(job, "zhaopin-resume-raw.json") ||
            ExtensionStorageService.IsPortableDataForExtension("chatgpt-bridge", "api-token.txt") ||
            ExtensionStorageService.IsPortableDataForExtension("chatgpt-agent-bridge", "state.json") ||
            ExtensionStorageService.IsPortableDataForExtension("yanzi-notes", "api-token.txt") ||
            ExtensionStorageService.IsPortableDataForExtension("mirror", "mirror_records.json") ||
            ExtensionStorageService.IsPortableDataForExtension("taskbar-calendar", "calendar_reminders.json.sync.json"))
            throw new Exception("Job agent portable scope includes device state or misses user data");
        if (!GiteePersonalSyncBackend.IsRetryableGiteeReadStatus(System.Net.HttpStatusCode.BadGateway) ||
            !GiteePersonalSyncBackend.IsRetryableGiteeReadStatus(System.Net.HttpStatusCode.TooManyRequests) ||
            GiteePersonalSyncBackend.IsRetryableGiteeReadStatus(System.Net.HttpStatusCode.Unauthorized) ||
            GiteePersonalSyncBackend.IsRetryableGiteeReadStatus(System.Net.HttpStatusCode.NotFound))
            throw new Exception("Gitee read retry classification is unsafe");
        Console.WriteLine("Extension data catalog verification passed: discovery, merging, assets, scoped personal data, backup exclusion");
    }

    private sealed class MemoryBackend : IPersonalSyncBackend
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        public string DisplayRoot => "memory";
        public Task ProbeAsync(CancellationToken _) => Task.CompletedTask;
        public Task<byte[]?> TryReadBytesAsync(string path, CancellationToken _) =>
            Task.FromResult(_files.TryGetValue(path, out var value) ? value.ToArray() : null);
        public Task WriteBytesAsync(string path, byte[] data, string contentType, CancellationToken _)
        {
            _files[path] = data.ToArray();
            return Task.CompletedTask;
        }
        public Task DeleteFileAsync(string path, CancellationToken _)
        {
            _files.Remove(path);
            return Task.CompletedTask;
        }
    }
}
