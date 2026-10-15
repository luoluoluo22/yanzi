# 燕子 MCP

独立于浣熊的日常操作 MCP。通过燕子已有 Local Agent API 自动发现用户能力，沿用真实 JSON Schema，调用小程序、应用、设备和微信文件传输助手。凭据从用户本机设置读取，端口和令牌更新自动生效，不写入插件或命令行。

能力只需在燕子中声明与注册一次。MCP 从 `/v1/agent/catalog` 合并宿主能力和已安装小程序能力，自动生成工具及参数 Schema；小程序停止时仍可发现其声明，执行与提供者启动交由燕子的 `/v1/capabilities/invoke` 处理。每次 `tools/list` 重新读取完整目录；调用缓存中不存在的工具时也重新发现一次，但绝不重试实际调用。客户端若缓存工具列表，需要重新获取列表或重新连接以加载新增工具。

## 安装与验证

在此目录执行 `npm install`、`npm run smoke`。安装到 Codex：

```powershell
codex mcp add yanzi -- node F:/Desktop/kaifa/OpenQuickHost/tools/yanzi-mcp/src/index.js
```

stdio 由模型客户端按需启动，没有常驻隐藏进程或额外自启动。燕子本身需要运行并启用 Agent API。网页版 ChatGPT 需要另行部署带认证的 HTTPS MCP Connection；安装本机 stdio 不会自动开放公网。

## 网页服务

HTTPS 地址：`https://yanzi-mcp.luoluoluo.cc.cd/mcp`。HTTP 服务仅监听 `127.0.0.1:3767`，通过现有 Cloudflare Tunnel 的独立路由提供访问，所有 MCP 调用均要求 OAuth。工作区文件和 Shell 开发能力继续使用浣熊。

执行 `powershell -NoProfile -WindowStyle Hidden -File scripts/start-http.ps1` 可启动服务；已在线时不会重启。连接密码和 OAuth 状态保存在忽略提交的 `.runtime/`，不得复制到插件包或 Git。OAuth 连接密码目前沿用本机浣熊现有连接密码，但授权状态独立保存。

已安装个人小程序“燕子 MCP”，由燕子 `on_app_launch` 在后台启动，每分钟检查本机 HTTP 服务，点击才显示管理面板。它依赖燕子本身随登录启动，不另建 Windows 计划任务，不重启共享 Cloudflare 连接器。电脑关机、注销或燕子不运行时，本机日常工具不可用。

日志：`.runtime/http.stderr.log` 记录工具调用，`.runtime/startup.log` 记录启动失败及 PID，`.runtime/supervisor.json` 记录最近检查时间。重启前会归档旧日志。日志不保存工具参数、消息正文或凭据。

网页接入分两步：在 ChatGPT 创建 OAuth MCP Connection 并完成本人授权，再将该 Connection 的真实 App ID 绑定到个人“燕子”插件。服务器健康检查通过不等于账号连接和网页工具调用已完成。

2026-10-07 已创建并授权 `Yanzi Connection`（`asdk_app_6ac59b8aef088191aa5ef3d854ce52b9`），绑定的个人插件：[燕子](https://chatgpt.com/plugins/plugins_6ac59bd449d48191b5a03f670e186813)，版本 `0.1.0`。网页插件和本机 stdio 共用能力桥接源码，OAuth 状态独立于浣熊。

[网页真实调用验收](https://chatgpt.com/c/6ac59bf6-44e0-83ea-9dbd-5f5a02738581)：`yanzi_ping`、`yanzi_extension_list`、`yanzi_wechat_status` 均成功，列出 43 个已安装小程序；微信未运行、未登录。服务审计记录了三个成功调用，未发送消息、未启动或修改小程序。验收截图保存于 `.runtime/web-test.png`。

更新个人网页插件时使用 `scripts/package-web.ps1 -AppId <已验证的 Connection App ID>` 生成仅含清单、技能和图标的归档，再更新上述已有插件，避免重复创建。归档不含服务源码、依赖目录、运行日志或连接凭据。

## 行为

- `yanzi_ping` / `yanzi_catalog` 提供连接和能力目录。
- `yanzi_extension_open` / `list` / `status` / `stop` 管理现有小程序。
- `yanzi_wechat_status` / `yanzi_wechat_fileTransfer_sendText` 使用宿主微信能力，仅支持文件传输助手。
- 其他工具自动生成 `yanzi_` 前缀，点号转下划线；每分钟刷新目录，新增能力无需改 MCP。
- 输入校验失败不调用宿主；所有写操作不自动重试；宿主失败映射成 MCP `isError`。
- stderr 审计只含工具名、时间、耗时及成功标记，不记录消息正文、令牌或工具参数。

开发验收：MCP 初始化、工具发现、燕子在线、微信状态、小程序列表均通过；临时创建无界面的验收小程序，通过 MCP 启动成功后移入燕子回收站。没有发送真实微信消息。

安装后的 MCP 会在新的 Codex 会话中加载。若当前 Codex CLI 报 `service_tier=default` 与自身版本不兼容，可仅对此命令使用 `codex -c 'service_tier="fast"' mcp ...` 覆盖；不需要修改用户的全局模型设置。
