# Independent Yanzi Transfer Service integration

The Yanzi desktop host is a control plane only. The private standalone worker lives at:
`https://github.com/luoluoluo22/yanzi-transfer-service`.

On an installed device, `%LOCALAPPDATA%\YanziTransferService\config.json` and
`control.token` activate the lightweight broker. It speaks only to `127.0.0.1:39422`
using the per-user 256-bit token file. No cloud relay is used for file payloads.

- `files.p2p.connect`: ask the worker to ensure independent EasyTier P2P-only/KCP is running.
- `files.p2p.status`: direct link status, independent-worker indicator and active tasks.
- `files.p2p.stop`: explicit maintenance request (worker refuses when jobs are active).
- `files.p2p.jobs`: inspect persistent worker jobs, even after a Yanzi restart.
- `files.temporary.publish`: create a durable, authenticated receipt in the worker.
- `files.temporary.receive`: idempotently enqueue or reattach to the receipt's durable job; worker downloads to its own disk and verifies SHA-256. The broker copies a complete file into the old coordinator's receipt-safe destination.

The desktop host neither starts its own EasyTier core nor hosts the data endpoint when the standalone service is installed. A host process exit cannot stop the new worker. If worker files are absent, the earlier in-process feature is retained during staged migration; if the worker is installed but offline, requests fail closed rather than silently starting old transport or using the cloud.

The worker uses a separate private overlay and ports from the old 1.0.80 implementation to allow side-by-side rollout and QA. Windows Task Scheduler starts it independently at user logon. Production before-logon support requires a separately managed signed Windows Service wrapper. Do not share or commit control.token, private network secrets or receipts.

Test acceptance:
1. Independent worker and P2P link remain running when Yanzi exits/restarts.
2. Two-device file delivery continues and SHA-256 matches.
3. Worker crash restarts without data loss, restores the old job ID, and resumes from .part.
4. Explicit stop rejects active jobs and never affects Raccoon MCP or other EasyTier instances.
