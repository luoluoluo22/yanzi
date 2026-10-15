# 燕子 ChatGPT 子 Agent 派发与续聊（2026-10-09）

## 目标与语义

每次子任务派发创建专属 **非激活的 ChatGPT 标签页**。燕子本地 Bridge 为其保存两个不同维度的身份：

- **短期身份**：`tabId`，用于当前浏览器会话中的快速定位，不保证浏览器重启/标签关闭后有效。
- **长期身份**：`conversationId` 和规范 `https://chatgpt.com/c/<conversationId>` URL，首次 ChatGPT 回复成功后从真实网页回执获得，用于后续重新打开并续聊。
- **调度身份**：`subagent.id`、稳定 `requestKey`、每轮 `jobId`，用于重复提交去重和历史跟踪。

这里的“子 Agent”是一个**隔离 ChatGPT 对话会话**，不是自动具有独立插件凭证、文件操作权限或持续运行能力的操作系统进程。要让子任务修改源码，仍需明确授予该对话相关操作工具；派发子任务本身不会擅自继承父会话的权限。

## 本机命令行接口

在仓库根目录：

```powershell
# 派发一个真正的新子任务（始终创建独立聊天，不复用他人标签页）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 `
  -Action start -Title "燕子截图改进" `
  -Prompt "分析最近的 OCR 回归并提出下一轮修复方案" `
  -RequestKey "ocr-improve:iteration-001"

# 查看所有任务，或查询某个子 Agent 的历史与对话 URL
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 -Action list
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 -Action status -SubagentId <UUID>

# 新一轮继续同一段对话（标签已关闭也会重建后台标签，但不会新建对话）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 `
  -Action continue -SubagentId <UUID> -Prompt "继续，针对失败用例补齐测试" `
  -RequestKey "ocr-improve:iteration-002"
```

> `RequestKey` 必须稳定且同一次请求重试保持一致，同键不同内容会被拒绝，防止重复派发。API 接受 Bearer 本地 Token，脚本会从本机受限目录读取，不需要在命令中明文传输 Token。

### 可选：把子 Agent 结果自动返回父对话

父对话在 `chatgpt-bridge` 上已有 `originId` 时，启动子任务时传入 `-ParentOriginId <UUID>`。这样每轮子任务完成后，会自动生成 `chatgpt_feedback_send` Job，将**子 Agent ID、任务状态、结果摘要、子对话 URL** 返回父对话。可以在 `status` 结果里查看各轮 `returnJobId` 的执行结果。

如果父对话来源没有绑定，仍可创建子 Agent 并查询结果，但**不能安全推测哪一个 ChatGPT 对话代表父任务**；结果保留在本地 Bridge 中，等待明确绑定。

## 本地 API

地址：`http://127.0.0.1:53921`，Bearer Token 和变更请求头 `X-Bridge-Request: 1` 与原 Bridge 相同。

| API | 作用 |
|---|---|
| `POST /api/subagents` | 派发新任务，参数 `{requestKey,title,prompt,parentOriginId?,timeoutSeconds?}` |
| `GET /api/subagents` | 列出所有子任务 |
| `GET /api/subagents/{id}` | 查询任务、对话 URL、tabId、轮次和原始 Job |
| `POST /api/subagents/{id}/messages` | 继续同一子 Agent，参数 `{requestKey,prompt}` |
| `POST /api/subagents/{id}/return` | 如果已有父绑定，手动触发结果回传（重复调用不会重复发送） |

### 安全边界

- 每个新子任务使用 `chatgpt_send` 配置 `newChat:true,tabPolicy:'new',temporary:false,closeAfter:false`，只打开 **inactive** 标签。
- 首次回复回传 URL / Conversation ID / Tab ID 互相核验；不合格则标记 `needs_review`，不能续聊。
- 续聊使用 `chatgpt_subagent_continue` 严格比对 URL 和 `conversationId`；如果没有标签，则新开**原对话 URL**，禁止选一个其他 ChatGPT 聊天代替；两个相同的标签同时存在则拒绝，避免串话。
- 将续聊后出现的新标签登记到燕子管理列表，以便后续检索与安全清理。
- 子任务完成时持久化回复、Job ID、URL、时间；服务重启时正在执行的任务会变为 `needs_review`，不盲目重发，以防重复提交。仍在队列中的任务可继续排队。
- 仅由明确注册的父对话接收自动回传；否则保留反馈，不向当前活跃聊天猜测发送。
- 不会随意关闭用户的原聊天标签页。只有显式关闭、且确认是燕子自建且非活动的标签页，才允许清理。

