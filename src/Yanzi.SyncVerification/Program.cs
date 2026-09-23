using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

VerifySyncArchitectureSafety();
VerifySyncConflictExperience();
if (args.Contains("--sync-ux-safety")) return;
VerifyBackpackResponsiveness();
if (args.Contains("--backpack-safety")) return;
VerifySearchInteractionSafety();
VerifyQuickWindowSwitchSafety();
if (args.Contains("--interaction-safety")) return;
VerifySyncPackageSafety();
if (args.Contains("--sync-safety")) return;
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

static void VerifySyncArchitectureSafety()
{
    var compatibility = new CloudSyncCapabilitiesResponse { Ok = true, ObjectSyncAvailable = true, ObjectsAuthoritative = false };
    Assert(CloudSyncTransferPolicy.SupportsUpload(compatibility), "Compatibility mode supports conditional object uploads.");
    Assert(!CloudSyncTransferPolicy.SupportsDownload(compatibility), "Partial compatibility objects must not be treated as the only download source.");
    Assert(!CloudSyncTransferPolicy.SupportsUpload(new CloudSyncCapabilitiesResponse { Ok = true }), "Unavailable object PUT must remain blocked.");
    var oldPending = new CloudObjectPendingOperation { LastExpectedRevision = 577 };
    var knownLayout = new CloudObjectSyncCacheEntry { ObjectId = "yanm.layout", Revision = 600, UpdatedByDeviceId = "this-device", Payload = JsonSerializer.SerializeToElement(new { layout = 1 }) };
    Assert(CloudSyncTransferPolicy.UploadRevision(oldPending, knownLayout, knownLayout, "this-device") == 600,
        "An old same-device retry must use the already adopted version instead of getting stuck at 577.");
    Assert(CloudSyncTransferPolicy.UploadRevision(oldPending, knownLayout, knownLayout, "another-device") == 577,
        "Another device's data must not be silently rebased.");
    Assert(CloudSyncTransferPolicy.UploadRevision(new CloudObjectPendingOperation(), knownLayout, knownLayout, "this-device") == 0,
        "Intentional creates must still conflict if an object already exists.");
    Assert(CloudSyncTransferPolicy.UploadRevision(oldPending, knownLayout, null, "this-device") == 577,
        "A remote-only version must not become the local baseline implicitly.");

    var pendingState = new CloudObjectSyncState();
    var baseline = new Dictionary<string, CloudObjectSyncCacheEntry>
    {
        ["settings.general"] = new() { ObjectId = "settings.general", Revision = 5, Payload = JsonSerializer.SerializeToElement(new { enabled = false }) }
    };
    var write = new AccountConfigObjectWrite("settings.general", new LauncherConfigObjectEnvelope
        { ObjectId = "settings.general", Payload = JsonSerializer.SerializeToElement(new { enabled = true }) });
    CloudPendingChanges.Capture(pendingState, [write], baseline);
    Assert(pendingState.PendingOperations[write.ObjectId].LastExpectedRevision == 5, "Offline edits must retain the observed base revision.");
    baseline[write.ObjectId].Revision = 8;
    CloudPendingChanges.Capture(pendingState, [write], baseline);
    Assert(pendingState.PendingOperations[write.ObjectId].LastExpectedRevision == 5, "Downloading a new baseline must not authorize overwriting it.");
    var uploadedOnly = new CloudObjectSyncState();
    uploadedOnly.LocalBaselines[write.ObjectId] = new CloudObjectSyncCacheEntry
        { ObjectId = write.ObjectId, Revision = 5, Payload = write.Envelope.Payload };
    uploadedOnly.Objects[write.ObjectId] = new CloudObjectSyncCacheEntry
        { ObjectId = write.ObjectId, Revision = 8, Payload = JsonSerializer.SerializeToElement(new { enabled = false }) };
    CloudPendingChanges.RecordApplied(uploadedOnly, [write]);
    CloudPendingChanges.Capture(uploadedOnly, [write], uploadedOnly.LocalBaselines);
    Assert(uploadedOnly.LocalBaselines[write.ObjectId].Revision == 5 && uploadedOnly.PendingOperations.Count == 0,
        "Upload-only preflight must not turn an unapplied remote change into a new local edit.");
    var received = new AccountConfigObjectWrite(write.ObjectId, new LauncherConfigObjectEnvelope
        { ObjectId = write.ObjectId, Payload = uploadedOnly.Objects[write.ObjectId].Payload });
    CloudPendingChanges.RecordApplied(uploadedOnly, [received]);
    Assert(uploadedOnly.LocalBaselines[write.ObjectId].Revision == 8, "The local baseline advances when remote data is actually applied.");
    var freshState = new CloudObjectSyncState();
    CloudPendingChanges.Capture(freshState, [write], new Dictionary<string, CloudObjectSyncCacheEntry>());
    Assert(freshState.PendingOperations.Count == 0, "New-device defaults must not become offline edits.");
    freshState.Conflicts[write.ObjectId] = new CloudObjectConflictRecord { ObjectId = write.ObjectId };
    CloudPendingChanges.Capture(freshState, [write], baseline);
    Assert(freshState.PendingOperations.Count == 0, "Unresolved conflicts must not be resubmitted by unrelated edits.");
    var testRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yanzi-state-test-" + Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(testRoot);
    try
    {
        var accountA = new CloudObjectSyncState { UserId = "account-a" };
        accountA.Objects["settings.general"] = new CloudObjectSyncCacheEntry
            { ObjectId = "settings.general", Revision = 10, Payload = JsonSerializer.SerializeToElement(new { enabled = false }) };
        accountA.PendingObjectIds.Add("settings.general");
        accountA.PendingOperations["settings.general"] = new CloudObjectPendingOperation { ObjectId = "settings.general", LastExpectedRevision = 0 };
        CloudObjectSyncStateStore.SaveAt(testRoot, accountA);
        CloudObjectSyncStateStore.SaveAt(testRoot, new CloudObjectSyncState { UserId = "account-b" });
        Assert(CloudObjectSyncStateStore.LoadAt(testRoot, "account-a").PendingOperations.Count == 1,
            "Switching accounts must preserve the first account's offline edits.");
        Assert(CloudObjectSyncStateStore.LoadAt(testRoot, "account-a").PendingOperations["settings.general"].LastExpectedRevision == 0,
            "A persisted create operation must not silently rebase to a remote revision on load.");
        accountA.LastSyncedRevision = 9;
        CloudObjectSyncStateStore.SaveAt(testRoot, accountA);
        var accountPath = CloudObjectSyncStateStore.AccountPath(testRoot, "account-a");
        System.IO.File.WriteAllText(accountPath, "broken");
        var recovered = CloudObjectSyncStateStore.LoadAt(testRoot, "account-a");
        Assert(recovered.RecoveredFromBackup && recovered.PendingOperations.Count == 1,
            "Damaged state must recover a valid previous copy with offline edits.");
        CloudObjectSyncStateStore.SaveAt(testRoot, recovered);
        Assert(System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(accountPath)!, "*.corrupt-*").Length == 1,
            "Recovery must preserve damaged original for diagnosis.");
        System.IO.File.WriteAllText(accountPath, "broken");
        System.IO.File.WriteAllText(accountPath + ".bak", "broken too");
        var blocked = CloudObjectSyncStateStore.LoadAt(testRoot, "account-a");
        Assert(!string.IsNullOrEmpty(blocked.PersistenceError), "Unreadable state must block synchronization, not look like a new device.");
        var saveBlocked = false;
        try { CloudObjectSyncStateStore.SaveAt(testRoot, blocked); }
        catch (System.IO.IOException) { saveBlocked = true; }
        Assert(saveBlocked && System.IO.File.ReadAllText(accountPath) == "broken", "Blocked recovery must not replace original data.");
    }
    finally { System.IO.Directory.Delete(testRoot, recursive: true); }

    Assert(PersonalSyncAuthority.SelectMode(new SyncSession { UserId = "account", ExpiresAt = 1 }, false) == PersonalConfigSyncMode.UploadOnlyBackup,
        "Expired authentication must never promote a backup to configuration authority.");
    Assert(PersonalSyncAuthority.SelectMode(null, true) == PersonalConfigSyncMode.UploadOnlyBackup,
        "A saved account login must keep personal storage in backup mode while reconnecting.");
    Assert(PersonalSyncAuthority.SelectMode(null, false) == PersonalConfigSyncMode.Bidirectional,
        "Standalone users must retain personal synchronization.");
    var page = new CloudSyncObjectListResponse
    {
        CurrentRevision = 30, CursorRevision = 20,
        Objects = [new CloudSyncObjectRecord { ObjectId = "a", Revision = 20 }]
    };
    Assert(CloudSyncProgress.DownloadCursor(10, page) == 20, "A concurrent server write must not advance the downloaded cursor.");
    Assert(CloudSyncProgress.DownloadCursor(20, new CloudSyncObjectListResponse { CurrentRevision = 40, CursorRevision = 40 }) == 20,
        "An empty response from an older server must not skip an unseen write.");
    Assert(CloudSyncProgress.DownloadCursor(20, new CloudSyncObjectListResponse
        { Objects = [new CloudSyncObjectRecord { ObjectId = "b", Revision = 21 }] }) == 21,
        "A following page must still receive the other device's write.");
    var when = new DateTime(2026, 9, 22, 1, 0, 0, DateTimeKind.Utc);
    var first = LauncherConfigObjectStore.CreateRestorePoint([], [], when, "test");
    var second = LauncherConfigObjectStore.CreateRestorePoint([], [], when, "test");
    Assert(first.RestorePointId != second.RestorePointId && LauncherConfigObjectStore.GetRestorePointPath(first) != LauncherConfigObjectStore.GetRestorePointPath(second),
        "Backups with the same timestamp must not overwrite one another.");
    foreach (var invalid in new[] { "{}", "null", "{\"restorePoints\":null}", "{\"restorePoints\":[null]}" })
    {
        var rejected = false;
        try { LauncherConfigObjectStore.DeserializeHistoryIndex(System.Text.Encoding.UTF8.GetBytes(invalid)); }
        catch (JsonException) { rejected = true; }
        Assert(rejected, "Malformed history must never be treated as an empty backup directory.");
    }
    var malformed = new LauncherConfigHistoryIndex { RestorePoints = [new LauncherConfigRestorePointInfo
        { RestorePointId = "test", Path = "state/config-history/points/../../index.json", Sha256 = new string('a', 64) }] };
    var unsafePathRejected = false;
    try { LauncherConfigObjectStore.DeserializeHistoryIndex(LauncherConfigObjectStore.SerializeHistoryIndex(malformed)); }
    catch (JsonException) { unsafePathRejected = true; }
    Assert(unsafePathRejected, "History cleanup must not follow paths outside the backup directory.");
    Console.WriteLine("Sync architecture safety passed: download watermarks, interleaved changes, unique backups, strict history index, stable authority.");
}

