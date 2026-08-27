using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

VerifySyncCoverageCatalog();
VerifyAiSecretBoundary();
VerifyYanmObjectStore();
VerifyPersonalRestorePoint();
VerifyExtensionAuthoritySelection();
VerifyExtensionDataObjects();

var updatedAtUtc = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc);
var original = CreateSnapshot();
var initialWrites = AccountConfigObjectStore.PrepareWrites(original, updatedAtUtc, [], []);

Assert(initialWrites.All(static item => item.ObjectId != "quickPanel.groups"), "Account writes must retire the aggregate quick-panel object.");
Assert(initialWrites.All(static item => item.ObjectId != "radialMenu.pages"), "Account writes must retire the aggregate radial-page object.");
Assert(initialWrites.Count(static item => item.ObjectId.StartsWith(AccountConfigObjectStore.QuickPanelGlobalPrefix, StringComparison.Ordinal)) == 2, "Expected two global group objects.");
Assert(initialWrites.Count(static item => item.ObjectId.StartsWith(AccountConfigObjectStore.QuickPanelContextPrefix, StringComparison.Ordinal)) == 1, "Expected one context group object.");
Assert(initialWrites.Count(static item => item.ObjectId.StartsWith(AccountConfigObjectStore.RadialMenuPagePrefix, StringComparison.Ordinal)) == 2, "Expected two radial page objects.");

var roundTrip = AccountConfigObjectStore.Apply(new CloudQuickPanelConfigSnapshot(), initialWrites.Select(static item => item.Envelope));
Assert(roundTrip != null, "Dynamic object round trip returned null.");
Assert(roundTrip!.QuickPanelGlobalGroups.Select(static item => item.Id).SequenceEqual(["g1", "g2"]), "Global group order or IDs changed during round trip.");
Assert(roundTrip.QuickPanelContextGroups.Single().Id == "c1", "Context group was not restored.");
Assert(roundTrip.RadialMenu?.Pages.Select(static item => item.Id).SequenceEqual(["r1", "r2"]) == true, "Radial page order or IDs changed during round trip.");

