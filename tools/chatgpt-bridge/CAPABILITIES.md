# Browser capability gateway v1

The local ChatGPT bridge now exposes an OpenCLI-style **site.command** catalogue.
It reuses Yanzi's existing browser extension, credentials, tab ownership checks,
per-tab serialization, four-job pool, and durable polling. It does not proxy the
official OpenAI API, read cookies, execute arbitrary JavaScript, or open a
remote network listener.

## Discovery

`GET http://127.0.0.1:53921/api/capabilities` requires the **same Bearer token**
as `/api/jobs`. It lists only commands with working existing executors:
`chatgpt.ask`, `chatgpt.read`, `chatgpt.new`, `chatgpt.status`,
`chatgpt.image`, `chatgpt.images` and `chatgpt.cleanup`.
Each descriptor includes the access mode and JSON input schema.

## Execute

`POST http://127.0.0.1:53921/api/capabilities/execute` accepts:

```json
{
  "capability": "chatgpt.ask",
  "arguments": {
    "prompt": "Say OK",
    "temporary": true,
    "closeAfter": true,
    "timeoutSeconds": 180
  }
}
```

Equivalent selection: `{"site":"chatgpt","command":"ask","arguments":{...}}`.

Headers: `Authorization: Bearer <local-token>`,
`X-Bridge-Request: 1`, `Content-Type: application/json`.

The response is HTTP 202: `{"capability":"chatgpt.ask","job":{...}}`.
The command is **asynchronous**: query `GET /api/jobs/{job.id}` until
`success`, `error`, `timeout` or `interrupted`. An interrupted job
may already have sent the message; **never blindly retry**. The underlying
browser executor still enforces tab ownership, temporary-chat verification,
draft protection and rate limits.

Unsupported capability IDs, extraneous fields, missing prompts or invalid
types return HTTP 400 and never enter the queue. Localhost, Host/Origin,
Bearer, and CSRF request-header checks apply to both routes.

## Extension design

Register a new provider only when its **real executor** has been implemented
and independently tested. A descriptor is not permission to automate every
website, bypass a site's checks, or make OpenCLI's local CDP daemon publicly
accessible. Future provider adapters may reuse this schema, but must provide
their own capability validation, browser-side execution and lifecycle tests.

Model/tool/project selection requires verified browser controls and is **not
advertised as supported** by this first version; OpenCLI's older hard-coded
model IDs are not suitable as an authoritative current model list.
