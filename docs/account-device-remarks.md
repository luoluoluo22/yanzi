# Account device remarks

Devices have an account-owned remark in addition to their original model/host name. Set it once; all authenticated clients read the same displayName on their next device-list refresh. Offline targets are supported because the change updates the account record, not a queued remote command.

- Windows: Settings > YanNest device list > Save remark. Empty text restores the original name.
- Android: Computer connection status > Device remarks.
- Raccoon MCP: the federation adapter requires a separate integration from its maintained source. Do not advertise `set_device_remark` until that adapter is rebuilt and deployed.
- HTTP: `PUT /v1/me/devices/:deviceId/remark`, JSON `{"remark":"Company PC"}`, using an account login. Device-scoped grants cannot rename devices.
- Labels are trimmed, NFC-normalized and limited to 40 Unicode code points. Empty string clears the label. Concurrent edits use the last successful server write.
- Device listings retain `modelName`, `remark`, and `remarkUpdatedAt`; `displayName` prefers the remark. IDs remain the routing identity. Duplicate names are rejected by MCP selection; select by stable ID.
- Device heartbeat/registration cannot overwrite accountRemark/accountRemarkUpdatedAt. This uses reserved keys in existing capabilities JSON and needs no schema migration.
- Legacy device.alias.set remains a local shortcut, not the account-wide remark.

## Verification

Cloudflare tests cover cross-account/grant denial, offline edits, heartbeat preservation, clearing and Unicode validation. Federation tests were executed in the source worktree; the older federation source remains outside this integration. Windows capability verification checks name/model/ID resolution and tool registration. Windows and Android dev builds succeed.

Commands:
```
node --test cloudflare/src/*.test.mjs cloudflare/tests/*.test.mjs
dotnet run --project src/Yanzi.CapabilityVerification -- --device-remarks
powershell -File scripts/build-android-mvp.ps1 -Configuration dev -ArtifactRoot <isolated artifacts directory>
```

## Rollout

This feature is developed on feat/account-device-remarks, based on the current Raccoon federation development branch at d79eab8. That branch and origin/main have diverged; integrate the federation prerequisite and resolve differences before a main PR. Do not merge its unrelated commits as part of this feature.

Backend changes affect production deployment paths. Obtain deployment authorization before integrating those paths into main (the repository deploy workflow is automatic). Then deploy the backend, update Windows hosts and bundled/standalone Raccoon sources, and install the Android update through the existing release flow. Refresh connected MCP tool catalogs. No live service or installed application has been replaced during development.

Rollback the code through the normal release flow. Stored labels remain in device records; older code can ignore them. Do not copy account credentials between computers. Visual interaction and a live multi-device rollout remain release acceptance steps.
