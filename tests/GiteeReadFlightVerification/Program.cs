using OpenQuickHost.Sync;

static void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

var baseKey = GiteeReadFlightCoordinator.CreateKey("owner", "repo", "main", "state/object.json", "token-A");
var otherTenant = GiteeReadFlightCoordinator.CreateKey("owner", "repo", "main", "state/object.json", "token-B");
var otherPath = GiteeReadFlightCoordinator.CreateKey("owner", "repo", "main", "state/other.json", "token-A");
Check(baseKey != otherTenant && baseKey != otherPath && !baseKey.Contains("token-A"),
    "distinct auth scopes/paths and no plaintext token in key");

var first = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
var fetches = 0;
Func<Task<byte[]?>> factory = () =>
{
    Interlocked.Increment(ref fetches);
    return first.Task;
};
var tasks = Enumerable.Range(0, 64)
    .Select(_ => GiteeReadFlightCoordinator.ReadAsync(baseKey, factory, CancellationToken.None))
    .ToArray();
await Task.Delay(50);
Check(fetches == 1, "64 simultaneous reads cause one actual fetch");
first.SetResult([1, 2, 3]);
var results = await Task.WhenAll(tasks);
Check(results.All(x => x is { Length: 3 }), "every waiter receives content");
results[0]![0] = 99;
Check(results[1]![0] == 1, "each waiter owns independent response bytes");
Check(GiteeReadFlightCoordinator.ActiveCount == 0, "completed entries are removed");

var newCount = 0;
await GiteeReadFlightCoordinator.ReadAsync(baseKey,
    () =>
    {
        Interlocked.Increment(ref newCount);
        return Task.FromResult<byte[]?>([4]);
    }, CancellationToken.None);
Check(newCount == 1, "sequential next read is not cached");

// Stress the completion-to-next-read boundary. A completed in-flight entry
// must never be observed as a cache by a subsequent sequential caller.
for (var round = 0; round < 20; round++)
{
    var stressKey = baseKey + "-sequential-" + round;
    var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var physical = 0;
    var pending = GiteeReadFlightCoordinator.ReadAsync(stressKey,
        () => { Interlocked.Increment(ref physical); return completion.Task; }, CancellationToken.None);
    completion.SetResult([(byte)round]);
    var initial = await pending;
    var followup = await GiteeReadFlightCoordinator.ReadAsync(stressKey,
        () => { Interlocked.Increment(ref physical); return Task.FromResult<byte[]?>([(byte)(round + 1)]); },
        CancellationToken.None);
    Check(initial is { Length: 1 } && initial[0] == round &&
          followup is { Length: 1 } && followup[0] == round + 1 && physical == 2 &&
          GiteeReadFlightCoordinator.ActiveCount == 0,
        "completed flights are evicted before next sequential read " + round);
}

using (var token = new CancellationTokenSource())
{
    var unblock = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var sharedFetches = 0;
    var cKey = baseKey + "-cancel";
    Func<Task<byte[]?>> sharedFactory = () =>
    {
        Interlocked.Increment(ref sharedFetches);
        return unblock.Task;
    };
    var survivor = GiteeReadFlightCoordinator.ReadAsync(cKey, sharedFactory, CancellationToken.None);
    var cancelWaiter = GiteeReadFlightCoordinator.ReadAsync(cKey, sharedFactory, token.Token);
    token.Cancel();
    try { await cancelWaiter; throw new Exception("Cancellation did not propagate"); }
    catch (OperationCanceledException) { }
    unblock.SetResult([7]);
    Check((await survivor)![0] == 7 && sharedFetches == 1,
        "one canceled waiter does not cancel shared transport");
}

var failingKey = baseKey + "-failure";
try
{
    await GiteeReadFlightCoordinator.ReadAsync(failingKey,
        () => Task.FromException<byte[]?>(new IOException("simulated HTTP failure")),
        CancellationToken.None);
    throw new Exception("Failure was hidden");
}
catch (IOException) { }
var retryCount = 0;
await GiteeReadFlightCoordinator.ReadAsync(failingKey,
    () =>
    {
        Interlocked.Increment(ref retryCount);
        return Task.FromResult<byte[]?>([9]);
    }, CancellationToken.None);
Check(retryCount == 1, "failed flight is evicted and next read retries");

var key = baseKey + "-write";
var oldWait = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
var newWait = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
var oldRead = GiteeReadFlightCoordinator.ReadAsync(key, () => oldWait.Task, CancellationToken.None);
GiteeReadFlightCoordinator.Invalidate(key);
var freshCount = 0;
Func<Task<byte[]?>> freshFactory = () =>
{
    Interlocked.Increment(ref freshCount);
    return newWait.Task;
};
var freshRead = GiteeReadFlightCoordinator.ReadAsync(key, freshFactory, CancellationToken.None);
oldWait.SetResult([1]);
Check((await oldRead)![0] == 1, "old waiter still completes after mutation");
var freshFollower = GiteeReadFlightCoordinator.ReadAsync(key, freshFactory, CancellationToken.None);
Check(freshCount == 1, "old completion cannot remove a newer flight");
newWait.SetResult([2]);
Check((await freshRead)![0] == 2 && (await freshFollower)![0] == 2,
    "mutation invalidation keeps newer readers isolated");

Check(GiteeReadFlightCoordinator.ActiveCount == 0, "no in-flight entries leak after QA");
Console.WriteLine($"GITEE_SINGLEFLIGHT_PASS networkFlights={GiteeReadFlightCoordinator.NetworkFlights} sharedWaiters={GiteeReadFlightCoordinator.SharedWaiters}");
