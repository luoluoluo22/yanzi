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

## Cloudflare Builds (Auto Deployment)

The GitHub repository `luoluoluo22/yanzi` is connected directly to Cloudflare. Pushing new commits to the `main` branch will automatically trigger builds and deployments for both the Worker and the Pages website on Cloudflare.

### Backend Worker

Use these Build settings for the backend Worker (`openquickhost-sync`) in the Cloudflare dashboard:

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

### Website (Cloudflare Pages)

The Pages project `openquickhost-site` is also connected to the repository:

- Production branch: `main`
- Build command: empty (static assets only)
- Output directory: `/website`
- Git deployments are active; pushing changes to the `website/` directory will automatically update the live site.

Known working worker deployment:

- Commit: `568ef01`
- Worker version: `aa1f2f57-8675-4161-a6fc-337e68f4ca25`

## Required Secrets

The Worker requires these secrets:

- `AUTH_TOKEN_SECRET`
- `RESEND_API_KEY`
- `RESEND_FROM_EMAIL`

If `AUTH_TOKEN_SECRET` is missing or empty, login fails with:

```text
Imported HMAC key length (0) must be a non-zero value up to 7 bits less than,
and no greater than, the bit length of the raw key data (0).
```

Set `AUTH_TOKEN_SECRET` with:

```powershell
$env:CLOUDFLARE_API_TOKEN = "<token>"
$secret = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$secret | npx wrangler secret put AUTH_TOKEN_SECRET --config wrangler.jsonc
```

Registration and password-reset verification emails use Resend:

- API endpoint: `https://api.resend.com/emails`
- API key binding: `RESEND_API_KEY`
- Sender binding: `RESEND_FROM_EMAIL`

If either Resend binding is missing or empty, sending a registration code fails
with:

```json
{
  "error": "email_provider_not_configured",
  "message": "Email provider is not configured"
}
```

Check configured Worker secrets:

```powershell
npx wrangler secret list --config wrangler.jsonc
```

Set the Resend values:

```powershell
$env:CLOUDFLARE_API_TOKEN = "<token>"
"re_xxx" | npx wrangler secret put RESEND_API_KEY --config wrangler.jsonc
"Yanzi <noreply@your-verified-domain.example>" | npx wrangler secret put RESEND_FROM_EMAIL --config wrangler.jsonc
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

Useful WAF allow/skip expression:

```text
http.host eq "sync.luoluoluo.cc.cd"
and starts_with(http.request.uri.path, "/v1/")
and any(http.request.headers["x-yanzi-client"][*] in {"desktop" "mobile" "web"})
and any(len(http.request.headers["x-yanzi-client-version"][*])[*] gt 0)
```

Use this to skip custom WAF / managed WAF / rate limiting for trusted Yanzi API
requests, or raise the rate limit threshold for these requests.

Cloudflare Free plan Rate Limiting rules cannot use `http.request.headers` in
the rate limit expression. The API returns:

```text
not entitled: the use of field http.request.headers is not allowed,
an higher Advanced Rate Limiting plan is required
```

Current verified rate limiting rule:

```text
description: Rate limit login endpoint
expression: http.request.uri.path eq "/v1/auth/login"
period: 10
requests_per_period: 30
mitigation_timeout: 10
```

This replaced the previous `2 requests / 10 seconds` limit, which caused normal
desktop login retries to hit Cloudflare `1015 Too Many Requests`.

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
