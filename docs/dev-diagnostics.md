# 开发故障诊断入口（第四阶段）

本工具只读，不更改用户的真实燕子配置、密钥或线上服务。收到错误描述、日志或截图时，先生成带证据的**排查候选计划**，再决定修改范围。它不假定原因已经找到。

## 使用

在燕子 Git 仓库根目录：

    .\scripts\dev-diagnose.ps1 -Description 'Gemini 接口返回 401 CREDENTIALS_MISSING'
    .\scripts\dev-diagnose.ps1 -Description '模型列表不能滚动' -ScreenshotNote '界面显示50个模型，但滚轮不生效' -Json
    .\scripts\dev-diagnose.ps1 -Description 'AI 一直生成中' -Log "$env:LOCALAPPDATA\OpenQuickHost\logs\ai-chat.log" -Json

说明：ScreenshotNote 接收用户/开发者已经观察并转述的截图文字，**本工具不会自行 OCR、读取图片像素或宣称截图已分析**。若用户提供原始截图，需要先由具有视觉能力的助手理解图片，再只传入必要的观察摘要。

日志可选，**必须显式提供**；只读指定本机 `%LOCALAPPDATA%\OpenQuickHost\logs\` 下的 `ai-chat.log`、`runtime.log`、`shell.log`、`dev-debug.log`、`cloud-sync-diagnostics.log`。只扫描日志末尾有限字节、已知错误指纹，不返回原始行、请求体、API Key、授权头或对话。拒绝目录外、非白名单和超大文件（16 MB）。无法推断远程电脑的当前日志，必须分别授权读取相应设备并判断时间、版本。

## 输出

- signals：401、鉴权缺失、超时、429、模型滚动、窗口位置、剪贴板等已知指纹；不能把指纹当根因。
- features：来自版本化业务索引的候选源码/测试文件，输出匹配分而不是虚构的成功率。
- recentChanges：相关文件最近提交；仅说明可回顾的变化，不意味着修改导致故障。
- qaHistory：复用有 Git SHA 与测试范围的本机 QA 记录，过期或脏工作树测试不得算当前通过，隔离测试不冒充正式客户端真机验收。
- nextChecks：按已识别信号生成的下一步检查项；可直接交给开发 Agent 执行后续只读检查。
- unresolved：始终列出仍未实证的环节，报告状态固定为 triage-candidate-not-confirmed。

此工具不持久化症状输入/截图文字/原始日志；输出重定向由调用方自行审查。不要将密钥粘贴进 Description 或 ScreenshotNote。

## 开发流程集成

1. 接到用户反馈，先运行 dev-diagnose 识别候选；与实际安装版本、设备和日志采集时间核对。
2. 使用 `scripts/dev-preflight.ps1` 将候选代码与行为验收条件、保留条件、自动测试绑定。
3. 复现问题、修复、测试、独立 UI QA，复核 Git diff，按 PR/CI 门禁合并。
4. 按授权的发布策略决定是否触发正式客户端发布；开发工具更新本身无需安装。

离线测试：`powershell.exe -NoProfile -File scripts/test-dev-diagnose.ps1`
实现：`tools/dev-navigator/diagnose.py`。
