# Windows UI QA — 2026-11-07 home machine

Device: PC-202409131508 (home Windows, not office).
Worktree: F:\Desktop\kaifa\yanzi-ui-timeout-fix-20261107 (isolated, clean at QA start).
Tested SHA: a572d178bf6bf886bb729fadac96bd6702bf3018.
Command: powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\qa-ui-windows.ps1 -RepoPath F:\Desktop\kaifa\yanzi-ui-timeout-fix-20261107 -ExpectedSha a572d178bf6bf886bb729fadac96bd6702bf3018 -MinimumPassingTests 17 -StepTimeoutSeconds 600
Result: Release host build exit 0, test restore exit 0, WPF STA smoke exit 0; 22 passed, 0 failed. Started UTC 2026-11-07T11:25:05; finished UTC 2026-11-07T11:26:49.
Machine-readable evidence: docs/ui-unification/windows-ui-qa-20261107-home-timeout-fix.json.
Logs: %TEMP%\yanzi-ui-qa-a572d178bf6b\host-build.log, test-restore.log, wpf-smoke.log (on home device).
Pending: both light/dark screenshots, physical keyboard and screen-reader interactions beyond automated checks, external live service integration, actual foreground/background interaction. No final visual/functional sign-off.
