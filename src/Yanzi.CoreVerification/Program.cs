using System.Text.Json.Nodes;
using Yanzi.Core;

var checks = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new InvalidOperationException(scenario);
    checks++;
}
async Task Canceled(Task task, string scenario)
{
    try { await task; throw new InvalidOperationException(scenario); }
    catch (OperationCanceledException) { checks++; }
}

var coordinator = new AccountSyncCoordinator();
var lease = await coordinator.EnterAsync();
var waiting = coordinator.EnterAsync();
coordinator.Invalidate();
lease.Dispose(); lease.Dispose();
await Canceled(waiting, "Old-account queued operation entered the new session");
using (await coordinator.EnterAsync()) { checks++; }

var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var running = coordinator.RunAsync(async () => { started.SetResult(); await finish.Task; return 1; });
await started.Task;
var oldGeneration = coordinator.Generation;
coordinator.Invalidate();
finish.SetResult();
await Canceled(running, "Old-account result survived logout");
var committed = false;
try { coordinator.Commit(oldGeneration, () => committed = true); }
catch (OperationCanceledException) { checks++; }
Check(!committed, "Stale session mutated storage");
coordinator.Commit(coordinator.Generation, () => committed = true);
Check(committed, "Current session could not commit");

using (await coordinator.EnterAsync())
{
    using var cancellation = new CancellationTokenSource();
    var canceledWait = coordinator.EnterAsync(cancellation.Token);
    cancellation.Cancel();
    await Canceled(canceledWait, "Canceled waiter did not exit");
}
Check(await coordinator.RunAsync(() => Task.FromResult(42)) == 42, "Canceled wait leaked the semaphore");
var active = 0; var maxActive = 0;
await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => coordinator.RunAsync(async () =>
{
    active++; maxActive = Math.Max(maxActive, active);
    await Task.Yield();
    active--;
})));
Check(maxActive == 1 && active == 0, "Account operations overlapped");

JsonNode Parse(string json) => JsonNode.Parse(json)!;
void Merge(string baseline, string edited, string latest, string expected, string scenario) =>
    Check(JsonNode.DeepEquals(JsonSettingsMerge.Apply(Parse(baseline), Parse(edited), Parse(latest)), Parse(expected)), scenario);
Merge("{\"a\":1,\"b\":1}", "{\"a\":2,\"b\":1}", "{\"a\":1,\"b\":2}", "{\"a\":2,\"b\":2}", "Saving a page overwrote a concurrent edit");
Merge("{\"page\":{\"a\":1,\"b\":1}}", "{\"page\":{\"a\":2,\"b\":1}}", "{\"page\":{\"a\":1,\"b\":2}}", "{\"page\":{\"a\":2,\"b\":2}}", "Nested settings lost a concurrent edit");
Merge("{\"a\":1,\"b\":1}", "{\"b\":1}", "{\"a\":1,\"b\":2,\"c\":3}", "{\"b\":2,\"c\":3}", "Deleted setting reappeared");
Merge("{\"a\":null}", "{\"a\":null}", "{\"a\":2}", "{\"a\":2}", "Unchanged null replaced latest setting");
Merge("{\"a\":null}", "{}", "{\"a\":2}", "{}", "Explicit null removal failed");
Merge("{}", "{\"a\":null}", "{\"a\":2}", "{\"a\":null}", "Explicit null addition failed");
Merge("{\"a\":[1]}", "{\"a\":[2]}", "{\"a\":[3],\"b\":4}", "{\"a\":[2],\"b\":4}", "Array replacement corrupted other fields");
Merge("{\"a\":[1]}", "{\"a\":[1]}", "{\"a\":[3]}", "{\"a\":[3]}", "Unedited list overwrote sync changes");
var original = Parse("{\"x\":{\"y\":1}}");
var copy = JsonSettingsMerge.Apply(original, original.DeepClone(), original)!;
copy["x"]!["y"] = 2;
Check(original["x"]!["y"]!.GetValue<int>() == 1, "Merged settings alias the source snapshot");
Console.WriteLine($"CORE_ACCOUNT_SESSION_SETTINGS_CONCURRENCY=PASSED; checks={checks}");
