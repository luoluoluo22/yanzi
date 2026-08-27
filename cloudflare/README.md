# OpenQuickHost Cloud Sync

This folder contains the Cloudflare backend for account, extension, device, and object-level configuration sync:

- Workers: HTTP API
- D1: metadata and user sync state
- R2: extension package archives

## Routes

- `GET /health`
- `GET /v1/extensions`
- `PUT /v1/extensions/:id`
- `PUT /v1/extensions/:id/archive?version=x.y.z`
- `GET /v1/extensions/:id/archive`
- `GET /v1/users/:userId/extensions`
- `PUT /v1/users/:userId/extensions/:extensionId`
- `GET /v1/me/devices`
- `POST /v1/me/devices`
- `POST /v1/me/mobile/messages`
- `GET /v1/me/mobile/messages?deviceId=<id>`
- `POST /v1/me/mobile/messages/:messageId/ack`
- `GET /v1/sync/objects`
- `GET /v1/sync/capabilities`
- `GET /v1/sync/changes?since=<revision>&limit=<1-500>`
- `PUT /v1/sync/objects/:objectId` (`expectedRevision` is required; conflicts return `409` with `details.currentRevision`)

Account routes require `Authorization: Bearer <token>`.

## D1 migrations

Apply migrations before deploying a Worker version that uses new tables:

```powershell
npx wrangler d1 migrations apply openquickhost-sync-db --remote --config cloudflare/wrangler.toml
npx wrangler deploy --config cloudflare/wrangler.toml
```

Migration `0010_sync_objects.sql` adds the per-account monotonic revision and current-object tables. Migration `0011_sync_object_history.sql` adds the append-only object history used by the history and restore APIs. Migration `0012_scrub_ai_secrets.sql` removes legacy AI API keys after desktop clients move those keys to local DPAPI. Migration `0013_scrub_personal_sync_secrets.sql` removes repository tokens/passwords from account config; the Worker also rejects future plaintext credential persistence from old clients. Apply all migrations before deploying a Worker that reports sync protocol version 2. The desktop client keeps the legacy whole-snapshot path as a fallback while these migrations roll out.

Object history endpoints:

- `GET /v1/sync/history?objectId=...&before=0&limit=50`
- `POST /v1/sync/objects/:id/restore`

A restore is a conditional write and creates a new revision; rejected conflicts do not consume a revision.

## Object-authority rollout

The Worker reports its sync mode through `/v1/sync/capabilities`. Keep `SYNC_OBJECTS_AUTHORITATIVE` unset during migration: new clients will dual-write the legacy snapshot and object rows. After migration and multi-device validation, set the Worker variable to `true` to make new clients stop legacy snapshot writes:

```powershell
npx wrangler secret put SYNC_OBJECTS_AUTHORITATIVE --config cloudflare/wrangler.toml
```

Enter `true` when prompted. Removing the variable immediately returns new clients to compatibility dual-write mode.

Both capability modes can be verified locally:

```powershell
.\scripts\test-cloud-object-sync.ps1 -AuthorityMode false
.\scripts\test-cloud-object-sync.ps1 -AuthorityMode true
```