static void VerifySyncConflictExperience()
{
    static JsonElement Payload(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    static LauncherConfigObjectEnvelope Envelope(string json, bool deleted = false) => new()
        { ObjectId = "settings.mouseTriggers", Payload = Payload(json), Deleted = deleted };
    Assert(LauncherConfigObjectStore.HasEquivalentPayload(Envelope("{\"a\":1,\"b\":2}"), Envelope("{\"b\":2,\"a\":1}")), "Object property order must not create a conflict.");
    Assert(!LauncherConfigObjectStore.HasEquivalentPayload(Envelope("[1,2]"), Envelope("[2,1]")), "Array order must remain meaningful.");
    Assert(!LauncherConfigObjectStore.HasEquivalentPayload(Envelope("{}"), Envelope("{}", true)), "Deletion must remain a real difference.");
    var state = new CloudObjectSyncState();
    const string id = "settings.mouseTriggers";
    var conflict = new CloudObjectConflictRecord { ObjectId = id, LocalPayload = Payload("{\"rightButtonLongPress\":true}"), RemoteRevision = 2 };
    state.Conflicts[id] = conflict;
    state.Objects[id] = new CloudObjectSyncCacheEntry { ObjectId = id, Revision = 3, Payload = Payload("{\"rightButtonLongPress\":false}"), UpdatedByDeviceName = "Other" };
    state.PendingOperations[id] = new CloudObjectPendingOperation { ObjectId = id };
    Assert(CloudConflictReconciler.Reconcile(state) == 0 && conflict.RemoteRevision == 3, "Real differences must survive and track latest remote revision.");
    Assert(SyncUserText.Differences(conflict, state.Objects[id]).Contains("右键长按"), "Known differences need understandable names.");
    state.Objects[id].Revision = 1;
    state.Objects[id].Payload = conflict.LocalPayload;
    Assert(CloudConflictReconciler.Reconcile(state) == 0 && state.Conflicts.Count == 1, "Older cache must not erase conflict.");
    state.Objects[id].Revision = 4;
    Assert(CloudConflictReconciler.Reconcile(state) == 1 && state.Conflicts.Count == 0, "Equal preserved content must reconcile.");
    Assert(state.PendingOperations.ContainsKey(id), "Reconciliation must preserve newer pending edits.");
    Assert(SyncUserText.Device("a", "PC", "a") == "本机", "Own device needs a friendly label.");
    Assert(SyncUserText.Device("b", "PC", "a") == "PC", "Same display name must not imply same device.");
    Assert(SyncUserText.Device(null, null, "a") != "本机", "Unknown identity must not imply this device.");
    Console.WriteLine("Sync UX verification passed: semantic equality, preserved conflicts, revision refresh, pending edits, friendly labels.");
}

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

static void VerifySyncPackageSafety()
{
    static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    Assert(SyncPackageSafety.ReadIndex(null).Items.Count == 0, "Missing index should initialize an empty repository.");
    foreach (var json in new[] { "", "{", "null", "{}", "{\"items\":null}", "{\"items\":[null]}", "{\"schemaVersion\":99,\"items\":[]}" })
        Reject(() => SyncPackageSafety.ReadIndex(System.Text.Encoding.UTF8.GetBytes(json)), "Corrupt index was treated as empty: " + json);
    var valid = new WebDavSyncIndex { Items = [new WebDavSyncEntry { ExtensionId = "safe-id", Deleted = true }] };
    Assert(SyncPackageSafety.ReadIndex(JsonSerializer.SerializeToUtf8Bytes(valid)).Items.Count == 1, "Valid tombstone rejected.");
    valid.Items.Add(new WebDavSyncEntry { ExtensionId = "SAFE-ID", Deleted = true });
    Reject(() => SyncPackageSafety.ReadIndex(JsonSerializer.SerializeToUtf8Bytes(valid)), "Duplicate IDs accepted.");
    var root = Path.Combine(Path.GetTempPath(), "yanzi-sync-verification-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        foreach (var id in new[] { "..", "../outside", "a/b", "a\\b", "C:\\outside", "name:stream", "trailing.", ".yanzi-old", " " })
            Reject(() => SyncPackageSafety.ResolveExtensionDirectory(root, id), "Unsafe extension path accepted: " + id);
        var target = SyncPackageSafety.ResolveExtensionDirectory(root, "extension");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "original.txt"), "original");
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("new.txt").Open())) writer.Write("updated");
        using var hostileStream = new MemoryStream();
        using (var hostileArchive = new System.IO.Compression.ZipArchive(hostileStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(hostileArchive.CreateEntry("../escaped.txt").Open())) writer.Write("escape");
        try { SyncPackageSafety.ReplaceDirectoryAsync(target, hostileStream.ToArray(), default).GetAwaiter().GetResult(); throw new Exception("ZIP traversal accepted."); }
        catch (IOException) { }
        Assert(!File.Exists(Path.Combine(root, "escaped.txt")) && File.Exists(Path.Combine(target, "original.txt")), "ZIP traversal damaged files outside staging.");
        var bytes = stream.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        SyncPackageSafety.VerifyHash(bytes, hash.ToLowerInvariant());
        Reject(() => SyncPackageSafety.VerifyHash(bytes, new string('0', 64)), "Hash mismatch accepted.");
        Reject(() => SyncPackageSafety.ReplaceDirectoryAsync(target, [1, 2, 3], default).GetAwaiter().GetResult(), "Corrupt ZIP accepted.");
        Assert(File.ReadAllText(Path.Combine(target, "original.txt")) == "original", "Failed extraction damaged original files.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { SyncPackageSafety.ReplaceDirectoryAsync(target, bytes, canceled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        Assert(File.Exists(Path.Combine(target, "original.txt")), "Cancellation damaged original files.");
        // An occupied destination forces promotion to fail; it must remain untouched.
        var blocked = Path.Combine(root, "occupied");
        File.WriteAllText(blocked, "keep");
        try { SyncPackageSafety.ReplaceDirectoryAsync(blocked, bytes, default).GetAwaiter().GetResult(); throw new Exception("Occupied destination accepted."); }
        catch (IOException) { }
        Assert(File.ReadAllText(blocked) == "keep", "Failed promotion damaged destination.");
        SyncPackageSafety.ReplaceDirectoryAsync(target, bytes, default).GetAwaiter().GetResult();
        Assert(File.ReadAllText(Path.Combine(target, "new.txt")) == "updated" && !File.Exists(Path.Combine(target, "original.txt")), "Replacement did not commit.");
        Assert(!Directory.EnumerateDirectories(root, ".yanzi-*").Any(), "Temporary directories leaked.");
    }
    finally { Directory.Delete(root, recursive: true); }
    Console.WriteLine("Sync package safety passed: corrupt indexes, duplicate IDs, path traversal, hashes, extraction failures, cancellation and replacement.");
}

