# 燕子：独立 ChatGPT Agent 浏览器方案（2026-10-09）

## 原因

同一个 Edge Profile 中新建普通 ChatGPT 标签页会恢复别的标签页留下的草稿。单靠 tabId 或新聊天 URL 不提供站点存储隔离。系统必须保护已有草稿，禁止自动清空、覆盖或在状态不明时重复提交。Chromium 的 `--user-data-dir` 可为独立浏览器建立另一套 Cookie、历史和站点存储。

## 已实现文件

- `scripts/yanzi-chatgpt-isolated-profile.ps1`：准备独立 Profile 和最小权限测试扩展，启动第二 Bridge、只读状态检查、无头本地渲染探针；仅显式 `OpenLogin` 操作启动有界面的 Edge。
- `tools/chatgpt-bridge/isolated-service.mjs`：第二个本地 Bridge，监听 127.0.0.1:53922，数据和 Token 路径完全独立。
- `tools/chatgpt-bridge/isolated-readiness.mjs`：通过 53922 检查登录/验证/草稿/输入框状态，不发送任何聊天消息，仅操作由独立扩展创建的诊断标签页。
- `scripts/verify-yanzi-chatgpt-isolated-headless.ps1`：一次性独立 Edge 实验；不会使用系统主 Edge 的 Profile，不读取主浏览器账户信息。
- `scripts/yanzi-managed-parent.ps1`：新增 `-BridgePort 53922`，可向专用 Bridge 派发或查看父任务。
- `tools/chatgpt-bridge/lab-iteration-runner.mjs`：支持 `--bridge-url http://127.0.0.1:53922` 时直接从隔离 Bridge 的本机私有存储中读取 Token，默认桥仍为 53921。

## 工作路径

独立 Edge Profile: `%LOCALAPPDATA%\OpenQuickHost\BrowserProfiles\ChatGPT-Agent`。

从代码生成的精简扩展：`%LOCALAPPDATA%\OpenQuickHost\BrowserProfiles\ChatGPT-Agent-Extension`。

独立 Bridge 数据：`%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\chatgpt-agent-bridge`。

主 Bridge 仍为 `http://127.0.0.1:53921`，独立 Bridge 仅监听 `http://127.0.0.1:53922`。两个 WebSocket 连接和状态数据库不共享。生成扩展仅授权 ChatGPT 站点与本地独立 Bridge，正式主 Edge 扩展保持不变，未复制其 Cookie、认证头或登录状态。

## 已实际验收

1. 独立 Edge Profile 的静态本地网页 `--headless=new --user-data-dir=...` 渲染成功，主 Edge 完全不受影响。
2. 第一次无头加载精简扩展时，独立 Bridge 已实证 `connected=true`、version=0.5.57，同时主 Bridge 53921 仍在线。仅结束独立 Edge 进程后，53922 断开，主 Bridge 未断。
3. 第二套 Bridge 在无登录条件下试图创建普通 ChatGPT 页面，输入框等待超时，未发送任何对话。读到的实际 Job 错误是 `等待 ChatGPT 输入框超时`。这可能由未登录、验证页面或无头渲染限制引起，未证明确切原因。
4. 无头浏览器重复启动时观察到扩展不总能自动连到 53922；这项重启稳定性尚未通过，不能称为无人值守生产环境。任何无法确认就绪的情况必须拒绝父子 Agent 任务。
5. 普通 Bridge 回归/独立父子实验回归此前通过。本轮新改动没有触及实际生产燕子宿主和主 Edge 标签。

## 人工首次登录（只能由用户主动选择）

在电脑空闲时运行：

```powershell
cd F:\Desktop\kaifa\OpenQuickHost
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-isolated-profile.ps1 -Action StartBridge
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-isolated-profile.ps1 -Action OpenLogin
```

独立 Edge 窗口出现后，通过 ChatGPT 官方登录流程登录一次，不应复制或导出主浏览器 Cookie。可在登录之后验证：

```powershell
node tools/chatgpt-bridge/isolated-readiness.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-chatgpt-isolated-profile.ps1 -Action Status
```

只有登录/网站验证完全通过、浏览器扩展连接稳定、没有草稿，`isolated-readiness.mjs` 才能返回 `ready:true`。

## 登录后持久父子迭代示例

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/yanzi-managed-parent.ps1 -BridgePort 53922 -Action create -Title '独立验收父 Agent' -RequestKey 'isolated-parent-001' -Prompt '根据本地固定测试结果只回复 JSON accept、retry 或 stop'

node tools/chatgpt-bridge/lab-iteration-runner.mjs --task title --run isolated-title-001 --parent-managed-id <上一步获得的 UUID> --bridge-url http://127.0.0.1:53922 --max-rounds 2 --sandbox .tmp/yanzi-iteration-lab/<隔离任务副本>
```

实验运行仍只允许修改单个固定测试文件对应的源文件，遇到站点草稿或回复不确定就停止待复核，禁止覆盖输入或无依据重发。

## 待完成

专用 Profile 的正常图形界面首次登录、ChatGPT 页面输入框 ready:true、持久父子 Agent 实际创建/回传，以及重复重启后自动连接的稳定性测试。上述步骤在用户进行首次登录前不能算完成。