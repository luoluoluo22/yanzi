# Agent Registry / Context Bootstrap v1

A ChatGPT tab ID is *not* an agent identity. The durable identities are the
Yanzi-managed supervisor/child IDs, backed by verified conversation URLs.

## Opt-in creation

The existing endpoints keep their prior semantics by default. To enable
automatic context injection, pass `"bootstrap":true` when creating a managed
supervisor. All children created with its `parentManagedId` automatically
inherit bootstrap mode.

```json
POST /api/managed-parents
{"requestKey":"demo:supervisor:001","title":"测试主管","prompt":"只回复：主管已启动","bootstrap":true}
```

The initial prompt runs in a **new saved** ChatGPT conversation. On the first
real browser reply, Bridge verifies `url`, `conversationId`, `tabId` and
`temporary:false`. It then queues exactly one `agent-bootstrap:supervisor`
job that continues **that same verified conversation** with an identity card.
The card carries an agent ID, conversation URL, task, role, parent ID and local
API reference. It contains no bearer credential.

The supervisor stays `bootstrapping` until a same-conversation reply includes
`YANZI_BOOTSTRAP_ACK:<agentId>`. If identity/ACK fails or delivery is
interrupted, the agent enters `needs_review` without blindly sending again.

When confirmed:

```json
POST /api/subagents
{"requestKey":"demo:child:001","parentManagedId":"<supervisor-id>","title":"小任务","prompt":"只回复：完成"}
```

Each child likewise gets its own identity card after the first real reply
creates its URL. Its first-round result is delivered to the designated
supervisor by existing durable `managed-parent-return` jobs; the supervisor
receives three separate messages for three independently completed children.
Never infer a parent from the active browser tab.

## Self context API

- `GET /api/agents`: list registered agents and bootstrap states.
- `GET /api/agents/{agentId}/context`: authoritative explicit-ID context.
- `GET /api/managed-parents/{id}`: supervisor state and child deliveries.
- `GET /api/subagents/{id}`: child state, real URL and job history.

Every API uses the existing localhost Bridge bearer authentication. Identity
is data, not an authentication token. User-provided `agentId` cannot bypass
Bridge access rules.

For **authorized host-side** Node scripts, this is the self() pattern:

```js
import { createAgentClient } from './agent-sdk.mjs';
const agent = createAgentClient({ agentId: '<ID from bootstrap>' });
const me = await agent.self();
const children = await Promise.all([
  agent.spawn({requestKey:'demo:a',title:'A',prompt:'只回复 A'}),
  agent.spawn({requestKey:'demo:b',title:'B',prompt:'只回复 B'}),
  agent.spawn({requestKey:'demo:c',title:'C',prompt:'只回复 C'})
]);
```

Alternatively set `YANZI_AGENT_ID` in the trusted script's environment to
avoid hardcoding IDs; **do not infer one from the latest active browser tab**.
The SDK reads the Bridge token from the current Windows user's protected local
app directory. Never copy that token or browser cookies into the prompt.

## Integration constraints

- An Agent can only call a local SDK if its authorized Raccoon/host executor is
  actually available in its ChatGPT conversation. Bootstrap alone does not
  grant new OS, connector or browser permissions.
- Bootstrap acknowledgments prove correct webpage message delivery and basic
  model reading; they do **not** prove a GPT model has actually called Raccoon.
- Versioned bootstrap is persisted in `state.json`; do not re-send after a
  service restart or uncertain submission. A separate reconciliation workflow
  is required for `needs_review`.
- Explicitly chosen saved conversations are required. Temporary chats are not
  valid durable agent identities.
- Existing legacy created agents without bootstrap remain backward-compatible.
