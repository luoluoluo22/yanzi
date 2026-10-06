# ChatGPT 后台工作台

Node.js 22+ 小程序，复用 `browser-extension` 中的燕子浏览器助手 0.2.2。无需 OpenAI API Key，不读取或导出 ChatGPT Cookie。通过已登录网页的输入框发送消息，界面变化可能需要更新选择器。

## 启动

```powershell
cd tools/chatgpt-bridge
npm ci
./install.ps1 -Launch
```

安装脚本通过燕子当前设置的本地 Agent API 注册小程序，再把运行文件复制到 `%LOCALAPPDATA%/OpenQuickHost/Extensions/<API 分配的 ID>`。在燕子里点击“ChatGPT 后台工作台”启动隐藏 Node 进程并打开控制页面；下次燕子启动时也会启动服务。单独使用可执行 `npm start`，再访问 `http://127.0.0.1:53921`。关闭控制页面不停止服务，系统休眠期间无法执行任务。

在 Edge 的 `edge://extensions` 或 Chrome 的 `chrome://extensions` 重新加载现有“燕子浏览器助手”。如果安装的不是本仓库 `browser-extension` 目录，应更新该安装目录或手动加载本目录。小程序使用独立的 53921 通道，不要求燕子旧通道连接成功。扩展弹窗可配置燕子 API 端口。

浏览器助手弹窗提供“连接与服务 / API 接入 / 执行日志”。在“API 接入”选择调用场景和示例格式，可以复制 HTTP 请求、PowerShell、Python 或 Node.js 完整代码，以及完整接入说明和令牌文件路径。脚本示例包含提交、轮询、错误处理和 UTF-8 编码；查看与复制本身不会发送任务。Windows PowerShell 5.1 保存示例到 `.ps1` 时使用 UTF-8 BOM 编码。

“打开完整 API 接入文档”会在浏览器独立标签页显示同一份指南，可展开鉴权、接口表、参数与结果说明。扩展不读取或显示真实令牌，调用脚本从本机文件读取。示例包含临时聊天、继续聊天、标签页查询/清理、计划与事件；带 `{id}` 的路径和示例 `tabId:123` 需要替换。

## HTTP API

仅监听 `127.0.0.1:53921`。外部脚本从 `%LOCALAPPDATA%/OpenQuickHost/ExtensionStorage/chatgpt-bridge/api-token.txt` 读取凭证，添加 `Authorization: Bearer <token>`，写入请求还需 `X-Bridge-Request: 1`。网页用 HttpOnly 同站 Cookie；凭证不放到网页脚本。

- `GET /health`：服务与扩展连接状态。
- `POST /api/jobs`：创建任务，202 返回任务 ID；`GET /api/jobs/{id}` 查询结果。
- `GET /api/jobs`：任务列表。
- `POST /api/schedules`：保存计划；`GET /api/schedules` 查询计划。
- `PATCH /api/schedules/{id}`：`{"enabled":false}` 暂停，`true` 启用。
- `POST /api/events`：`{"name":"file.changed"}` 触发同名计划。文件监听、其他小程序或业务事件通过此接口接入；第一版不内置文件监控。

任务示例：

```json
{"action":"chatgpt_send","prompt":"只回复：后台测试成功","newChat":true,"timeoutSeconds":180}
```

## 临时聊天（默认）

浏览器助手 0.2.2 默认 `temporary:true`，新建或复用页时进入 `https://chatgpt.com/?temporary-chat=true`。发送前必须确认网页的临时聊天标题、已启用控件，或当前页面的 `Save chat` 控件与临时模式参数。URL 参数本身不算确认依据；无法确认时拒绝发送，不降级到正式聊天。回复完成时再次确认模式。

```json
{"action":"chatgpt_send","prompt":"用 JSON 代码块返回结果","temporary":true,"closeAfter":true}
```

返回 `temporary`、`temporaryEvidence`，标明页面模式与检测依据。当前网页首次发送后隐藏临时标题，改为显示 `Save chat`，实现不会点击该按钮将临时聊天保存。临时聊天也可能有 `conversationId`，不能用 ID 是否为空判断是否为临时模式。

