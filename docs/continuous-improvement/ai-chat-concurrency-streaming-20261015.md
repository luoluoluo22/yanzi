# 燕子 AI 对话并发与流式回归基线（2026-10-15）

## 症状和根因

- 旧版在 \`MainWindow.AiChat.cs\` 用窗口级 \`IsAiChatRequestInFlight\` 拦截全部会话，A 正在回复时 B 无法提交，并在 await 后继续引用当前选中话题，存在串话风险。
- ChatGPT Plan 使用 SSE，其他第三方模型走非流式，遇到长生成容易被第三方网关固定超时拦截。HTTP 504 的根因仍需第三方排查；客户端不能把 504 误认为可安全重试的失败。
- 2026-10-10 的剪贴板保护提交 \`ea4fc48\` / \`d8c7310\` 留在其他分支，并非 \`main\` 祖先。v1.0.22 没有包含 \`IsAiChatMode\` 的防自动隐藏判断。

## 新的客户端不变量

1. \`AiChatRequestTracker\` 按话题 ID 管理“准备中/运行中”，允许不同话题并发，单话题仅允许一条未完成请求；按话题取消。
2. 请求启动时固定话题、附件、模型、供应商与消息上下文。所有响应写回原话题，不依赖之后的选中项；隐藏话题完成后不能改变当前输入焦点。
3. Chat 和 Agent 的模型网络请求都优先使用 \`stream=true\`。Chat 逐步显示原始文本，完成后才排版 Markdown；Agent 解析完整的工具调用后才执行工具。仅当 HTTP 400/422 **明确**声明 stream 不受支持时退回一次普通调用，不对 5xx/504 自动重试。
4. 剪贴板等独立小程序弹出时，AI 会话窗口保持可见；不能因失焦触发主面板自动隐藏。
5. 开发验收使用 \`--dev\` 独立配置和实例，不改正式用户进程、配置、令牌或小程序数据。

## 验证命令

\`\`\`powershell
dotnet build OpenQuickHost.sln -c Debug -p:SkipStopRunningApp=true
dotnet build src/Yanzi.SyncVerification -c Release -p:SkipStopRunningApp=true
dotnet run --project src/Yanzi.SyncVerification -c Release --no-build -- --ai-chat-parallel
dotnet run --project src/Yanzi.SyncVerification -c Release --no-build -- --ai-chat-sse
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-ai-chat-regression.ps1
\`\`\`

生产环境第三方 SSE 兼容性需在持有合法供应商凭证的设备上完成真实请求验收；编译及模拟 SSE 成功不代表第三方站点通过。

