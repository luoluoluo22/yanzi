# 燕子受管理父 Agent / 子 Agent 自动回传 v1

## 为什么要创建“父 Agent 专属对话”

当用户在 ChatGPT 原生客户端或未绑定的浏览器聊天提出需求时，燕子本机浏览器扩展**不一定能获得该会话的 tabId/conversationId**。直接选择“当前活跃标签页”可能会把子任务结果发送给错误的人或错误的对话。因此，燕子创建单独的受管理父 Agent：它也是一个有 `tabId` 和持久 `https://chatgpt.com/c/...` URL 的正式 ChatGPT 对话。

当父 Agent 派发子任务时使用 `parentManagedId`。子任务完成 → Bridge 持久记录结果事件 → 生成一条发往父 Agent 原对话的 `chatgpt_subagent_continue` → 父 Agent 收到结果并输出反馈。失去原标签页时浏览器扩展仅根据**准确的对话 URL**恢复新标签；绝不会挑一个任意活动聊天顶替。

## 一、使用方式

在 `F:\Desktop\kaifa\OpenQuickHost` 根目录执行：

```powershell
# 1. 创建父 Agent，第一次回复后其状态自动变为 ready
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-managed-parent.ps1 `
  -Action create -Title "燕子质量负责人" `
  -RequestKey "quality:managed-parent-01" `
  -Prompt "你负责验收来自子 Agent 的结果，指出是否达标和下一步建议。"

# 2. 查询父 Agent 的身份 / 状态 / URL / 回传记录
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-managed-parent.ps1 `
  -Action status -ParentId <PARENT-UUID>

# 3. 派发子 Agent，绑定刚创建的父 Agent
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-managed-parent.ps1 `
  -Action child -ParentId <PARENT-UUID> -Title "OCR 回归测试" `
  -RequestKey "quality:child-ocr-01" `
  -Prompt "验证 OCR 流程并报告测试结果。"

# 4. 查询此父 Agent 关联的事件日志（不需要网页在线）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-managed-parent.ps1 `
  -Action events -ParentId <PARENT-UUID> -AfterSeq 0

# 5. 需要的话等待新事件（长轮询，最长 25 秒）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-managed-parent.ps1 `
  -Action wait -ParentId <PARENT-UUID> -AfterSeq 2 -WaitMs 25000
```

新的 API：

- `POST /api/managed-parents`：创建父 Agent，参数 `{requestKey,title?,prompt}`，重复请求按 `requestKey` 去重。
- `GET /api/managed-parents`：所有受管理父 Agent。
- `GET /api/managed-parents/<id>`：父 Agent 状态、tabId、url、最近的模型回复、`deliveries[]` 及每条回传的 `returnJobId`。
- `POST /api/subagents` 新增可选 `parentManagedId`；其 `parentTaskId` 会由对应父 Agent 自动派生为 `managed-parent:<id>`，不需要分别填两套 ID。
- `GET /api/parent-tasks/managed-parent%3A<id>/events?after=<seq>&waitMs=<ms>`：既有持久事件邮箱，可供非 ChatGPT 本地调度器消费。

以上均使用本机原有 Bearer Token 和 `X-Bridge-Request: 1` 防护。

## 二、可靠性约束

1. **严格来源身份**：父、子对话的首次成功回复必须同时校验正式 ChatGPT URL、conversationId、有效 tabId 和非临时模式。目标页面跳转、重复标签歧义或身份不符，均拒绝发送。
2. **单父任务消息串行**：最多 4 个常规作业可以并发执行，但来自不同子 Agent 的反馈进入**相同父 Agent**时会依序提交，禁止同时操作同一个输入框。
3. **只要结果最终状态不明确就拒绝盲目重试**：浏览器返回超时、断开、非预期页面或重新启动时，父 Agent 会进入 `needs_review`，已有 `returnJobId` 不会被重新创建，其他反馈保留在队列。需要检查聊天后再显式恢复；目前没有自动“检测对话是否已经收到重复内容再重试”的能力。
4. **持久记录**：管理父 Agent 及所有 Delivery Job 都写入原有 Bridge `state.json`，部署脚本重启前先备份。子任务完成事件另外写入 `parent-feedback/<parentTaskId>.json`，关闭浏览器不会丢失；后续可以按事件序号恢复。
5. **未授予额外代码执行权限**：这是一种浏览器对话调度架构。Web 上父 Agent 只会根据提示词**回复建议或分析结果**。要让它自动修改源码和自行发出新子任务，还需要在经过允许的工具环境下追加“指令解析 + 允许列表 + 最大迭代次数 + 发布门禁”的工作流执行层。不要将“父对话自动回复”描述为“AI 已经完全自主修改代码并发布”。

## 三、真实浏览器端到端验收（2026-10-09）

父 Agent：

- ID：`6181d93a-3db0-4ff3-af20-7b2858a977e2`
- URL：`https://chatgpt.com/c/6ac84abd-423c-83e9-9955-e9a9dfcb984e`
- 首次 tabId：`399749540`，父回复：`YANZI_MANAGED_PARENT_READY`。

第一轮真实子 Agent `6a0ee0d9-d175-4ba3-ac26-19be5d6f3cd5` 回复 `YANZI_MANAGED_CHILD_DONE`，Bridge 自动续聊父 Agent，父对话回复 `YANZI_MANAGED_PARENT_RECEIVED`。投递 Job `be6a83be-c3ce-4fa1-9339-36a6d2006884` 状态 success。

第二轮先显式关闭**已经验证属于燕子管理的测试父标签页**，再派发子 Agent `83f535c9-462c-4a64-8f5c-ec686e0cbaf3`。Bridge 按原 URL 重新打开父对话：tabId 更新为 `399749555`，同一 conversationId，第二个投递 Job `72a7d4c1-9bea-465d-84e3-3956e0c52462` 也变为 success；父 Agent 回复同样的 `YANZI_MANAGED_PARENT_RECEIVED`。两条 Delivery 都已持久记录。

自动化测试：Bridge 完整测试套件 63/63 通过，覆盖父 Agent 创建、身份校验、子任务自动回传、并发回传串行、重启后状态持久化、异常发送停止重试。

## 四、与主 ChatGPT 对话的关系

本机管理的“父 Agent”是专属的、由燕子创建的 ChatGPT **另外一条对话**，不是将当前这条原生 ChatGPT 对话的 ID 自动窃取或绑定。当前用户聊天可以通过本机工具查询它的状态和最后回复；想让父 Agent 的结果再主动发回**当前聊天**，仍需原对话明确的 URL 绑定或者支持由工作台/任务调度系统自动通知用户的正式入口。
