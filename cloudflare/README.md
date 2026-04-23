# OpenQuickHost Cloud Sync

This folder contains a minimal Cloudflare backend for extension sync:

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

Write operations require header `x-api-key: <SYNC_API_KEY>`.
