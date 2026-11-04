# Search usage UI/persistence performance follow-up

Scope: launcher command creation, quick-panel usage bindings, and search-memory.json saving. This follows PR #161; it does not change feature ranking, cloud schema, or any published installer.

## Why

* CommandItem's constructor read and deserialized the same search-memory.json for every command. Quick-panel usage property bindings could repeat the same synchronous JSON read.
* The previous WPF DispatcherTimer flushed JSON serialization and disk writes on the UI thread. Calling Save from a worker also created a dispatcher timer without a message pump, so a sync callback could leave data pending.

## Change

* GetCachedUsageCount uses an isolated, immutable snapshot per data root, with one-second external file change checks and immediate cache replacement on Save. Authoritative Load stays uncached for WebDAV merge operations.
* Save snapshots entries before returning, debounces on a thread-pool timer, and serializes writes through LatestSnapshotWriter to avoid concurrent/stale writes. SafeFile.AtomicWriteText prevents half-written persistence files.
* App.OnExit flushes pending usage snapshots. These changes do not move the full command catalog or other settings storage into memory.

## Acceptance

Run `scripts/verify-search-usage-memory.ps1`. Regression checks use a temporary profile, and cover pending reads, mutable-source isolation, last-write-wins, automatic worker-thread debounce without a dispatcher, external replacement, profile switch, atomic writing, and normal exit flushing. They also build the host and run the existing desktop regression. No benchmark percentage is implied; release/real-user installation is separate.