## 真实浏览器验收记录

2026-10-09，Bridge v0.5.51，Edge extension v0.5.51，使用同一子 Agent 三轮：

1. 第一次创建专属隐藏标签 ID `399749515`，正式 URL `https://chatgpt.com/c/6ac8437c-58d0-83ea-ab0d-f215ab8ef092`，正确回复 `YANZI_SUBAGENT_E2E_OK_1`。
2. 第二轮仍然在该标签页、同一 URL 回复 `YANZI_SUBAGENT_E2E_OK_2`。
3. 显式关闭燕子自建测试标签页，再续聊第三轮；系统重新打开同一 URL，**标签 ID 更新为 `399749522`，conversation ID 不变**，正确回复 `YANZI_SUBAGENT_E2E_OK_3`。

第三轮发现重建标签的 `managed` 状态未正确登记，随后修复，并增加回归用例；正式再验收需以部署后的状态为准。

对应子 Agent ID：`a1c713ca-0f94-4677-8d12-042c76c10020`。

父来源当前未绑定，**父 ChatGPT 对话的真实自动回传还未进行生产验收**。但隔离 Bridge + 模拟扩展测试已验证显式绑定父对话后的回传 Job 创建、正确目标 URL 和重复请求去重。

## v0.5.52 最终回归补充

2026-10-09 热更新到扩展 **v0.5.52**、Bridge 对应修复版后，使用第二个隔离子 Agent `a6a2fdd3-47f9-44f9-ae10-a65654254f07`：

1. 首次创建后台标签 `399749526`，得到正式对话 `https://chatgpt.com/c/6ac84538-4c0c-83ea-be6e-74f7b628ab67`，正确回复 `YANZI_MANAGED_RECOVER_1`，标签受燕子管理。
2. 仅关闭该测试标签页，随后继续发送子任务。浏览器新建后台标签 `399749530`，URL 不变，正确回复 `YANZI_MANAGED_RECOVER_2`，**管理状态 `managed=true`**。
3. 对首次创建请求、第二轮续聊请求分别按原 `requestKey` 再次发送，返回 HTTP 200，子 Agent ID / Job ID 原值不变，轮次仍为 2，没有重复派发。
4. Bridge 重启后最初的子 Agent 仍保持 3 轮记录与最后回复，说明长期对话绑定已持久化。
5. Bridge/Web 扩展全部自动化回归 **55/55 通过**。

仍需明确：父对话 `originId` 为可选参数，因此以上生产实测没有向这个移动端 ChatGPT 会话**自动推送新消息**。此前使用模拟父来源的自动回传用例已通过；真实父对话自动回传需先明确绑定网页来源。不能将“子 Agent 的内容可由 Bridge 查询”和“消息已经自动送回当前 ChatGPT 会话”混为一谈。

## 父任务不依赖标签页的事件收件箱（2026-10-09）

**核心改变**：派发时可提供独立的 `parentTaskId`，例如 `yanzi:quality-loop-001`。这是父**任务**的关联键，不是 ChatGPT 对话 ID，也不是 Edge 的 `tabId`。因此即使不知道父对话 URL，也能把子 Agent 的进度和结果路由到正确的父任务事件收件箱。

事件由本机 Bridge 在子任务开始、续聊开始和每轮终结时产生：

- `subagent_dispatched`：首次派发；
- `subagent_continued`：原子对话开始新一轮；
- `subagent_finished`：本轮收到网页回复或明确进入错误/待复核状态。

每条事件包含递增 `seq`、`parentTaskId`、`subagentId`、`jobId`、`round`、状态、真实子对话 URL、回复文本和时间。只在完成终态时才能使用结果作为开发反馈。请求去重不会重复产生相同派发事件。

### 1. 派发带父任务 ID 的子任务

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 `
  -Action start -ParentTaskId 'yanzi:quality-loop-001' -Title '验证 UI 组件' `
  -RequestKey 'quality:ui-round-001' `
  -Prompt '在专属子对话中执行验证任务，并返回测试结果。'
```

