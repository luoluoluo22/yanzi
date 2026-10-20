# 燕子多 Agent 真实执行器接入（2026-10-20）

## 改造目标与范围

在原有 `delivery-ledger` + Bridge API 上增加 **显式启动、隔离运行、可核验交接** 的 Worker。借鉴 `grill-engineering` 的依赖任务、持续执行与独立验收；**不直接安装/部署上游 Runner**。

- `tools/chatgpt-bridge/agent-cli-runner.mjs`：支持 `agy` 和 `codex`；使用原生命令行参数数组及 `shell:false`、限时执行、流式输出长度限制。默认使用沙箱，不使用免审批的危险参数。
- `tools/chatgpt-bridge/delivery-worker.mjs`：按角色调用 Bridge `claim`、`renew`、`development` / `review`，调用失败时 `block`，并持久化失败原因。每次只执行一个任务，**不自动开后台守护任务**。
- `tools/chatgpt-bridge/delivery-ledger.mjs`：任务增加不可变 `prompt`，执行租约续期及带身份校验的主动阻断。原先任务未填写 prompt 仍可创建，但不允许 CLI Worker 误领后假装完成。
- `tools/chatgpt-bridge/github-integration-verifier.mjs`：独立只读获取 PR 和 GitHub Checks / Commit Status，严格比对原候选 SHA、目标分支、Merge SHA、已合并状态和必需检查列表。**绝不自动合并 PR。**
- `tools/chatgpt-bridge/delivery-api.mjs`：未经独立 `integrationVerifier` 的 `/integration` **默认 HTTP 403**，不能通过 Worker 自述的 `ciPassed:true` 绕开门禁。
- `tools/chatgpt-bridge/manual-live-delivery-smoke.mjs`：需要显式 `--confirm-real-cli` 才执行，并且每次自行创建一次性 Git 仓库、linked Worktree 和隔离 Bridge 数据目录。

## 真实环境探针

- Windows `agy 1.4.0`：只读沙箱、非交互一次性 prompt，实际退出码 0、返回标记 `YANZI_EXECUTOR_SMOKE_OK`。
- Windows Codex 0.116.0：确认二进制存在，**尚未用实际任务/账号进行验证**。
- Ubuntu 服务器 Codex 0.160.1：已安装但 `codex login status` 为 Not logged in，不能当作可用云端执行器。
- Raccoon MCP：经主机远程命令确实可以运行 Git、Node 与沙箱测试，**Raccoon 本身不是可以直接由 Node import 的 SDK**；现阶段用于从外部调用 Worker 命令和核对设备状态，跨设备统一任务账本仍需后续网络与认证适配。

## 强制保护

1. **仅 linked Git Worktree** 可领取任务：校验 `.git` 为指向工作树的文件、工作目录 Git Root 身份一致、非 main/master 分支、工作区原本干净。Windows Temp 路径可呈现短路径别名（如 ADMINI~1），故按文件系统 inode/dev 比较，而不做脆弱的字符串比较。
2. 开发 Agent 执行退出 0 **不代表完成**：须产生新的 Git Commit，且不能改写原提交历史，工作区仍干净，才登记开发完成。
3. QA 需由不同 workerId 领取，审查指定候选 SHA；本机 Git `show --check` 只是 standards 的证据。spec 需要结构化 JSON；没有可信 E2E verifier 时状态强制 blocked，不会通过验收。
4. 失去租约续期、外部 CLI 超时或输出无法确认，进入待人工检查，**不盲目启动新的模型调用**。
5. 不在正式 Bridge 自动启用 `enableDeliveryLedger`，不启用正式 Agent 自动合并，不覆盖用户未提交代码，不输出或上传 Token/Cookie。
6. 现阶段 HTTP Bridge 仅监听本机回环；云服务器无法直接领取桌面本地账本。不能把这个局部功能说成“云端多电脑调度已上线”。

## 示例：手动领取一项任务

先由可信程序在隔离 Bridge 上创建：

```json
{"runId":"manual-agent-demo","tasks":[{"id":"change-readme","dependsOn":[],"prompt":"在隔离项目内修复 README 并创建本地 Git Commit"}]}
```

启动时显式传入工作树、Token 文件及角色（Token 不出现在命令行）：

```powershell
node tools/chatgpt-bridge/delivery-worker.mjs --bridge http://127.0.0.1:53921 --token-file <trusted-token-file> --run-id manual-agent-demo --worker-id agy-dev-one --workspace <isolated-linked-worktree> --role development --backend agy
```

QA 使用不同 `worker-id` 且 `role reviewer`。当前通用 CLI 没有自动挂载可信 E2E verifier，故单独执行时即使 AI 给出 pass，`e2e` 仍标记 blocked。建议只用于受控演示和迭代，不投入无人监管的生产交付。

`createBridge` 的 `integrationVerifier` 默认为 `null`。有经批准的具体仓库/PR/检查列表后，可由受信任宿主注入 `verifyMergedPR` 的回调，而不是接受 Worker 的自述。

## 测试

- 定向测试：`node --test test/delivery-worker.test.mjs test/github-integration-verifier.test.mjs test/delivery-ledger.test.mjs test/delivery-api.test.mjs`，**17/17 通过**。
- 现有 Bridge 全量：`npm test`，在补齐两个原有但未纳入 Git 的本地实验 fixture 后，**106/106 通过**。自动化测试里的 Agent 输出是模拟的，但确实创建真实临时 Git 仓库、提交、经过 HTTP 领取及核验。
- 真实模型开发→QA 的一次性端到端过程另有 `manual-live-delivery-smoke.mjs`；应单独记录结果，不与模拟测试混淆。

## 尚未完成

1. 不可信生成代码仍缺少系统级强隔离的 CI/QA 沙箱。不能依赖模型“请不要读取秘密”的提示词保护凭据。
2. 云端唯一权威数据库、跨设备 fencing token、远程执行器在线/离线恢复、设备身份鉴别尚未落地。
3. 生产级 GitHub PR 自动提交与发布回滚、云端 Codex 认证还未验证。
4. 真实 CI 证据必须由确定性的验证器独立获取，不能将测试示例里的人工模拟 E2E callback 作为生产实现。

## 真实 CLI 开发 / QA 两轮验证结果（补充）

- 首轮真实 Antigravity：临时 Worktree 开发完成并产生 Git Commit `7af192d9…`；e2e 和 standards 检测 pass；模型验收结果被遗漏于 NDJSON 的 `result.response`，因此 spec=blocked，阻止了集成。
- 修复 AGY 输出解析并添加 nested-result 回归后，再运行一次真实 Antigravity 开发 + 不同 workerId 的 QA。生成 Commit `759cd2f9…`，E2E / Standards / Spec 全部 pass，最终任务状态为 `ready_for_integration`。
- E2E 为可信测试程序对一次性 README 标记的实际读取断言；两轮都没有操作燕子正式工作树、推送远程 PR 或自动合并。验证脚本：`node tools/chatgpt-bridge/manual-live-delivery-smoke.mjs --confirm-real-cli`。
- 子进程环境现在会过滤常见 Token、Password、Secret、API_KEY、Cookie 变量。实际沙箱仍需进一步进行权限穿透和本机数据隔离测试，不能视为绝对安全。
