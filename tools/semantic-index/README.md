# Yanzi Semantic Index

Local semantic indexing service for Yanzi. The service is deliberately separated from the
desktop host so large ML runtimes and model weights do not become part of `Yanzi.exe`.

## Design

- Model: `google/embeddinggemma-2`, Text + Vision only.
- Model loading: lazy; it is loaded only when a changed image needs encoding or a query is run.
- Idle memory: the model is unloaded after the configured idle period.
- Storage: SQLite metadata + normalized float32 embeddings.
- Incremental scan: size + mtime are checked before encoding; unchanged files are not reprocessed.
- Search refusal: results below `minScore` are omitted instead of always returning a misleading nearest neighbor.
- Network: binds to `127.0.0.1` only and uses a separate local token for non-health endpoints.
- The heavy runtime/model cache is not stored in this Git repository.

## HTTP API

- `GET /health`: health/status summary.
- `GET /status`: authenticated detailed status.
- `POST /scan`: incremental image scan.
- `POST /search`: semantic image search.
- `POST /model/unload`: force memory release.

Example search body:

```json
{"query":"有瀑布、森林、农田和动物的卡通游戏地图","topK":5,"minScore":0.68}
```

When no candidate crosses the threshold the service returns `items: []` and
`reason: "no_confident_match"`.

## Runtime layout on this machine

Small state/config lives under:

`%LOCALAPPDATA%\OpenQuickHost\SemanticIndex`

Large runtime and model assets may live on another local disk and are referenced by config.
They are reproducible caches and should not be cloud-synced.

The Yanzi mini-app `semantic-search` is the capability provider. It registers:

- `semantic.search`
- `semantic.scan`
- `semantic.status`

This lets AI, LocalAgentApi, WebView apps, and other mini-apps use the same index without
embedding model-specific code in each caller.
