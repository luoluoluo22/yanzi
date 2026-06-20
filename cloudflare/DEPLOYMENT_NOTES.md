# OpenQuickHost Sync Backend Deployment Notes

This file records the Cloudflare deployment details that were verified during the
2026-06-20 sync backend incident.

## Worker

- Worker name: `openquickhost-sync`
- Custom domain: `https://sync.luoluoluo.cc.cd`
- Workers.dev URL: `https://openquickhost-sync.a1137583371.workers.dev`
- Main entry: `cloudflare/src/index.js`
- Root config used by Cloudflare Builds: `wrangler.jsonc`
- Backend folder config: `cloudflare/wrangler.toml`

## Cloudflare Builds

The backend Worker is connected to GitHub repository `luoluoluo22/yanzi`.

Use these Build settings for the backend Worker:

- Build command: empty
- Deploy command: `npx wrangler deploy`
- Root directory: empty
- Production branch: `main`
- Path includes: `cloudflare/*`, `wrangler.jsonc`

Do not set root directory to `/cloudflare`.
Do not set it to `cloudflare ` with a trailing space.
Both cause `root directory not found`.

Do not use `cd cloudflare && npx wrangler deploy` when Cloudflare Builds already
has a root directory configured.

Known working deployment:

- Commit: `568ef01`
- Worker version: `aa1f2f57-8675-4161-a6fc-337e68f4ca25`

## Required Secret

The Worker requires this secret:

- `AUTH_TOKEN_SECRET`

If it is missing or empty, login fails with:

```text
Imported HMAC key length (0) must be a non-zero value up to 7 bits less than,
and no greater than, the bit length of the raw key data (0).
```

Set it with:

```powershell
$env:CLOUDFLARE_API_TOKEN = "<token>"
$secret = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$secret | npx wrangler secret put AUTH_TOKEN_SECRET --config wrangler.jsonc
```

Do not commit the secret value.

## Client Headers

Cloudflare WAF / rate limiting may require Yanzi client identity headers.
The clients currently send:

Desktop:

```http
User-Agent: YanziClient-Desktop/0.2.3
X-Yanzi-Client: desktop
X-Yanzi-Client-Version: 0.2.3
```

Mobile:

```http
User-Agent: YanziClient-Mobile/0.1.0
X-Yanzi-Client: mobile
X-Yanzi-Client-Version: 0.1.0
```

Web:

```http
X-Yanzi-Client: web
X-Yanzi-Client-Version: 0.1.0
```

The Worker CORS allow-list must include:

```http
content-type,authorization,x-yanzi-client,x-yanzi-client-version,x-api-version,x-client-version
```

## WAF / Rate Limiting

If `POST /v1/auth/login` returns Cloudflare HTML with `403` or `1015`, the request
was blocked before reaching the Worker.

Useful allow/skip expression:

```text
http.host eq "sync.luoluoluo.cc.cd"
and starts_with(http.request.uri.path, "/v1/")
and any(http.request.headers["x-yanzi-client"][*] in {"desktop" "mobile" "web"})
and any(len(http.request.headers["x-yanzi-client-version"][*])[*] gt 0)
```

Use this to skip custom WAF / managed WAF / rate limiting for trusted Yanzi API
requests, or raise the rate limit threshold for these requests.

## Verification

Health check:

```powershell
curl.exe --ssl-no-revoke -i https://sync.luoluoluo.cc.cd/health
```

Expected response:

```json
{
  "ok": true,
  "now": "..."
}
```

Login route sanity check with a deliberately wrong password:

```powershell
$body = @{ email="1137583371@qq.com"; password="wrong-password-for-check" } | ConvertTo-Json -Compress
$headers = @{
  "X-Yanzi-Client" = "desktop"
  "X-Yanzi-Client-Version" = "0.2.3"
  "User-Agent" = "YanziClient-Desktop/0.2.3"
  "Accept" = "application/json"
}
Invoke-WebRequest -UseBasicParsing `
  -Method Post `
  -Uri "https://sync.luoluoluo.cc.cd/v1/auth/login" `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body
```

Expected result for wrong password:

```json
{
  "error": "invalid_credentials",
  "message": "Invalid email or password"
}
```

This means the request reached the Worker and the auth path is functioning.