static void VerifySearchInteractionSafety()
{
    using var pipeline = new SearchPipelineManager();
    var first = pipeline.CreateSession("old", "file");
    var canceled = false;
    using var registration = first.Token.Register(() => canceled = true);
    var waiting = Task.Delay(TimeSpan.FromMinutes(1), first.Token);
    var second = pipeline.CreateSession("new", "all");
    Assert(canceled && first.Token.IsCancellationRequested, "Replacing a search must cancel its in-flight work.");
    try { waiting.GetAwaiter().GetResult(); throw new Exception("Old search delay was not canceled."); }
    catch (OperationCanceledException) { }
    Assert(!pipeline.IsActive(first) && pipeline.IsActive(second), "Late results must not remain active.");
    pipeline.CancelActive();
    Assert(second.Token.IsCancellationRequested && !pipeline.IsActive(second), "Hiding/editing must invalidate active work.");
    second.Dispose();
    Assert(second.Token.IsCancellationRequested, "Disposed sessions must expose a safe captured token.");
    using (var throwing = new SearchSession(99, "test", "provider"))
    {
        throwing.Token.Register(() => throw new InvalidOperationException("provider cancellation failed"));
        throwing.Dispose();
        Assert(throwing.Token.IsCancellationRequested, "A provider cancellation callback must not break session cleanup.");
    }
    for (var i = 0; i < 1000; i++)
    {
        var previous = pipeline.CreateSession("a", "file");
        var current = pipeline.CreateSession("ab", "file");
        Assert(previous.Token.IsCancellationRequested && pipeline.IsActive(current), "Rapid input left stale work active.");
    }

    var results = new BulkObservableCollection<string>(["one", "two", "three"]);
    var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
    results.CollectionChanged += (_, e) => changes.Add(e);
    results.ReplaceAll(new[] { "one", "two", "three" }.Where(_ => true));
    Assert(changes.Count == 0, "Identical search results should not reset WPF containers/selection.");
    results.ReplaceAll(results.Where(x => x != "two"));
    Assert(results.SequenceEqual(["one", "three"]) && changes.Count == 1, "Lazy self-filtering must be snapshotted before clearing.");
    results.ReplaceAll(results);
    Assert(changes.Count == 1 && results.Count == 2, "Self replacement must preserve results without a reset.");
    results[0] = "updated";
    var replacement = changes.Last();
    Assert((string?)replacement.NewItems?[0] == "updated" && (string?)replacement.OldItems?[0] == "one", "WPF replacement notification has reversed values.");
    results.ReplaceAll(Array.Empty<string>());
    var emptyChanges = changes.Count;
    results.ReplaceAll(Enumerable.Empty<string>());
    Assert(results.Count == 0 && changes.Count == emptyChanges, "Repeated empty results should not reset the list.");
    Console.WriteLine("Search interaction safety passed: cancellation, late-result rejection, disposed tokens, rapid input, stable collections and lazy/self updates.");
}

