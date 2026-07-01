# 燕子 (Yanzi) 项目 AI Agent 开发规范约束

本文件定义了所有 AI Agent（包括 Antigravity、Gemini 等助手）在参与本项目开发、维护、编译及发布时，**必须严格遵守**的行为准则与技术规约。

---

## 1. 脚本编码规范与 PowerShell 兼容性

> [!IMPORTANT]
> **PowerShell 5.1 编码限制**
> 1. 项目在 Windows PowerShell 5.1 下执行。凡是包含中文字符的 `.ps1` 脚本，在创建或修改时，**必须强制以 UTF-8 with BOM 格式保存**。禁止使用无 BOM 的 UTF-8，以防止中文字符串在运行时被解析为 GBK 造成严重的乱码与逻辑失效。
> 2. 禁止在 Windows 下使用 `&&` 或 `||` 进行命令链拼接。应使用 `;`（分号）或在不同的命令行中分段编写。

---

## 2. 版本发布与 GitHub Release 规则

> [!CAUTION]
> **发布乱码防范**
> 1. 发布新版本（运行 `upload-release-installer.ps1`）时，为规避 GitHub CLI 命令行转码和控制台字符集的乱码，**禁止**将包含汉字的临时更新说明直接写入文件并使用 `--notes-file` 传递。
> 2. 建议在打包完成后，直接调用 GitHub REST API 发送标准的 JSON 格式 `PATCH /repos/{owner}/{repo}/releases/{release_id}` 请求，通过内存中的 UTF-8 JSON Payload 更新 Release Notes 的 `body` 和 `name` 字段，以确保线上显示中文 100% 正确。
> 3. **网络与代理兜底**：在通过 `gh` 客户端或 API 访问 GitHub 时，如果环境为国内且开启了代理，需注入 `GODEBUG="http2client=0"` 环境变量强制关闭 Go 语言的 HTTP/2 Client，以解决由代理 ALPN 握手协议引起的常见 `EOF` / `connection reset` 错误。同时必须以 `-KeepProxy` 传给上传脚本。

---

## 3. Cloudflare 后端同步服务（Worker）部署与运维约束

> [!CAUTION]
> **禁止手动部署规约**
> 1. 本项目已接入 GitHub -> Cloudflare Git 自动构建部署集成。
> 2. **在任何时间、任何情况下，严禁在本地命令行中手动执行 `npx wrangler deploy` 等手动发布命令**。
> 3. 所有关于代码和配置的发布，必须且唯一依赖将代码 `git push` 推送至 GitHub `main` 分支，交由云端 CI/CD 自动完成构建与部署，以维护线上版本与代码仓库的绝对一致性。

> [!IMPORTANT]
> **Durable Object 与 Migration 冲突防范**
> 1. **现象**：部署时报错 `Durable Object namespace name '...' already in use. Please use a different name and try again. [code: 10065]`。
> 2. **原因**：`wrangler.toml` 里声明了已在云端生效过的数据库/Durable Object 的 `[[migrations]]` 迁移片段。重复执行时，Cloudflare 会因尝试重新创建已存在的同名空间而冲突中断。
> 3. **规约**：在云端初始迁移成功执行后，**后续进行业务逻辑或 API 日常热更新时，必须将 `wrangler.toml` 中的 `[[migrations]]` 部分注释或移除**，避免干扰日常自动和手动发布。

> [!WARNING]
> **机密环境变量（Secrets）防丢失规约**
> 1. **现象**：登录或接口访问报错 `Imported HMAC key length (0) must be a non-zero value...`（表示 HMAC 密钥为空，Resend 邮件 Key 缺失等）。
> 2. **原因**：本地手动运行 `npx wrangler secret put` 上传的机密变量，在触发 GitHub -> Cloudflare 自动构建（Git 集成 Auto-Build）后，会因构建环境未配置 Secrets 导致在重新 deploy 时被清空覆写。
> 3. **规约**：不允许仅依赖本地 secret 命令行上传。**必须在 Cloudflare Dashboard 控制面板上该 Worker 服务的 `Settings` -> `Variables` -> `Environment Variables` 中，手动以“加密 (Secret)”类型绑定以下三个常量**。这样每次 Git 自动构建部署后它们都将长久保持：
>    - `AUTH_TOKEN_SECRET` (JWT 签名密钥)
>    - `RESEND_API_KEY` (邮件发送 API 密钥)
>    - `RESEND_FROM_EMAIL` (邮件发送方地址)

> [!NOTE]
> **自定义域名路由映射**
> 1. **现象**：`workers.dev` 二级域名访问正常，但自定义域名 `sync.luoluoluo.cc.cd` 访问报 404 且 OPTIONS 请求无 CORS 响应头。
> 2. **原因**：本地手动运行 `wrangler deploy` 时，如果没有在配置文件中明确绑定 routes 触发器，云端就不会将流量分发至最新版本的 Worker。
> 3. **规约**：在进行 Worker 维护和迁移时，必须确保 `wrangler.toml` 包含正确的 `[[routes]]` 配置：
>    ```toml
>    [[routes]]
>    pattern = "sync.luoluoluo.cc.cd"
>    custom_domain = true
>    ```

