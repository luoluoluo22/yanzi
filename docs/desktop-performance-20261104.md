# Desktop polling and UI performance

Scope: Windows host polling, launcher grid, AI transcript and extension catalog refresh. This change does not publish an installer or restart the production host.

## Behavior

- Window binding discovery runs every 2 seconds instead of 150ms. Foreground events still trigger discovery; location events are filtered to top-level objects, deduplicated, bounded to 256 pending targets and processed in 32ms batches. Movement updates tracked windows rather than scanning every window. Process names have a short cache and process handles are disposed. Hover detection remains at 150ms and only inspects existing overlays. Disabled/empty bindings stop polling.
- AI messages use a recycling virtualized list with pixel scrolling. Markdown, attachments and message actions remain in the existing templates. Switching topics updates time dividers once and schedules one scroll request. The generation status is a fixed footer below the scrollable transcript.
- Launcher grid uses fixed 92 x 100 DIP virtualized cells, including the existing container margins. Selection, templates and drag handlers remain on the ListBox containers. Keyboard navigation reads the actual panel column count.
- Catalog fingerprints and manifest loading run in a single background refresh, with queued refresh requests coalesced. Changes arriving during a scan invalidate its result. Cache, staging, backup and reparse directories are excluded before descending. Existing filesystem-event batching and periodic reconciliation remain. Whole-catalog replacement refreshes hotkeys, radial menus and quick-panel slots once per batch instead of once per extension.
- Idle diagnostics write through one background worker, keeping only the latest pending snapshot per extension. State transitions and observed input resets publish immediately; unchanged state has a 30-second diagnostic heartbeat. The dispatch check itself stays at 5 seconds. JSON field names are unchanged; consumers should use `observedAt` to judge freshness. This diagnostic snapshot is not a transactional task receipt.
- Quick-panel phone badges refresh immediately on show, poll every 2 seconds while visible, and stop when hidden/closed.

## Verification

Run `powershell -NoProfile -File scripts/verify-desktop-performance.ps1` on Windows. It builds the host, runs performance regressions and the existing AI regression, then runs the actual host windows on an isolated desktop with synthetic data. CI also runs the deterministic performance regression under the existing full Windows gate.

The tests cover 5,000 grid items (fewer than 50 realized containers in the fixture), offscreen selection, resizing, collection mutation and recycling; 2,000 messages using the actual transcript template (7 containers in the fixture), streaming and topic replacement; 10,000 repeated window movements (one dispatcher post); bounded event queues; coalesced background writes, failure recovery and catalog fingerprint change detection. The native scenario covers the real MainWindow transcript and 1,000-item launcher grid, keyboard column agreement, quick-panel timer lifecycle and asynchronous catalog refresh.

Hidden-desktop checks are real WPF window tests, not physical mouse/keyboard acceptance in the user's installed application. They do not establish a percentage reduction in CPU or memory, and no such claim is made. Physical dragging across mixed-DPI monitors and long-running production profiling remain release acceptance work.

## Integration audit

The existing clean worktree was reused; no additional worktree was created. The original workspace's browser-helper changes were preserved. All registered worktrees, local branches, unique commits, worktree status and open PR checks were inventoried in `.tmp/desktop-performance/workspace-audit-current.json`. Unrelated unfinished, dirty or unverified work remains with its original branches/PR owners. This performance task does not certify those older tasks as complete or delete their worktrees.