static void VerifyBackpackResponsiveness()
{
    foreach (var threshold in new[] { 50, 120, 200, 250, 350, 500, 1500 })
        Assert(AppSettingsStore.NormalizeLongPressMilliseconds(threshold) == threshold, "User long-press threshold was overwritten.");
    Assert(AppSettingsStore.NormalizeLongPressMilliseconds(-1) == 50 &&
           AppSettingsStore.NormalizeLongPressMilliseconds(5000) == 1500, "Long-press bounds were not enforced.");
    using var cache = new QuickPanelSnapshotCache<PanelCacheProbe>();
    var key = new QuickPanelSnapshotKey("settings.json", 1, 100, 200, "explorer", false, false);
    var first = new PanelCacheProbe();
    var second = new PanelCacheProbe();
    PanelCacheProbe[] commands = [first, second];
    Assert(!cache.CanReuse(key, commands), "Uninitialized panel cache was reused.");
    cache.BeginUpdate(key, commands);
    Assert(!cache.CanReuse(key, commands), "Partial slot rebuild was reused.");
    cache.CompleteUpdate();
    Assert(cache.CanReuse(key, commands), "Unchanged panel failed to reuse slots.");
    foreach (var changed in new[] {
        key with { WriteVersion = 2 }, key with { LastWriteTicks = 101 },
        key with { FileLength = 201 }, key with { SettingsPath = "other.json" },
        key with { ContextProcess = "notepad" }, key with { GlobalFavorites = true },
        key with { ContextFavorites = true } })
        Assert(!cache.CanReuse(changed, commands), "Changed panel configuration reused stale slots.");
    Assert(!cache.CanReuse(null, commands), "Unverifiable settings were reused.");
    Assert(!cache.CanReuse(key, [second, first]) && !cache.CanReuse(key, [first]) &&
           !cache.CanReuse(key, [first, new PanelCacheProbe()]), "Command reorder/removal/replacement reused stale slots.");
    first.Change();
    Assert(!cache.CanReuse(key, commands), "In-place command change did not invalidate slots.");
    cache.BeginUpdate(key, commands);
    second.Change();
    cache.CompleteUpdate();
    Assert(!cache.CanReuse(key, commands), "A change during rebuilding was lost.");
    cache.BeginUpdate(key, commands);
    cache.CompleteUpdate();
    Assert(first.SubscriberCount == 1 && second.SubscriberCount == 1, "Rebuild leaked property subscriptions.");
    cache.Dispose();
    Assert(first.SubscriberCount == 0 && second.SubscriberCount == 0 && !cache.CanReuse(key, commands), "Cache disposal leaked handlers or remained reusable.");
    Console.WriteLine("Backpack responsiveness safety passed: unchanged-slot reuse, all invalidation paths, partial rebuilds, handler cleanup and threshold preservation.");
}