用 `tabId` 可在仍打开的临时页面继续对话；从新聊天开始则清空上一临时会话上下文。关闭页面后不承诺恢复临时对话。若传入正式聊天的 `tabId`，默认临时请求会停止；要继续正式聊天，明确传 `temporary:false`，或在控制页面取消“临时聊天”。不自动修改用户现有正式会话。

未指定模式的旧计划也由浏览器助手按临时模式执行。小程序仍按原有策略保存最近 200 个任务结果，临时聊天模式不代表本机任务结果也不保存。

真实测试：`node tools/chatgpt-bridge/temporary-live-test.mjs`，验证临时页、JSON、继续上下文、重置为空及关闭。结果见 [临时聊天实测记录](../../docs/chatgpt-bridge-temporary-verification-2026-10-03.md)。

操作包括 `chatgpt_status`、`chatgpt_new_chat`、`chatgpt_send`、`chatgpt_messages`、`chatgpt_close`、`chatgpt_cleanup`。新聊天返回 `tabId`；发送后返回 `tabId`、`conversationId`、`text`、`messageId`、`visibility`。指定 `tabId` 会继续该聊天；未指定时默认复用空闲工作台后台标签页并从新聊天开始，没有可复用页面才创建 inactive 标签页。读取消息必须指定标签页，或者浏览器中只有一个 ChatGPT 页面。新建空聊天尚无 conversationId，首次发送后才产生。

## 标签页生命周期（浏览器助手 0.2.1）

- `chatgpt_status` 返回 `exists`，每个页面带 `managed`、`canClose`，可判定当前是否有 ChatGPT 页面及其归属。`canClose` 是归属和前后台状态的初步判断，实际关闭前还检查草稿及生成状态。
- 未指定 `tabId` 时，`tabPolicy: "reuse"` 为默认值，优先复用工作台创建的空闲后台页；新聊天会导航到首页，避免沿用上一任务上下文。`tabPolicy: "new"` 则另建标签页。草稿、前台、固定、未加载完成或生成中的页不用于重置。
- 复用已有用户聊天请指定 `tabId`。也可用 `newChat:false`，此时仅在现有 ChatGPT 标签页恰好一个时继续，多个页面须明确选择。自动复用不会取得用户页面的所有权。
- `closeAfter:true` 在任务成功并收集结果后关闭空闲工作台后台页；失败或不满足关闭条件则保留。结果包含 `lifecycle` 中的 `created`、`reused`、`managed`、`closed`、`closeSkipped`。关闭后原 `tabId` 不再可用于后续读取，结果已存入服务任务记录，聊天历史仍在 ChatGPT 账户中。
- `{"action":"chatgpt_close","tabId":123}` 关闭指定工作台页；`{"action":"chatgpt_cleanup"}` 清理空闲工作台页。返回 `closedTabIds` 和 `skippedTabIds`，不会关闭用户原有页面、草稿或正在使用的页面。

归属使用 `chrome.storage.session` 保存，扩展 service worker 被挂起后仍可恢复；浏览器或扩展重启后旧页面保守视为用户页面。记录的 URL 与当前页面不符时撤销归属，避免关闭人工导航后的页面。旧版本创建的测试页没有归属记录，需手动清理。连续后台新任务通常共用一个页；如果现有工作台页均被用户占用，会另建一个页。一次性和定时任务可开启 `closeAfter` 完全释放标签页。

```json
{"action":"chatgpt_send","prompt":"用 JSON 代码块给出结果","tabPolicy":"reuse","closeAfter":true}
```

生命周期自动化检查包含连续复用、明确继续已有聊天、成功关闭、失败保留、用户页面保护、草稿/前台/固定保护和失效记录清理。真实浏览器复测命令：`node tools/chatgpt-bridge/lifecycle-live-test.mjs`；会发出两个短提示词，复用新建的测试页后关闭，并确认原有页面仍存在。

发送结果和读取的每条消息还包含以下内容字段：

- `text`：去掉界面按钮、语言标签和隐藏提示后的正文。正文首尾空白会裁剪。
- `codeBlocks`：按网页顺序返回 `{language, text}`；此处 `text` 直接读取代码节点，保留缩进、空行、反斜杠和末尾换行，机器处理代码应使用此字段。
- `markdown`：从网页 DOM 重建标题、列表、表格、链接和代码围栏；`markdownSource` 为 `reconstructed-from-dom`，不承诺与模型原始 Markdown 字节一致。
- `json` / `jsonError`：只有一个 JSON 代码块时解析其原文；没有代码块且正文以 `{` 或 `[` 开始时尝试解析正文。失败返回错误，不自动修补。

