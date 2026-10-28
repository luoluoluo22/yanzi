# Cloudflare Website Worker: deployment separation

The production website and sync API share a repository but **must remain separate Workers**.

| Component | Worker | Source / config | Hostname |
| --- | --- | --- | --- |
| Public website | `yanzi` | `website/wrangler.jsonc`, `website/` static files | `yanzi.luoluoluo.cc.cd` |
| Cloud sync and auth | `yanzi-sync` | `cloudflare/wrangler.toml` (or root `wrangler.jsonc` in backend-only commands) | `sync.luoluoluo.cc.cd` |

## Cloudflare Workers Builds settings for `yanzi`

Production trigger (`main`): **Root directory `/website`**, build command empty, **deploy command `npx wrangler deploy`**, watched paths `website/**`.

Preview trigger (`*` except `main`): **Root directory `/website`**, build command empty, **deploy command `npx wrangler versions upload`**, watched paths `website/**`.

These settings are stored in Cloudflare Workers Builds, **not** in the repository. Verify them in Cloudflare Workers → `yanzi` → Settings → Build when reconnecting Git or recreating triggers. Changes to either build root that revert it to `/` will invoke the repository-root `wrangler.jsonc`, which targets the sync Worker. Cloudflare may override its name to `yanzi`; the resulting Durable Object binding then fails with error 10061, or an unintended deployment may replace website content.

The website's `.assetsignore` excludes `.wrangler/` and `wrangler.jsonc`, because Wrangler creates temporary files beneath the website root during builds. Do not expose build artifacts as public assets.

## Verification and rollback

Run `node --test scripts/verify-website-deployment.mjs`, then from `website/` run `npx wrangler deploy --dry-run`. In production verify `https://yanzi.luoluoluo.cc.cd/`, `/store.html`, `/docs/getting-started.html`, and `/assets/auth.js` return 200 with the correct HTML / JavaScript; verify `https://sync.luoluoluo.cc.cd/health` remains a JSON 200. On a bad website deployment use the preceding successful version of Worker `yanzi` in Cloudflare Deployments; do not deploy any website code to `yanzi-sync`.

2026-10-13 incident: a missing custom domain was rebound, then both website triggers were corrected from root `/` to `/website`; production build was re-run on `main` successfully. Do not remove or rebind the separate `sync` custom domain.

The existing Engineering foundation regression GitHub Action automatically runs `cloudflare/tests/website-deployment-config.test.mjs` as part of its Worker test suite. No additional GitHub workflow permission or standalone Actions workflow is necessary.
