# Yanzi Windows release: one dependency tree, two processes

## Release layout

A production Windows package contains two small application hosts in the **same directory**:

- `Yanzi.exe`: user-facing Shell.
- `Yanzi.Runtime.exe`: independent, long-lived background owner.
- One copy of `Yanzi.dll`, Windows Desktop/.NET 9, Roslyn, Everything and other shared dependencies.
- Runtime-specific `Yanzi.Runtime.dll`, `Yanzi.Runtime.deps.json` and `Yanzi.Runtime.runtimeconfig.json`.

The separate `src/Yanzi.Runtime` project remains for development and testing. The packaging script builds both projects as self-contained apps into **separate temporary output directories**, compares every common relative file's SHA-256, and copies only the four Runtime-specific files beside `Yanzi.exe`. It fails if a common file differs or if a `Runtime/` folder appears in the final package. This preserves separate process names; tools that stop `Yanzi.exe` by process name must not inadvertently stop `Yanzi.Runtime.exe`.

## Runtime preservation and updates

On first Shell startup from the official installed `%LOCALAPPDATA%\Yanzi\current` directory (or from Velopack portable `current` with a parent `.portable` marker), `RuntimeConnection.FindExecutable` copies the packaged tree into a content-addressed snapshot in `%LOCALAPPDATA%\YanziRuntime\bundled`, and launches the independent `Yanzi.Runtime.exe` from that snapshot. Never run production Runtime directly from a directory an installer might replace.

Pre-existing standalone `Runtime\Yanzi.Runtime.exe` packages and the legacy `runtime.json` pointer remain recognized for migration. Existing background work is not force-killed on upgrade. `health` reports both assembly version and SHA-256 of `Yanzi.dll`; a mismatch is a **failed release consistency check**, not grounds to kill an active Runtime. A controlled transition or restart is required before declaring an installed upgrade verified.

Only the official installed Shell can register the HKCU `Yanzi` startup entry, always pointing to `%LOCALAPPDATA%\Yanzi\current\Yanzi.exe --tray`. Its successful registration removes the obsolete `Yanzi.Runtime` startup entry. The Shell launches and attaches to Runtime as needed. Release/Debug executables from development snapshots cannot mutate production startup registrations.

## Commands and release gates

- Build a local package **without installation or upload**:
  `powershell -File scripts/publish-installer.ps1 -Version 1.0.20 -ArtifactRoot <scratch-output> -SkipBaselineDownload`
- Check the built payload:
  `powershell -File scripts/verify-release-layout.ps1 -PublishDirectory <scratch-output>\publish\win-x64 -ExpectedVersion 1.0.20.0`
- **After a controlled official install / process handoff**, verify actual production state:
  `powershell -File scripts/verify-release-layout.ps1 -PublishDirectory "$env:LOCALAPPDATA\Yanzi\current" -CheckInstalled`

The post-install check requires: stable installed Shell process, exactly one Runtime process, the stable boot registration, no legacy Runtime entry, matching file versions and matching `Yanzi.dll` SHA-256 in the running Runtime snapshot. If any check fails, report the release as **pending/blocked**, preserve the previous snapshot, and do not erase user data or force-close background jobs.

The legacy build is approximately 164.18 MiB compressed / 371.76 MiB unpacked; the deduplicated validation build is approximately 85.8 MiB / 187.78 MiB unpacked, but each new version must be measured independently.

## Scope boundaries

Building, verifying, committing or merging is not an installation or public GitHub Release. Do not change the official startup entry, force-stop existing services, issue a new public tag, or upload installer assets without a separate explicit deployment step. For historical snapshots use `scripts/cleanup-runtime-history.ps1` with its reference audit; do not delete runtime pointers or directories solely by age.
