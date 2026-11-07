# 2026-11-07 Windows feature QA (pending)

- Target: PC-202409131508 (home), isolated `F:\Desktop\kaifa\yanzi-ui-feature-qa-20261107`; never touched dirty main worktree.
- Fetched feature SHA `1e2589499922b680a7e615f3453483f5a982c2a1`; `scripts/qa-ui-windows.ps1 -RepoPath F:\Desktop\kaifa\yanzi-ui-feature-qa-20261107 -ExpectedSha 1e2589499922b680a7e615f3453483f5a982c2a1 -MinimumPassingTests 20` exited 1. JSON `%TEMP%\yanzi-ui-qa-1e2589499922\windows-ui-qa.json`: failed at host-restore, exit_code null, pass=0 fail=0. Do NOT claim acceptance.
- Fixed process-exit-code handling at `b3ed8c032a060fb449a493c2b17cedde57cc1f86`; home machine checked out this SHA in the isolated worktree and started re-run. Result not yet confirmed at time of this evidence entry.
- Main branch was merged with explicit preservation of its ScheduleConfigWindow accessibility, screenshot and keyboard QA evidence; conflict resolution commit `c83eae21613be870814e1d0eba4dd5e33334534d` (new HEAD, requires its own device QA).
- Pending: full Release build and STA smoke on merged PR HEAD, dark/light screenshots, keyboard and accessibility, live integration; do not merge PR #200 before evidence is complete.
