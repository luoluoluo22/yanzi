# Cookie API · 燕子原生小程序

这是 C# / WPF 原生小程序，使用燕子公共 UI 组件。它通过燕子宿主的本机模型适配器向 AI 对话提供 ChatGPT 和 Gemini，而不是在小程序里重新实现浏览器登录。

- 本机适配器：127.0.0.1:18765，随机本地 Bearer Token，仅暴露 /v1/models 与 /v1/chat/completions。
- Cookie Suite 后台：127.0.0.1:18800，已有 Python、Node.js、Go 后台由小程序按需连接、启动；不向公网暴露。
- Ego 扩展：127.0.0.1:18888，需要当前用户安装启用 Cookie Hub 扩展。
- 登录凭证留在本机，不向燕子云同步 Cookie。模型适配器不提供 Cookie/Token 读取接口。
- 模型：chatgpt-web、gemini-3.5-flash-lite。在 AI 对话模型服务里自动注册 Cookie API (Ego)，不需要常驻小程序窗口。
- 错误语义：上游伪成功（200 但返回已知错误话术）重映射为 502，过滤 Gemini 返回的 ElicitationsGroup 网页附加标记。
- 支持 OpenAI 兼容非流式响应，以及先聚合再输出单块 SSE 的兼容模式；不承诺逐 token 流式。

**部署边界（0.1.0）**：家里电脑已有完整 Cookie Suite 后台，可在小程序内绑定目录后复用。目前没有跨设备自包含的后台发行包；在另一台全新机器上仍需先获取可信发行包并安装 Python/Node/Go 依赖。此版不执行 git clone，不含自动跨设备零依赖安装器。不能将“C# 原生 UI 已完成”误报为“全部后台原生化”。

卸载只会撤销 manifest 所属模型服务注册、停止当前燕子创建的本地适配器及后台进程；不会删除用户的独立 Cookie Suite、Ego 扩展或浏览器登录状态。

## 实机验证

在项目根目录执行：

dotnet run --project tests/Yanzi.CookieApi.Verification/Yanzi.CookieApi.Verification.csproj -c Release

测试覆盖鉴权、模型路由、禁止 Cookie 端点、Ego 连接和真实 ChatGPT/Gemini 请求。上游账号/网络失败时应返回明确的错误状态，不应伪装为成功。
