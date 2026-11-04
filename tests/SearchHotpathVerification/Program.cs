using OpenQuickHost;

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}

long ticks = 1000;
var cache = new SearchExtensionTimestampCache(1500, () => ticks);
var readCount = 0;
DateTime first = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
DateTime second = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
for (var i = 0; i < 2000; i++)
{
    var value = cache.Get("extension/one", "Action.cs", () => { readCount++; return first; });
    CheckQuiet(value == first);
}
Check(readCount == 1, "2000 search keystroke lookups reuse one disk metadata read");
ticks += 1499;
Check(cache.Get("extension/one", "Action.cs", () => { readCount++; return second; }) == first, "cache lives until TTL");
ticks++;
Check(cache.Get("extension/one", "Action.cs", () => { readCount++; return second; }) == second, "expired cache resolves updated mtime");
Check(readCount == 2, "TTL triggers one metadata recheck");
Check(cache.Get("EXTENSION/ONE", "Action.cs", () => throw new InvalidOperationException("duplicate cache key")) == second, "cache is path case insensitive");
Check(cache.Get("extension/one", "manifest.json", () => first) == first, "different entrypoint has independent timestamp");
cache.Invalidate();
Check(cache.Get("extension/one", "Action.cs", () => first) == first, "catalog reload invalidates cached timestamp immediately");
Check(cache.Count == 1, "invalidated entries do not accumulate");
Check(cache.Get("", null, () => second) == second, "missing path delegates without unsafe caching");

for (var i = 0; i < 5000; i++)
    cache.Get("new-extension-" + i, "Action.cs", () => first);
Check(cache.Count <= 4096, "catalog cache size bounded even for large extension sets");

var known = new[] { "all", "app", "pin:alpha", "pin:beta", "extension" };
var checkedScopes = new List<string>();
var pinned = SearchPinnedScopeLookup.Resolve(known, key =>
{
    checkedScopes.Add(key);
    return key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase);
});
Check(checkedScopes.Count == known.Length, "pinned provider scopes resolved only once per scope");
Check(pinned.SetEquals(new[] { "pin:alpha", "pin:beta" }), "pinned lookup preserves provider identity");
Check(pinned.Contains("PIN:ALPHA"), "scope lookup remains case insensitive");
for (var i = 0; i < 5000; i++)
    CheckQuiet(pinned.Contains("pin:alpha") && !pinned.Contains("extension"));
Check(checkedScopes.Count == known.Length, "5000 command rows never repeat catalog provider resolution");

Console.WriteLine("SEARCH_HOTPATH_VERIFIED");
static void CheckQuiet(bool ok)
{
    if (!ok) throw new InvalidOperationException("unexpected cache or scope lookup result");
}
