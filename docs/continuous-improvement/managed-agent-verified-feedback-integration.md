# 燕子：父子 Agent 可信验收集成记录（2026-10-09）

## 已实现

Bridge 新增 POST /api/managed-parents/:id/verified-feedback：只允许带有确认成功的子 Job 身份、父任务关联和结构化本地验证证据的请求。反馈会排入原有 managed-parent-return 队列，按父对话串行处理，重复 requestKey 去重，冲突拒绝，不直接把子 Agent 的自述当成完成证明。

控制器 tools/chatgpt-bridge/lab-iteration-runner.mjs 新增 --parent-managed-id <UUID> 模式。它先要求父对话 ready，再要求子 Agent 生成 JSON 补丁。本地受限执行器负责修改允许的实验文件、运行固定测试和回滚。然后将验证结果递交给父 Agent，父 Agent 只允许返回 accept/retry/stop。没有测试通过证据的 accept 被拒绝，最多三轮。每个 runId 保留持久锁，不允许重复提交。

## 自动化验收

Bridge 74/74 测试通过，包括独立验证失败->父 Agent retry->第二轮通过->accept、父 Agent 错误接受失败报告被拦截、子任务身份核对、原始回复隔离、幂等与字段篡改拒绝、父对话反馈串行、已有草稿时发送前保护。

## 真实网页验收

已就绪的专用测试父 Agent：6181d93a-3db0-4ff3-af20-7b2858a977e2。原对话恢复成功，按要求返回了 JSON 决策协议的初始化回复。真实子 Agent Job ed38f864-99fb-4d62-866e-60c1c9f255e8 在新标签页恢复了已有草稿，网页发送前被保护机制拒绝。源码哈希未改变，没有补丁生成，因而这一轮完整网页闭环并未通过。

本轮实验报告：.tmp/yanzi-iteration-lab/managed-real-title-20261009/results/iteration-real-managed-title-20261009.json。此前失败时遗留的父任务锁，在核对发送前阻止与 Bridge 无活动 Job 后已安全释放；runId 锁保留。

## 部署范围及剩余问题

Bridge 服务端已部署且保留备份；Edge 扩展仍为 0.5.55。改动仅限 Bridge / 实验控制器 / 测试与文档，未改正式燕子小程序、未发布新宿主。

下一步必须解决独立 Edge 测试 Profile 的正常登录与稳定持久聊天；不得清除主账号未发送草稿或复制主浏览器 Cookie。完成独立环境后，才能用真实网页再次验收持久父子 Agent 的自动修改与回传。