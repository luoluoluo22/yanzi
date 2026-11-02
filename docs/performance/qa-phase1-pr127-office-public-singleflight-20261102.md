# PR #127: real public Gitee concurrent singleflight / physical GET QA (2026-11-02)

## Scope and safety
- Beijing time 15:39-15:53, office CHINAMI-3IF988E, isolated checkout D:\Projects\qa-phase1-pr127-20261102.
- Only read-only unauthenticated requests to the public Gitee repository metadata API. No user token, private objects, account sync state, production data or remote writes.
- Optional YANZI_GITEE_PUBLIC_PROBE=1; ordinary CI remains independent of external Gitee availability.
- PR #128 owns Retry-After changes; this run did not edit that PR.

## Real transport + concurrent deduplication
- Extended tests/GiteeHttpReadMetricsVerification/Program.cs to hold one shared fetch behind an explicit test gate while 32 logical readers join the same key.
- After releasing the gate, all 32 readers received identical nonempty real HTTPS content, with exactly one actual GET through HttpMessageHandler and 31 shared waiters.
- The next sequential read fetched the public endpoint again (not a stale cache hit), proving flight eviction.
- Two initial direct GETs + 32 coalesced logical reads + one sequential logical read = 35 logical reads but 4 actual HTTPS GETs. All 4 were HTTP 200; metrics reported sent=4, completed=4, success2xx=4.
- This is real public HTTPS transport, not a production account synchronization benchmark; it does not establish production same-load HTTP reduction or cross-device consistency.
- The full metrics verification (including fake-handler status/cancellation/timer/window tests) passed twice with the public probe enabled. Existing GiteeReadFlightVerification also passed with exit code 0.
- Isolated .NET SDK 9.0.320 Release desktop build: exit 0, 0 warnings, 0 errors.

## Independent Canary
- Isolated self-contained win-x64 Canary artifact: D:\Projects\_qa_tools\canary-phase1-20261102-1545, 586 recursive files; Yanzi.dll SHA-256 2C590707B969068051E7192E3FD803B7051DED05E71764A5EA79B935E31A314C.
- The long publish tool call did not return a trustworthy exit code; the artifact was inspected, the subsequent desktop build exited 0, and the Canary executable actually started. Do not count the missing publish exit code as verified.
- First smoke used Process.MainWindowHandle, which was zero for this WPF process; no window heartbeat result was obtained and the isolated instance was stopped. This was a QA harness limitation, not evidence of an application failure.
- Retest enumerated top-level HWNDs by PID: isolated --dev --show Canary PID 22952, 8 windows, WM_NULL 80/80 with no SendMessageTimeout failures; private memory 132.09 MiB, handles 654, threads 20.
- Observed wall-clock WM_NULL P95 99.09ms / P99 1160.23ms is noisy and materially higher than previous isolated samples. It is not a warehouse UI interaction benchmark; investigate under phase 2, do not claim a UI latency win.
- Official Yanzi PID 29104 remained unchanged. The isolated Canary exited; user credentials, WebView2 IndexedDB, backups, rollback files and ReleaseQueue were not modified.
- Local evidence: %LOCALAPPDATA%\OpenQuickHost\QA\phase1\office-pr127-public-coalescing-canary-20261102.json, SHA-256 DC9D49A9C9CE4D1181A23464458E751579E33787849CE2EDD05DA212D520A11E.

## Remaining acceptance gates
- Home PC-202409131508 currently offline; no wake or forced connection attempted. Matching-home-commit Canary is missing.
- Real account Gitee same-workload physical GET before/after, Retry-After/429/503, and home/office add-update-conflict-delete-restore synchronization consistency are missing.
- Stage 1 and PR #127 remain open/Draft; stages 2-4 remain unaccepted. CI for the new commit must be checked before any merge.