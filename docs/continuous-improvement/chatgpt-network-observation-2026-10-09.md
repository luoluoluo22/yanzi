# ChatGPT 网络状态观察：已登录 Edge + 燕子 Bridge（2026-10-09）

## 本轮目标和已完成范围

在燕子自有、后台且未固定的 ChatGPT 测试标签页内观察网络活动，把**网络请求元数据**随 Job 返回。当前仍沿用官方网页完成对话，不提取凭证、不复放 ChatGPT 内部接口、不代替模型 API。

浏览器扩展 v0.5.56 新增 Manifest V3 `webRequest` 权限及模块 `browser-extension/chatgpt-network-observer.js`，Bridge 新增 `chatgpt_network_probe` action，并将正常 `chatgpt_send` 任务的网络摘要一起附在 `job.data.network`。

监听器限定 `https://chatgpt.com/*`；只针对指定且由燕子持有的后台测试标签页启动采样。`onBeforeRequest`、`onCompleted`、`onErrorOccurred` 不申请任何 `extraInfoSpec`，不采集请求/响应头、Cookie、Authorization、请求和回复正文，也不持久化任何完整 URL 或 query。记录方法分组、请求类型、站内路由类别、响应码区间、计数、失败数和最长耗时，单次最多计 200 项。

注意：`conversation_api` 只是根据 `chatgpt.com/backend-api/` 路径所作的**粗分类**，不代表已辨认出确切发消息接口。`webRequest` 可观察 WebSocket 握手，但不观察 WebSocket 消息正文，也不解析 SSE 流正文。网络事件和 Job 的时间关联不等于服务端已接受特定提示词。

## 使用方式

在 `F:\Desktop\kaifa\OpenQuickHost` 目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/observe-yanzi-chatgpt-network.ps1 -TabId <燕子管理的后台测试标签页 ID> -DurationMs 4000

# 需要确认网络探测器工作时，可选择仅访问无凭证公共文件
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/observe-yanzi-chatgpt-network.ps1 -TabId <测试标签页 ID> -PublicProbe
```

`-PublicProbe` 只执行一次 `fetch('https://chatgpt.com/robots.txt', {credentials:'omit', cache:'no-store'})`，不输入消息，也不读取响应正文。仅对燕子自己管理、非活跃、非固定的标签页开放。普通任务仍遵守原有草稿保护，不能因为收到网络事件就覆盖草稿。

正常 Bridge Job 的只读验收：`GET /api/jobs/:id`，查看 `data.network`；若网页提前失败，`data.network` 可能只有已采样的部分数据。如果因为页面加载或注入失败而从未开始监听，则不会产生 `data.network`，不得把它误当成“网络没有请求”。

## 真实 Edge 端到端证据

扩展版本：`0.5.56`，Bridge 在线。由燕子创建新的非活跃测试标签页 `399749613`，`managed=true`。

首次只读探针 Job `3b825bc1-fb8a-403a-a6a5-0ed0a5f84ee7`：公共资源访问成功，采样到 1 次 GET、1 次 2xx、0 次失败，约 1.68 秒。

随后对该临时聊天发送不含敏感信息的固定验收消息（只回复 `YANZI_NETWORK_CAPTURE_OK`）。ChatGPT 真实回复正确，Bridge Job `ace43e28-3e86-4db4-91cd-9bb880f105bc` 为 `success`；同一 Job 的网络摘要采样 8.4 秒，记录 27 请求，其中 GET 22、POST 5、`conversation_api` 5、完成 26、失败 1。采样结果里没有原始正文、鉴权头和完整 URL。

自动化回归：Bridge 80/80 通过，包含网络源数据脱敏、其他标签页隔离、无权限时安全降级、观察器释放、只读 Job 操作与普通发送 Job 关联。

## 下一阶段

1. 对同一标签连续做 5–10 次无敏感固定回复任务，比较网页返回状态与网络事件的时间关系、卡死时采样如何变化。
2. 父子 Agent Job 完成判定仍以网页回应 / 原对话回读及本地固定测试验收为准；网络指标只用于诊断与辅助选择安全恢复策略。
3. 研究正式 OpenAI API 或用户自有 Chat Gateway 的调用路径。不要假定 ChatGPT 登录 Cookie 可以作为稳定、受支持的编程 API 访问凭据。
4. 若要真正不依赖网页执行，请引入支持的正式 API；不要自动重放未经公开支持的 ChatGPT 站内私有端点。