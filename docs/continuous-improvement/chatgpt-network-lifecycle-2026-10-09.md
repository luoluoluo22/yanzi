# 燕子 ChatGPT Bridge：网络状态驱动的异常诊断（2026-10-09）

## 目标

将 Edge 网络元数据接入持久 Bridge Job 与父子 Agent 迭代控制器。在网页渲染进程卡死、Bridge 超时或扩展断开时，仍保留请求时间窗内的脱敏计数。不通过非公开后端接口发送消息，也不读取或保存认证信息、完整请求 URL、消息正文。

## 功能

- Edge 扩展 v0.5.57：开始页面发送前注册只读观察器，随后在 `watch_started`、`page_dispatch_started`、`page_reply_received` 阶段上报统计。运行期间每约 1.5 秒发送一次分段网络摘要。该阶段是**本地执行进度**，`page_dispatch_started` 不等于服务端已接受提示词。
- Bridge 接收 `job_network_progress` 后，仅关联当前活动的 Job，严格白名单过滤网络统计字段，不留 URL、请求头、请求体、Cookie 和授权字段。保留最新快照、采样次数、阶段和更新时间，并持久化到原 `state.json`。任务关闭时补充 `networkDiagnosis`。
- `network-evidence.mjs` 对 Job 做保守分类：`reply_confirmed`（网页确实回复）、`draft_guard_blocked`（草稿保护）、`conversation_network_activity`（观测到对话 API 类流量但不知道是否接受当前消息）、`dispatch_outcome_unknown`（已调用页面消息执行但结果不明）、`renderer_or_load_blocked`、`unattributed_network_activity`、`no_network_evidence`。任何非成功且不确定的情况均禁止自动再次发送。
- `lab-iteration-runner.mjs` 将网络观察和诊断记录在每轮审计报告。临时模式对不确定的失败可只读检查原对话最后一组问答，并用运行关联标记、任务类型和 SHA-256 绑定；无法确认时停止，不重新提交。
- 父任务事件新增 `networkDiagnosis` 和不含敏感信息的 `networkEvidence` 精简摘要，支持父 Agent 直接查看子任务状态。

## 真实网页验收

- 部署版本：`0.5.57`。测试标签页 `399749657` 是燕子创建、独立非活跃的临时 ChatGPT 页面，预检正常且无草稿。
- 测试 Job `8db645d4-d61f-43c9-a6b8-2c1940c1d1fa`，网页回答 `YANZI_NETWORK_STAGES_OK`，状态 `success`。
- 运行中采样 8 次，最后阶段 `page_reply_received`；采到 27 个请求，5 个对话 API 类请求；分类 `reply_confirmed`，下一步 `verify_reply`。这只证实本次网页回复确实返回，以及相同执行窗口内观察到了对话类网络活动。
- 模拟断开最终回应的测试：上报 6 个请求和 `page_dispatch_started` 阶段后断开扩展连接，Bridge Job 结束为 `interrupted`，仍从持久化记录读到先前证据，分类为 `conversation_network_activity`、建议 `read_only_reconcile`，而不是 `success` 或再次发送。

## 自检命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/inspect-yanzi-chatgpt-job.ps1 -JobId 8db645d4-d61f-43c9-a6b8-2c1940c1d1fa
```

观察和诊断不能证明某条具体聊天消息已由服务端接受，也无法读取服务器内部生成进度；网络请求属于粗分类而不是已确认的专有协议。**只有固定测试通过、源码哈希核对及必要的父 Agent 接受之后**，才可以对开发任务作完成判定。浏览器输入的认证挑战和草稿冲突问题仍需独立解决。

## 下一步

基于已保存的网络生命周期数据，统计 5–10 次隔离无敏感内容对话任务，衡量成功/超时路径的阶段覆盖率。保持逐任务允许的回查与最大轮次限制，不自动重放 ChatGPT 网站未公开的私有请求。