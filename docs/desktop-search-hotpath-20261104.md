# Search hot-path follow-up

Two previously remaining UI-thread hot paths were addressed after PR #165.

* GetRecentlyAddedOrderingBoost previously called GetLocalExtensionModificationUtc for each extension on every search keystroke. That requires path normalization, manifest and entrypoint file existence and modification-time queries. A 1.5-second bounded metadata cache reuses results across quick keystrokes; catalog replacement and direct upsert immediately invalidate it. The watcher refresh still detects changed files and preserves recently modified extension priority. Missed notifications naturally expire after 1.5 seconds. Cache keys include extension directory and entrypoint, and entries are bounded.
* UpdateSearchScopeCounts previously resolved pinned search providers within the command/scope nested loop, repeatedly scanning the entire command catalog. It now resolves each scope once per filter and uses a case-insensitive set for the inner loop. Search-scope counts and pinned provider identity are unchanged.

Run `scripts/verify-search-hotpath.ps1`: independent regression covers repeated reads, expiration, watcher-driven invalidation, cache bounds, case-insensitive scope identities and no repeated provider resolution for thousands of command rows; Windows host compilation and existing desktop regression are also required.

No benchmark percentage is claimed. Real installed-app UI acceptance and release remain separate.