### 2. 父任务读取自己的收件箱

```powershell
# 立即返回未读事件（after=0 表示从头读取）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 `
  -Action events -ParentTaskId 'yanzi:quality-loop-001' -AfterSeq 0

# 最多等待 25 秒，一旦新事件出现便尽快返回；可按返回的 lastSeq 继续等待
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 `
  -Action wait -ParentTaskId 'yanzi:quality-loop-001' -AfterSeq 2 -WaitMs 25000

# 或直接读取本地共享快照，子 Agent 完成后会原子更新文件
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-subagent.ps1 `
  -Action file -ParentTaskId 'yanzi:quality-loop-001'
```

HTTP 接口：`GET /api/parent-tasks/<url-encoded-parentTaskId>/events?after=<seq>&waitMs=<0..25000>`，与 Bridge 一样需要本机 Bearer Token。JSON 格式包含 `events[]`、`lastSeq`、`latestSeq`、`hasMore`。`after` 为排除式游标；客户端收到并处理事件后保存最后的 `seq`，避免重复消费。事件日志在 `state.json` 中持久化，当前最多保留最近 2000 条事件；非常久没有消费的客户端应该按任务读取子 Agent 原始历史核对，不能假定超过保留上限仍无遗漏。

快照文件：`%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\chatgpt-bridge\parent-feedback\<encodeURIComponent(parentTaskId)>.json`。该文件与本机 Bridge 共享相同 Windows 用户权限，**不应包含明文密钥或发给不可信用户**。快照当前保留该父任务最近 200 条事件，不是无限历史库。

### 3. 用独立监听器代替人工盯网页

```powershell
# 等待收到一条「完成」事件，把事件逐条存盘并自动记住上次游标；最长等待 60 秒
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/watch-yanzi-subagent-events.ps1 `
  -ParentTaskId 'yanzi:quality-loop-001' -ConsumerId primary `
  -MaxCompletedEvents 1 -MaxWaitSeconds 60

# 由燕子已存在的闲置任务调度器显式启动时，可以无限监听；需要自行结束进程
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/watch-yanzi-subagent-events.ps1 `
  -ParentTaskId 'yanzi:quality-loop-001' -ConsumerId idle-worker `
  -MaxCompletedEvents 0 -MaxWaitSeconds 0
```

已消费事件保存在 `parent-feedback\received\<parent>-event-<seq>.json`，消费序号保存在 `parent-feedback\received\<parent>-<consumer>.cursor`。脚本为**至少一次处理**设计：如果事件处理和保存游标之间进程崩溃，重启可能再次读取同一个事件，调度器必须按 `eventId` 或 `seq` 去重。监听是一个按需运行的普通进程，不在此步骤增加开机自启动或新服务。

### 4. ChatGPT 父 Agent 的实际运行边界

- **父 Agent 当前正在调用工具**：可以用 `-Action wait` 阻塞等待子结果，收到事件后同一段任务继续分析、修复与重新测试，无需等待用户提示。
- **父 Agent 尚未完成，但临时断开**：外部本地调度器可监听并持久化结果；父 Agent 恢复工作后从游标继续读取。
- **父 ChatGPT 会话已经结束**：本地文件变动**不能直接唤醒模型或自动向用户发送 ChatGPT 消息**。如果要主动在父对话接着输入，仍需显式绑定 `parentOriginId`（网页 URL/对话身份），或采用支持主动通知/调度的正式入口。请勿把本地 `parentTaskId` 误认为 ChatGPT 前端对话身份。

### 真实验收与故障回放

本机创建了 `parentTaskId=yanzi:parent-feedback-smoke-01` 的真实网页子 Agent `710441c0-ccf2-4ac1-862e-c623180c88e6`，父监听器先后接到序号 1（开始）和序号 2（完成），最后正文为 `YANZI_PARENT_EVENT_OK_1`。通过命令分别读取 API、快照文件和已处理游标，均得到相同序号 2 和相同结果。单元集成测试包含长轮询收到完成事件、重复提交不重复产生事件、第二轮续聊、新版 Bridge 重启后仍可恢复父任务记录、游标非法输入拒绝；回归 **58/58** 通过。

尚未对本次原生 ChatGPT 对话做自动消息推送（缺少该对话来源绑定）；这与父事件持久化是两个不同能力。