static void VerifyQuickWindowSwitchSafety()
{
    // 1. 目标判定规则验证
    Assert(QuickWindowSwitchService.IsToggleEligibleTarget("shell:Desktop"), "shell:Desktop 应该支持智能窗口切换。");
    Assert(QuickWindowSwitchService.IsToggleEligibleTarget("shell:Downloads"), "shell:Downloads 应该支持智能窗口切换。");
    Assert(QuickWindowSwitchService.IsToggleEligibleTarget("notepad.exe"), "notepad.exe 应该支持智能窗口切换。");
    Assert(QuickWindowSwitchService.IsToggleEligibleTarget(@"C:\Windows\notepad.exe"), "完整路径可执行文件应该支持智能窗口切换。");
    Assert(QuickWindowSwitchService.IsToggleEligibleTarget("calc"), "通用命令应该支持智能窗口切换。");
    Assert(!QuickWindowSwitchService.IsToggleEligibleTarget("https://yanzi.luoluoluo.cc.cd"), "网页网址不应支持智能窗口切换。");
    Assert(!QuickWindowSwitchService.IsToggleEligibleTarget("http://127.0.0.1:8080"), "HTTP 网址不应支持智能窗口切换。");
    Assert(!QuickWindowSwitchService.IsToggleEligibleTarget(""), "空字符串不应支持智能窗口切换。");

    // 2. Manifest 序列化与反序列化验证
    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    var jsonWithToggleTrue = "{\"name\":\"测试\",\"toggleWindow\":true}";
    var manifest1 = JsonSerializer.Deserialize<LocalExtensionManifest>(jsonWithToggleTrue, options);
    Assert(manifest1?.ToggleWindow == true, "反序列化 toggleWindow: true 失败。");

    var jsonWithToggleFalse = "{\"name\":\"测试\",\"toggleWindow\":false}";
    var manifest2 = JsonSerializer.Deserialize<LocalExtensionManifest>(jsonWithToggleFalse, options);
    Assert(manifest2?.ToggleWindow == false, "反序列化 toggleWindow: false 失败。");

    var jsonWithoutToggle = "{\"name\":\"测试\"}";
    var manifest3 = JsonSerializer.Deserialize<LocalExtensionManifest>(jsonWithoutToggle, options);
    Assert(manifest3?.ToggleWindow == null, "未显式指定 toggleWindow 时应为 null。");

    // 3. CommandItem 默认与显式设置验证
    var cmdDefault = new CommandItem("T", "测试默认", "subtitle", "小程序", "#000000", "notepad.exe", []);
    Assert(cmdDefault.ToggleWindow == true, "CommandItem 默认 ToggleWindow 应该为 true。");

    var cmdDisabled = new CommandItem("T", "测试关闭", "subtitle", "小程序", "#000000", "notepad.exe", [], toggleWindow: false);
    Assert(cmdDisabled.ToggleWindow == false, "CommandItem 显式关闭 ToggleWindow 应该为 false。");

    Console.WriteLine("Quick window switch safety passed: eligibility check, manifest roundtrip, default value fallback and CommandItem mapping.");
}

sealed class PanelCacheProbe : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public int SubscriberCount => PropertyChanged?.GetInvocationList().Length ?? 0;
    public void Change() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Title"));
}
