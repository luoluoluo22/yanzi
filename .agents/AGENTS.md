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
