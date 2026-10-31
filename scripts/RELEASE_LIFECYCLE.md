# Yanzi one-command release lifecycle

This complements the existing Windows release workflow and
the two per-machine Yanzi-CrossDevice-Deployment scheduled installers.

## Usage

From a clean, reviewed main whose HEAD is pushed to origin/main, set
the Windows version in src/OpenQuickHost/OpenQuickHost.csproj before publishing.

Single command (waits for the complete result):

    powershell.exe -NoProfile -File .\scripts\release-lifecycle.ps1

Extended timeout:

    powershell.exe -NoProfile -File .\scripts\release-lifecycle.ps1 -MaxWaitMinutes 120 -PollSeconds 20

After an interruption or to check an already published version:

    powershell.exe -NoProfile -File .\scripts\release-lifecycle.ps1 -Version 1.0.75 -StatusOnly -MaxWaitMinutes 5

The built-in preflight checks clean, synced main, compiles the application,
runs isolated deployment tests, then calls deploy-to-devices.ps1 -NoWait once.
The orchestrator polls the matching GitHub Actions commit and verifies an
official published release with a SHA-256 digest for the setup asset.
It never automatically resolves merge conflicts or republishes existing tags.

Once published, each device agent downloads and installs the trusted asset,
checks SHA-256 and starts the executable. A second, independent scheduled task
checks the installed binary version and running process and uploads a minimal
receipt to the PRIVATE GitHub repository:
luoluoluo22/yanzi-deployment-receipts

Install the receipt task once per authorized device:

    powershell.exe -NoProfile -File .\scripts\install-deploy-receipt-agent.ps1

The task checks every 2 minutes. To avoid repository noise, it writes only on
state change or once per six-hour heartbeat. Reports older than 12 hours, older
than the published release, from the wrong machine, showing a wrong version
or no running process cannot pass verification. Raw logs, prompts, API keys,
and user data never enter the receipt repository.

## Results and recovery

Exit 0: both devices have current verified-running receipts.
Exit 1: a test, CI, Git preflight, publication or authorization error.
Exit 2: still pending or timed out; no false success.

JSON audit reports are saved to:
%LOCALAPPDATA%\OpenQuickHost\ReleaseOrchestrator\vVERSION.json
%LOCALAPPDATA%\OpenQuickHost\ReleaseOrchestrator\latest.json

StatusOnly can be repeated safely. After interruption the same command
resumes a matching CI run or existing release rather than duplicating it.
A failed workflow requires explicit RetryFailedWorkflow after investigation.

Offline regression suite:

    powershell.exe -NoProfile -File .\scripts\test-release-lifecycle.ps1

Fixture tests are side-effect-free and cover current, wrong-version, stale,
failed, missing, and non-running devices.

Important: installation verification is not a substitute for feature-specific
UI interaction, Gemini network requests, or real-device gesture QA.
