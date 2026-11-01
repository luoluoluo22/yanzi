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

An isolated local Windows development build was installed and executed on the real home computer on 2026-11-01, without replacing the installed Yanzi v1.0.85 or publishing a GitHub Release.

- Stage: %LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-runningextensions-stress-20261101\Yanzi.exe, launched with --dev --show --running-extensions-preview --running-extensions-qa. This is a real executable in an isolated development data channel, not just a build result.
- The actual RunningExtensionsWindow (HWND 787954) opened and logged loaded count=0, snapshotMs=1, uiMs=1, totalMs=2.
- Dev-only stress registered 24 synthetic running extension entries, then removed all 24. The actual WPF UI logged the insertion refresh at 17:07:55.837 (count=24, changes=24, totalMs=2) and removal refresh at 17:07:58.897 (count=0, changes=24, totalMs=8).
- An independent SendMessageTimeout WM_NULL probe after the churn produced 60/60 successful responses, maximum 14 ms. An earlier empty-window run produced 40/40 successful responses, maximum 11 ms.
- The official Yanzi.exe stayed running in its production directory; no public GitHub Release was created by this test.

Remaining before declaring the issue fixed across production users: test actual running customer mini-apps and the terminate button (synthetic fixtures test only collection churn), measure representative long-duration performance under extension synchronization, and perform deployment QA via the authorized publication process. The original freeze's precise stack trace remains unknown.
