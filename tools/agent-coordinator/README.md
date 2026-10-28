# 燕子 Agent Coordinator（云端统一任务账本）

本组件复用 `tools/chatgpt-bridge/delivery-ledger.mjs` 的任务依赖、租约、开发-独立 QA 与失败恢复机制，提供 SQLite 持久化的单一权威任务入口。它**不是第二套燕子管家，也不是自动 GitHub 合并服务**，不影响 `/opt/yanzi-butler` 和已运行的燕子消息服务。

## 技术约束

- Node.js 22（必须显式增加 `--experimental-sqlite`，当前 Ubuntu 22.12 与 Windows 22.17 均验证）；零新增 NPM 运行依赖。
- HTTP **只监听 127.0.0.1**。严禁直接用 `0.0.0.0` 对外发布。外部设备仅可经受信任的私有隧道转发到本机回环，或经过单独评审的 HTTPS + 身份网关接入。
- `YANZI_AGENT_ADMIN_TOKEN`：64 位十六进制随机字符串，仅通过受保护环境变量提供。不要放入 Git/日志/任务描述。
- `YANZI_AGENT_DB`：持久 SQLite 路径；运行用户需要写权限；建议专属数据目录、专属服务用户与可靠备份。单队列状态总量上限约 8 MiB；适合当前 3 个 Worker 的调度规模，不适合大规模任务中心。
- SQLite 使用 WAL 与 `BEGIN IMMEDIATE`，支持同机多进程安全串行写入。禁止把 SQLite DB 放到共享网络盘上，不要跨设备共享文件；**所有设备访问同一 Coordinator API**。
- 新设备需管理员登记，服务端仅存 SHA-256 摘要，不存原始设备 Token；Token 只在首次注册/重新注册的响应中返回。设备权限只支持 `development` 和 `reviewer`。如需停用设备，使用管理员撤销接口。
- 开发与 QA 不允许同一 `deviceId`；开发人员提交完整 Git Commit SHA、QA 只审查对应 SHA，E2E 未获可靠验证不得通过。跨设备代码同步需要各自私有 Git 分支；本组件**不会自动把本地 Worktree 同步到另一电脑**。
- 断联导致租约超时，任务会变为 `blocked/external_outcome_uncertain`。先人工核查外部提交/修改，再由经过授权的主管显式 `retryBlocked`，禁止盲目重复写入。
- 不开放自动 `integration` API，不能从设备凭证调用自动合并/发布。

## 隔离测试

```bash
node --experimental-sqlite --test tools/agent-coordinator/test/*.test.mjs
```

测试包含双设备并发领取、服务重启后任务恢复、SHA fencing、角色校验与撤销、设备离线导致的不确定结果、安全 URL、真实临时 Git Worktree 的提交和另一设备身份的独立 QA。CLI 模型输出在这些自动化测试中为模拟数据；上一轮 Antigravity 真实开发与 QA 的测试单独记录在 `docs/continuous-improvement/agent-executor-integration-2026-10-20.md`。

## 单独启动（仅内网回环测试）

```bash
export YANZI_AGENT_DB=/some/private/data/queue.sqlite
export YANZI_AGENT_ADMIN_TOKEN="$(openssl rand -hex 32)"
export YANZI_AGENT_PORT=58081
node --experimental-sqlite tools/agent-coordinator/server.mjs
```

注意：以上只是演示方式，交互 Shell 退出后令牌会丢失，不可作为持久化生产部署方案。永久服务需安全凭据注入，避免日志泄露与其他 Agent 共用凭据。服务器现有 Cloudflare Tunnel 路由不得未经授权直接接入此服务。

### 管理员 HTTP API（仅受保护的回环链路）

请求头 `Authorization: Bearer <adminToken>`、写操作另加 `X-Yanzi-Agent-Request: 1`。

- `GET /health`：无密钥健康检查，不包括任务数据。
- `POST /v1/admin/devices`：登记 `{"deviceId":"desktop-01","grantedRoles":["development"]}`；响应包含仅此一次的新设备 Token。
- `DELETE /v1/admin/devices/<deviceId>`：立即禁止已撤销设备访问。
- `GET /v1/admin/devices`：设备状态与最近接触时间，不返回 Token。
- `POST /v1/admin/runs`：创建 DAG 任务（同原 `delivery-ledger` 格式）。
- `GET /v1/admin/runs/<runId>`：查看工单、事件及状态。

### Worker（每次只领取一项）

在受信任电脑上将其个人设备 Token 保存在权限受限的文件中，不要把 Token 放入进程命令行、聊天、Git 或 Cloudflare Worker Secrets 的共享变量。

```powershell
node tools/agent-coordinator/worker.mjs --server http://127.0.0.1:58081 --token-file <private-file> --run-id <runId> --device-id desktop-01 --workspace <clean-linked-worktree> --role development --backend agy
```

- 以上 `127.0.0.1` 在远程电脑上必须是指向服务器的**已经认证的私有隧道本地端口**，不是电脑自己的随意本地服务。
- 直接跨公网仅接受 **HTTPS** 且须配置专门身份网关，默认没有启用公网入口。
- QA 需另一 `device-id`、`role reviewer`；没有独立可信 E2E verifier 时通用 Worker 会故意将 QA 标为 blocked，无法伪装成通过。
- 不启动常驻 Worker、不添加定时任务、不更改现有宿主，直到目标设备和数据隔离验收。

## 下一步尚待完成

1. 审批单独 HTTPS/私网隧道、设备证书/Access，并在实际两台机器进行凭证登记与真正的网络端到端测试。
2. CI 中自动校验 Commit 与工作树同步；QA 引入针对仓库的独立可信端到端测试。不能仅信任模型自报。
3. 工作树生命周期清理、云端任务状态接入燕记 APP、与 `/opt/yanzi-butler` 每小时巡检对接。
4. 评估未来用正规任务表（目前 SQLite 存储单个有界 JSON Ledger）降低长期写入放大。

管理恢复入口：`POST /v1/admin/runs/<runId>/retry`。必须使用管理员 Token，并提交 `{"taskId":"a","reason":"已核查旧任务没有不可逆外部修改"}`。仅被阻断的任务可以重派，代次递增；禁止自动重试不确定的写入结果。