建议结构化输出明确要求使用 JSON 代码块。普通段落中的 JSON 转义字符可能已被网页 Markdown 渲染改变，仅凭 DOM 无法恢复原始生成文本。多个 JSON 代码块可分别解析 `codeBlocks`。

测试比对可提交 `{"action":"chatgpt_messages","tabId":123,"includePageSnapshot":true}`，额外获得 `pageSnapshots` 中的网页 HTML、显示文本和代码节点原文。默认读取不返回 HTML。

计划示例：

```json
{"at":"2026-10-04T09:00:00+08:00","task":{"action":"chatgpt_send","prompt":"生成今天的工作计划"}}
```

`at`、`intervalSeconds`（至少 60 秒）、`event` 三选一。命名事件不把事件 payload 或外部文本拼接到提示词。

状态为 `queued`、`running`、`success`、`error`、`timeout`、`interrupted`。待执行任务上限 100，保存最近 200 个完成记录。扩展离线时排队，连接后串行执行。断线、超时或重启时，已开始请求不自动重发；查询聊天后再决定是否重试。系统恢复时循环计划合并错过的次数，一次性计划会补执行一次。

## 验证

```powershell
npm test
```

测试包含真实 HTTP/WebSocket 队列、鉴权、计划、重启恢复以及模拟 DOM 下的发送和完成判断。模拟测试不能替代真实 ChatGPT 网页验证。人工实测时先查询标签页，新建聊天后把浏览器留在另一个标签页，发送短测试提示词，核对返回内容和 `visibility: hidden`，再验证继续聊天和定时/事件任务。

2026-10-03 已在用户 Edge + 浏览器助手 0.2.0 上完成实测：新建后台聊天、发送并接收 `YANZI_FINAL_OK`、同一聊天继续发送、读取消息、命名事件触发和一次性定时任务均成功，返回 `visibility: hidden`。自动化测试共 23 项通过。详情见 [实测记录](../../docs/chatgpt-bridge-live-verification-2026-10-03.md)。另完成普通 JSON、JSON 代码块、Python、多代码块、Markdown 表格与列表、80 行长文本六类实测，见 [结构化内容核对记录](../../docs/chatgpt-bridge-structured-verification-2026-10-03.md)。

在仓库根目录运行下列命令可复测（第一条会实际发送六个提示词并保留测试聊天；可加参数 `json-fenced,python` 只测试指定样例）：

```powershell
node tools/chatgpt-bridge/live-test.mjs
node tools/chatgpt-bridge/verify-live-artifacts.mjs .artifacts/chatgpt-structured-test
```

第一步保存 API 返回及网页快照，完成后等待四秒再次读取，检查迟到内容。第二步独立解析保存的 HTML，核对代码哈希、语法、段落、表格、链接，并用当前提取器回放快照。语法检查不会执行生成代码。

当前网页适配覆盖无 ID 的 ProseMirror `main [contenteditable="true"][role="textbox"]`、中文“发送”按钮、`data-chatgpt-search-unit-key` 消息结构及回复操作控件。页面会在不同标签页恢复同一草稿：仅当草稿与请求提示词完全相同时允许提交，不覆盖其他内容。重复标签关闭扩展可能关闭新建页面，使用时须排除 ChatGPT 或暂停该扩展。

明确指定的后台标签页被浏览器丢弃时会 reload；自动选择复用时跳过已丢弃页面。冻结、休眠、登录失效、人工验证、使用额度限制均可能使任务失败。默认保留工作台页用于下次复用，可选择成功后关闭。服务不改变标签页 active 状态，也不绕过人工验证。

Chrome 官方的 MV3 WebSocket 保活说明：https://developer.chrome.com/docs/extensions/how-to/web-platform/websockets 。现有反向接口项目 `acheong08/ChatGPT` 已于 2023-08-10 归档（https://github.com/acheong08/ChatGPT），本实现未引入其旧协议。
