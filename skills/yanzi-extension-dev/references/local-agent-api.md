# Local Agent API

Default endpoint:

```text
http://127.0.0.1:53919
```

Header:

```text
X-Yanzi-Token: <token>
```

## Health

```http
GET /health
```

## List Extensions

```http
GET /v1/extensions
```

## Get Template

```http
GET /v1/extensions/template
```

## Get Manifest

```http
GET /v1/extensions/{id}
```

## Create Extension

```http
POST /v1/extensions
Content-Type: application/json

{
  "manifest": "{...manifest json string...}"
}
```

The manifest may contain inline PowerShell script fields such as:

- `runtime`
- `entryMode`
- `script.source`

## Replace Extension

```http
PUT /v1/extensions/{id}
Content-Type: application/json

{
  "manifest": "{...manifest json string...}"
}
```

The `id` inside the manifest must match the URL `id`.

## Rename Extension

```http
PATCH /v1/extensions/{id}/rename
Content-Type: application/json

{
  "name": "新名称"
}
```

## Set Shortcut

```http
PATCH /v1/extensions/{id}/shortcut
Content-Type: application/json

{
  "shortcut": "Ctrl+Alt+T"
}
```

Use `""` or `null`-equivalent omission to clear the shortcut.

## Delete Extension

```http
DELETE /v1/extensions/{id}
```