var initialMap = initialWrites.ToDictionary(static item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
var knownDynamicIds = initialWrites
    .Where(static item => AccountConfigObjectStore.IsDynamicObjectId(item.ObjectId) && !item.Envelope.Deleted)
    .Select(static item => item.ObjectId)
    .ToArray();

var edited = Clone(original);
edited.QuickPanelGlobalGroups[0].Name = "Global One Edited";
var editedWrites = AccountConfigObjectStore.PrepareWrites(edited, updatedAtUtc.AddMinutes(1), initialMap.Keys, knownDynamicIds);
var changedPayloadIds = editedWrites
    .Where(item => initialMap.TryGetValue(item.ObjectId, out var previous) &&
                   !LauncherConfigObjectStore.HasEquivalentPayload(item.Envelope, previous.Envelope))
    .Select(static item => item.ObjectId)
    .ToArray();
Assert(changedPayloadIds.Length == 1 && changedPayloadIds[0].StartsWith(AccountConfigObjectStore.QuickPanelGlobalPrefix, StringComparison.Ordinal), "Editing one group must not rewrite unrelated groups or pages.");

var reduced = Clone(original);
reduced.QuickPanelGlobalGroups.RemoveAll(static item => item.Id == "g2");
reduced.RadialMenu!.Pages.RemoveAll(static item => item.Id == "r2");
var unknownRemoteId = AccountConfigObjectStore.QuickPanelGlobalPrefix + new string('a', 64);
var deletionWrites = AccountConfigObjectStore.PrepareWrites(
    reduced,
    updatedAtUtc.AddMinutes(2),
    initialMap.Keys.Append(unknownRemoteId).Append("quickPanel.groups").Append("radialMenu.pages"),
    knownDynamicIds);
var tombstoneIds = deletionWrites.Where(static item => item.Envelope.Deleted).Select(static item => item.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
var removedDynamicIds = knownDynamicIds.Except(
    deletionWrites.Where(static item => AccountConfigObjectStore.IsDynamicObjectId(item.ObjectId) && !item.Envelope.Deleted).Select(static item => item.ObjectId),
    StringComparer.OrdinalIgnoreCase).ToArray();
Assert(removedDynamicIds.Length == 2 && removedDynamicIds.All(tombstoneIds.Contains), "Explicitly removed group/page must produce tombstones.");
Assert(!tombstoneIds.Contains(unknownRemoteId), "A remote-only object must never be tombstoned by a stale local snapshot.");
Assert(tombstoneIds.Contains("quickPanel.groups") && tombstoneIds.Contains("radialMenu.pages"), "Retired aggregate objects must be tombstoned during migration.");

var finalMap = initialWrites.ToDictionary(static item => item.ObjectId, static item => item.Envelope, StringComparer.OrdinalIgnoreCase);
foreach (var write in deletionWrites) finalMap[write.ObjectId] = write.Envelope;
var afterDeletion = AccountConfigObjectStore.Apply(new CloudQuickPanelConfigSnapshot(), finalMap.Values);
Assert(afterDeletion?.QuickPanelGlobalGroups.Select(static item => item.Id).SequenceEqual(["g1"]) == true, "Deleted group reappeared after applying tombstones.");
Assert(afterDeletion?.RadialMenu?.Pages.Select(static item => item.Id).SequenceEqual(["r1"]) == true, "Deleted radial page reappeared after applying tombstones.");

Console.WriteLine("Account config object verification passed: round-trip, isolated edits, safe tombstones, remote-only preservation.");

static void VerifySyncCoverageCatalog()
{
    var unclassified = SyncCoverageCatalog.FindUnclassifiedAppSettingsProperties();
    Assert(unclassified.Count == 0,
        $"AppSettings properties missing a sync policy: {string.Join(", ", unclassified)}");

    var unknown = SyncCoverageCatalog.FindUnknownCatalogProperties();
    Assert(unknown.Count == 0,
        $"Sync coverage catalog contains unknown properties: {string.Join(", ", unknown)}");

    var contractGaps = SyncCoverageCatalog.FindAccountSnapshotContractGaps();
    Assert(contractGaps.Count == 0,
        $"Account-synced properties missing from snapshot contract: {string.Join(", ", contractGaps)}");

    Console.WriteLine($"Sync coverage verification passed: {SyncCoverageCatalog.Entries.Count} AppSettings properties classified.");
}

static void VerifyAiSecretBoundary()
{
    var localProvider = new AiServiceProviderSettings
    {
        Id = "provider-a",
        Name = "Provider A",
        BaseUrl = "https://example.invalid",
        ApiKey = "secret-a"
    };
    var syncedProviders = AiCredentialStore.PrepareSyncedProviderMetadata([localProvider]);
    Assert(syncedProviders.Single().ApiKey.Length == 0, "AI provider API key leaked into synchronized metadata.");
    Assert(localProvider.ApiKey == "secret-a", "Preparing AI metadata mutated the live local credential.");

    var local = new AppSettings
    {
        AiApiKey = "legacy-local-secret",
        AiServiceProviders = [localProvider]
    };
    var incoming = new AppSettings
    {
        AiApiKey = string.Empty,
        AiServiceProviders =
        [
            new AiServiceProviderSettings { Id = "provider-a", Name = "Renamed", ApiKey = string.Empty }
        ]
    };
    AiCredentialStore.PreserveLocalSecrets(local, incoming);
    Assert(incoming.AiApiKey == "legacy-local-secret" && incoming.AiServiceProviders[0].ApiKey == "secret-a",
        "Applying synchronized AI metadata discarded a protected local credential.");

    using var cachedPayloadDocument = JsonDocument.Parse("""
        {"aiApiKey":"legacy","aiServiceProviders":[{"id":"provider-a","apiKey":"nested-secret"}],"model":"safe"}
        """);
    var scrubbed = CloudObjectSyncStateStore.RemoveSensitiveAiFields(cachedPayloadDocument.RootElement, out var changed);
    Assert(changed && scrubbed.GetProperty("aiApiKey").GetString() == string.Empty &&
           scrubbed.GetProperty("aiServiceProviders")[0].GetProperty("apiKey").GetString() == string.Empty &&
           scrubbed.GetProperty("model").GetString() == "safe",
        "Legacy AI cache scrub did not remove only sensitive fields.");
    Console.WriteLine("AI credential boundary verification passed: synced metadata contains no API keys and local secrets survive merges.");
}

static void VerifyYanmObjectStore()
{
    var updatedAtUtc = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
    var original = new YanmSettings
    {
        Enabled = true,
        GridSizePixels = 12,
        Components = [new YanmComponentSettings { Id = "note", Title = "Note" }],
        ComponentState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["note::text"] = "hello",
            ["timer::elapsed"] = "42"
        }
    };
    var initial = YanmObjectStore.PrepareWrites(original, updatedAtUtc, [], []);
    Assert(initial.Count(static item => YanmObjectStore.IsDynamicObjectId(item.ObjectId) && !item.Envelope.Deleted) == 2,
        "Yanm component-state keys were not split into independent objects.");
    var layout = initial.Single(static item => item.ObjectId == YanmObjectStore.LayoutObjectId);
    var layoutPayload = layout.Envelope.Payload.Deserialize<YanmLayoutObjectPayload>(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
    Assert(layoutPayload?.Settings?.ComponentState.Count == 0, "Yanm layout object must not contain component state.");

    var roundTrip = YanmObjectStore.Apply(new YanmSettings(), initial.Select(static item => item.Envelope), out var applied, out _);
    Assert(applied && roundTrip.Enabled && roundTrip.ComponentState.Count == 2 &&
           roundTrip.ComponentState["note::text"] == "hello",
        "Yanm object round trip lost layout or component state.");

    var initialMap = initial.ToDictionary(static item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
    var knownIds = initial.Where(static item => YanmObjectStore.IsDynamicObjectId(item.ObjectId) && !item.Envelope.Deleted)
        .Select(static item => item.ObjectId).ToArray();
    var edited = JsonSerializer.Deserialize<YanmSettings>(JsonSerializer.Serialize(original))!;
    edited.ComponentState["note::text"] = "edited";
    var editedWrites = YanmObjectStore.PrepareWrites(edited, updatedAtUtc.AddMinutes(1), initialMap.Keys, knownIds);
    var changedIds = editedWrites.Where(item => initialMap.TryGetValue(item.ObjectId, out var previous) &&
                                                !LauncherConfigObjectStore.HasEquivalentPayload(item.Envelope, previous.Envelope))
        .Select(static item => item.ObjectId).ToArray();
    Assert(changedIds.Length == 1 && YanmObjectStore.IsDynamicObjectId(changedIds[0]),
        "Editing one Yanm state key rewrote layout, index, or unrelated state keys.");

    var reduced = JsonSerializer.Deserialize<YanmSettings>(JsonSerializer.Serialize(original))!;
    reduced.ComponentState.Remove("timer::elapsed");
    var remoteOnlyId = YanmObjectStore.ComponentStatePrefix + new string('b', 64);
    var deletionWrites = YanmObjectStore.PrepareWrites(
        reduced,
        updatedAtUtc.AddMinutes(2),
        initialMap.Keys.Append(remoteOnlyId),
        knownIds);
    var tombstones = deletionWrites.Where(static item => item.Envelope.Deleted)
        .Select(static item => item.ObjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(tombstones.Contains(YanmObjectStore.BuildComponentStateObjectId("timer::elapsed")),
        "Removing a known Yanm state key did not create a tombstone.");
    Assert(!tombstones.Contains(remoteOnlyId), "A stale client tombstoned a remote-only Yanm state object.");

    var restoredIds = new List<string>();
    Assert(MainWindow.SetIndexedObjectPresence(restoredIds, knownIds[0], true),
        "Restoring a dynamic object did not add it back to its index.");
    Assert(!MainWindow.SetIndexedObjectPresence(restoredIds, knownIds[0], true) && restoredIds.Count == 1,
        "Dynamic restore added a duplicate index member.");
    Assert(MainWindow.SetIndexedObjectPresence(restoredIds, knownIds[0], false) && restoredIds.Count == 0,
        "Restoring a tombstone did not remove the dynamic index member.");
    Console.WriteLine("Yanm object verification passed: layout isolation, per-key edits, tombstones, remote-only preservation.");
}

static void VerifyPersonalRestorePoint()
{
    var updatedAtUtc = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
    var snapshot = CreateSnapshot();
    var writes = LauncherConfigObjectStore.PrepareWrites(snapshot, updatedAtUtc).ToArray();
    var point = LauncherConfigObjectStore.CreateRestorePoint(writes, [writes[0]], updatedAtUtc, "verification");
    var bytes = LauncherConfigObjectStore.SerializeRestorePoint(point);
    var restoredPoint = LauncherConfigObjectStore.DeserializeRestorePoint(bytes);
    Assert(restoredPoint != null && restoredPoint.Objects.Count == writes.Length &&
           restoredPoint.ChangedObjectIds.SequenceEqual([writes[0].ObjectId]),
        "Personal repository restore point did not retain the complete object set and changed-object summary.");
    var restoredSnapshot = LauncherConfigObjectStore.Compose(
        null,
        restoredPoint!.Objects,
        out _,
        preferObjectsOverBase: true);
    Assert(restoredSnapshot != null && restoredSnapshot.QuickPanelGlobalGroups.Count == snapshot.QuickPanelGlobalGroups.Count &&
           restoredSnapshot.RadialMenu?.Pages.Count == snapshot.RadialMenu?.Pages.Count,
        "Personal repository restore point could not reconstruct the launcher configuration.");
    Console.WriteLine("Personal restore-point verification passed: immutable full-object snapshot round trip.");
}

static void VerifyExtensionAuthoritySelection()
{
    var local = new WebDavSyncEntry
    {
        ExtensionId = "demo",
        PackageHash = "local",
        UpdatedAtUtc = "2026-07-10T12:00:00Z",
        Revision = 20,
        Deleted = true
    };
    var remote = new WebDavSyncEntry
    {
        ExtensionId = "demo",
        PackageHash = "remote",
        UpdatedAtUtc = "2026-07-10T09:00:00Z",
        Revision = 21,
        Deleted = false
    };
    Assert(ReferenceEquals(
            PersonalSyncService.ChooseExtensionEntry(local, remote, PersonalConfigSyncMode.UploadOnlyBackup),
            local),
        "Logged-in personal backup allowed a newer repository package to override the account/local deletion.");
    Assert(ReferenceEquals(
            PersonalSyncService.ChooseExtensionEntry(local, remote, PersonalConfigSyncMode.Bidirectional),
            remote),
        "Standalone personal sync no longer selected the newer remote extension entry.");
    local.BaseRevision = 10;
    local.BasePackageHash = "base";
    local.BaseDeleted = false;
    Assert(PersonalSyncService.HasConcurrentExtensionChanges(local, remote),
        "Concurrent local deletion and remote package update was not surfaced as an extension conflict.");
    local.Revision = 10;
    local.PackageHash = "base";
    local.Deleted = false;
    Assert(!PersonalSyncService.HasConcurrentExtensionChanges(local, remote),
        "A remote-only extension update was incorrectly classified as a two-sided conflict.");
    Console.WriteLine("Extension authority verification passed: account mode is backup-only, standalone mode remains bidirectional.");
}

static void VerifyExtensionDataObjects()
{
    var first = ExtensionDataObjectStore.CreateNext("private.notes", "folders/today.json", "{\"text\":\"one\"}", null);
    var second = ExtensionDataObjectStore.CreateNext("private.notes", "folders/today.json", "{\"text\":\"two\"}", first);
    Assert(second.Revision > first.Revision, "Extension-data revision did not advance monotonically.");
    Assert(second.History.Any(item => item.VersionId == first.VersionId && item.ContentHash == first.ContentHash),
        "Extension-data head did not retain the previous immutable version reference.");
    Assert(ExtensionDataObjectStore.BuildObjectPath(first.ExtensionId, first.Key) ==
           ExtensionDataObjectStore.BuildObjectPath(second.ExtensionId, second.Key),
        "The same extension-data key did not map to a stable object path.");
    Assert(ExtensionDataObjectStore.BuildHistoryPath(first) != ExtensionDataObjectStore.BuildHistoryPath(second),
        "Distinct extension-data versions mapped to the same immutable history path.");
    var staleState = new ExtensionDataSyncState { LastRemoteRevision = first.Revision };
    Assert(ExtensionDataObjectStore.HasConcurrentChange(second, staleState, first.ContentHash),
        "A remote extension-data revision above the local baseline was not classified as concurrent.");
    staleState.LastRemoteRevision = second.Revision;
    Assert(!ExtensionDataObjectStore.HasConcurrentChange(second, staleState, first.ContentHash),
        "An edit based on the current remote extension-data revision was falsely classified as concurrent.");
    var legacy = ExtensionDataObjectStore.CreateLegacy(first.ExtensionId, first.Key, "legacy");
    Assert(ExtensionDataObjectStore.HasConcurrentChange(legacy, null, first.ContentHash),
        "Unobserved legacy extension data could be overwritten without a migration conflict.");
    var tombstone = ExtensionDataObjectStore.CreateTombstone(second.ExtensionId, second.Key, second);
    Assert(tombstone.Deleted && tombstone.Revision > second.Revision &&
           tombstone.History.Any(item => item.VersionId == second.VersionId),
        "Extension-data deletion did not create a new tombstone revision with history lineage.");
    Assert(ExtensionDataObjectStore.HasConcurrentChange(second, new ExtensionDataSyncState
           {
               LastRemoteRevision = first.Revision
           }, ExtensionDataObjectStore.ComputeContentHash(string.Empty), localDeleted: true),
        "A stale extension-data deletion could overwrite a newer remote value without conflict.");

    var roundTrip = ExtensionDataObjectStore.Deserialize(
        ExtensionDataObjectStore.Serialize(second),
        second.ExtensionId,
        second.Key);
    Assert(roundTrip?.Content == second.Content && roundTrip.ContentHash == second.ContentHash,
        "Extension-data object round trip lost or corrupted content.");

    var tampered = Clone(second);
    tampered.Content = "tampered";
    var rejectedTamper = false;
    try
    {
        _ = ExtensionDataObjectStore.Deserialize(
            ExtensionDataObjectStore.Serialize(tampered),
            tampered.ExtensionId,
            tampered.Key);
    }
    catch (InvalidDataException)
    {
        rejectedTamper = true;
    }
    Assert(rejectedTamper, "Extension-data content hash validation accepted a tampered object.");
    Console.WriteLine("Extension data verification passed: per-key revisions, immutable history references, stable paths, hash validation.");
}

static CloudQuickPanelConfigSnapshot CreateSnapshot() => new()
{
    QuickPanelSlots = Enumerable.Repeat<string?>(null, 28).ToList(),
    QuickPanelGlobalGroups =
    [
        new QuickPanelGroupSettings { Id = "g1", Name = "Global One", Slots = ["ext.one"] },
        new QuickPanelGroupSettings { Id = "g2", Name = "Global Two", Slots = ["ext.two"] }
    ],
    QuickPanelContextGroups =
    [
        new QuickPanelGroupSettings { Id = "c1", Name = "Context One", ContextProcessName = "code", Slots = ["ext.context"] }
    ],
    SelectedQuickPanelGlobalGroupId = "g1",
    SelectedQuickPanelContextGroupId = "c1",
    RadialMenu = new RadialMenuSettings
    {
        SelectedPageId = "r1",
        Pages =
        [
            new RadialMenuPageSettings { Id = "r1", Name = "Radial One", Slots = ["ext.one"] },
            new RadialMenuPageSettings { Id = "r2", Name = "Radial Two", Slots = ["ext.two"] }
        ]
    }
};

static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
