# Running mini-apps window: UI freeze mitigation (2026-11-01)

## Incident evidence

- Independent desktop monitor recorded UI response timeouts at 16:31:42 and 16:31:45, followed by manual capture at 16:31:50 (local time).
- Target window handle 395294 matched the RunningExtensionsWindow created at 16:31:41.
- The original view called RunningExtensionRegistry.GetSnapshot and LocalExtensionCatalog.LoadCommands directly on the WPF dispatcher every second, cleared the whole ObservableCollection, and used Dispatcher.Invoke from extension registry callbacks.
- This is a confirmed UI-thread blocking risk. The precise call stack at the original incident was not collected, so the root cause remains a high-confidence hypothesis, not a proven stack trace.

## Mitigations

- Query running extension state on a worker thread (Task.Run), never synchronously from the window's UI refresh.
- Read manifests on a worker thread only on first use, when new running IDs appear, or when the user explicitly requests a reload.
- Cache CommandItem metadata and instantiate WPF icon/brush objects only for running entries on the UI thread.
- Preserve unchanged view-model instances; insert, move and remove individual items instead of clearing the ListView.
- Coalesce registry updates with one pending dispatcher callback and a 200ms debounce. The periodic safety refresh is now every five seconds.
- Avoid unbounded UI callback queues and ignore refresh continuations after window close.
- Log snapshotMs, catalogMs, uiMs, totalMs, item count and change count on relevant refreshes; no clipboard content is collected.

## Verification

- Release build: dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --no-restore -p:RunAnalyzers=false -m:1 --nologo -v:q — exit 0, zero errors.
- Build emitted existing warnings in DesktopShellHelper.cs, AddJsonExtensionWindow.xaml.cs, VipActivationWindow.xaml.cs, and RadialMenuWindow.xaml.cs, not in the modified window.
- Static regression assertions: no synchronous Dispatcher.Invoke, no ObservableCollection.Clear, background registry/catalog reads, 200ms debounce, 5-second polling, incremental moves, registry unsubscription, and stage timing logs — all passed.
- git diff --check — passed.

## Remaining release gate

An installed build has NOT yet been independently exercised. Before release or marking the incident fixed in production, QA should repeatedly open the running extensions window, start and stop mini-apps under load, verify refresh and terminate operations, examine Running extensions refresh timing logs, and confirm no UI heartbeat timeouts over representative usage. Do not treat compilation alone as a production-level stability signoff.
