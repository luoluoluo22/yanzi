# Local Agent API Reference

Yanzi exposes a localhost HTTP API for extension management, execution control, configuration, and diagnostics. This API is designed for same-machine agent automation.

## Connection Details

- **Base URL**: `http://127.0.0.1:53919`
- **Authentication Header**: `X-Yanzi-Token: <token>` (Obtain token from `%LOCALAPPDATA%\OpenQuickHost\appsettings.local.json` under `agentApiToken`)

---

## 1. Diagnostics & Health

### Health Check
```http
GET /health
```
Response: `200 OK` (Plain text `"OK"`)

---

## 2. Extension CRUD Management

### List All Extensions
```http
GET /v1/extensions
```
Response: JSON list of local extensions.

### Get Extension Template
```http
GET /v1/extensions/template
```

### Get Extension Manifest
```http
GET /v1/extensions/{id}
```

### Create Extension
```http
POST /v1/extensions
Content-Type: application/json

{
  "manifest": "{...manifest json string...}"
}
```

### Replace/Update Extension
```http
PUT /v1/extensions/{id}
Content-Type: application/json

{
  "manifest": "{...manifest json string...}"
}
```
*Note: The `id` in the URL must match the `id` defined in the manifest.*

### Rename Extension
```http
PATCH /v1/extensions/{id}/rename
Content-Type: application/json

{
  "name": "New Extension Name"
}
```

### Set Hotkey/Shortcut
```http
PATCH /v1/extensions/{id}/shortcut
Content-Type: application/json

{
  "shortcut": "Ctrl+Alt+T"
}
```
*Note: Provide `""` or `null` to clear the shortcut.*

### Delete Extension (To Recycle Bin)
```http
DELETE /v1/extensions/{id}
```

---

## 3. Extension Execution Control

### Run Extension
```http
POST /v1/extensions/{id}/run
Content-Type: application/json

{
  "input": "optional text parameter passed to extension"
}
```
*Note: Runs the extension in the background. If successful, returns immediately with `{"ok": true, "success": true, "output": "native-window-started"}` for windowed modes.*

### Get Execution Status
```http
GET /v1/extensions/{id}/status
```
Response: Returns run status metrics.

### Stop Extension
```http
POST /v1/extensions/{id}/stop
```
Response: `{"ok": true, "stopped": true}`

---

## 4. Extension Storage Management

### Trigger Cloud Sync
```http
POST /v1/storage/{id}/sync
```
Response: Forcefully synchronizes the extension's storage files (e.g. settings, cached assets) to the configured cloud WebDAV/Git provider.

### Write Storage File
```http
PUT /v1/storage/{id}
```
*Note: Direct storage file modification and state updates.*

---

## 5. Recycle Bin Management

### List Recycle Bin
```http
GET /v1/extensions/recycle-bin
```
Response: Returns a list of deleted extensions pending permanent deletion.

### Restore Extension
```http
POST /v1/extensions/recycle-bin/{id}/restore
```
Response: Restores the extension back to the active extensions list.

### Permanently Delete Extension
```http
DELETE /v1/extensions/recycle-bin/{id}
```

---

## 6. App Store & Publishing

### Install from App Store
```http
POST /v1/store/extensions/{id}/install
```

### Publish Extension to Store
```http
POST /v1/extensions/{id}/publish
```
Response: `{"ok": true, "message": "Successfully published: https://..."}`

### Unpublish Extension from Store
```http
POST /v1/extensions/{id}/unpublish
```

---

## 7. App Settings & User

### Get Current User Profile
```http
GET /v1/user/me
```

### Get App Settings
```http
GET /v1/settings
```

### Update App Settings
```http
PATCH /v1/settings
Content-Type: application/json

{
  // Settings payload fields
}
```

---

## 8. Quick Panel Layout Control

### Remove Item from Quick Panel
```http
DELETE /v1/quickpanel/remove?extensionId={extensionId}
```

### Reorder Quick Panel Items
```http
PUT /v1/quickpanel/reorder
Content-Type: application/json

[
  "extension-id-1",
  "extension-id-2",
  "extension-id-3"
]
```
