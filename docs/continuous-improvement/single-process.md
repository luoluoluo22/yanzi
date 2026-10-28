# Windows Yanzi single-process release policy (v1.0.22+)

**Production invariant:** one installed `Yanzi.exe` owns WPF UI, tray, background sync, local Agent API, global input hooks, extension scheduling, idle triggers, mobile messaging and local extension execution. There must not be a child or independently running `Yanzi.Runtime.exe`.

## Development and production boundaries
- Development runs must use `--dev` or Debug profile; `HostRuntimeProfile.IsDevelopment` disables production hooks, cloud sync, startup registration and update checks, and uses `OpenQuickHost.Dev` data. Never convert a dev build into the stable single instance during verification.
- Production startup calls `LegacyRuntimeMigrationService.StopManagedProcesses()` only for official or marked-portable builds, after acquiring the singleton; it stops only verified legacy runtime executables under the current user's LocalAppData/YanziRuntime in the same interactive Windows session.
- Migration does not delete configurations, credentials, extension data, runtime snapshots, or app caches. Failed legacy terminations must be reported by logs and post-install checks rather than hidden.
- Production must not launch or attach to `RuntimeConnection`. In-process code owns scheduling, sync, script execution, listeners and approvals. `EnsureAvailableAsync` is explicitly retired. Legacy RPC source retained only to ease audit of old profiles; it must never be reconnected in the installed product.

## Build and verification
1. Build `OpenQuickHost.sln` in both Debug and Release.
2. Run `src/Yanzi.SyncVerification --single-process`, `--cloud-login-retry`, and `--vip-version-policy`. Check `Yanzi.RuntimeProfileVerification` in both stable and dev profiles.
3. Run `scripts/publish-installer.ps1 -Version <version>` from an isolated, clean worktree. It must publish only `src/OpenQuickHost`, never the independent Runtime project.
4. Run `scripts/verify-release-layout.ps1` on the output: require `Yanzi.exe` and embedded framework dependencies, reject `Yanzi.Runtime.exe`, `Yanzi.Runtime.dll`, all Runtime manifest files and a nested Runtime directory.
5. After installing on an authorized test computer, run with `-CheckInstalled` and confirm exactly one installed `Yanzi.exe`, zero `Yanzi.Runtime.exe`, current startup registration, and live functions (local Agent API, global hotkeys, cloud sync, scheduled miniapps and mobile delivery).
6. Check updater metadata, file hashes, GitHub Release assets and source branch before publication. Do not merge unrelated unverified changes.

**Rollback:** preserve the previously installed Windows package and the user data directory. If single-process startup fails, revert the application installer, not credentials or extension data. Re-enable the older Runtime only when explicitly rolling back to an older release.
