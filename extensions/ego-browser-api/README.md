# Ego 浏览器 API 服务 v0.3.0

本服务是燕子扩展之外的独立 Node.js HTTP 服务。燕子只负责安装依赖、启动服务与承载界面；浏览器自动化由 Ego Browser 命令执行。

## 本机接口

- 基础地址：http://127.0.0.1:43820
- GET /health：公开健康检查（仅回环监听）
- GET /api/environment：Node 与 Ego 命令环境
- POST /api/jobs：提交 ego_smoke_test 自检任务
- GET /api/sites：读取四个网站适配器及其操作目录
- POST /api/sites/{site}/actions：调用网站分类操作
- GET /api/sessions、POST /api/sessions：列出或创建浏览器会话
- GET /api/jobs：最近任务及状态
- GET /api/jobs/{id}：单项任务结果
- GET /api/logs：最近服务日志
- GET /api/openapi.json：OpenAPI 3.1 描述
- GET /docs：接口说明页面

除 /health 和工作台初始化接口外，API 使用 Bearer 令牌。令牌存放在用户本机 ExtensionStorage/ego-browser-api/api-token.txt。服务只绑定 127.0.0.1，校验 Host 和浏览器 Origin，不提供任意脚本执行接口。

## Ego 自检任务

工作台提交 ego_smoke_test 后，服务通过 ego-browser.exe nodejs 创建独立 TaskSpace，打开 https://chatgpt.com/?temporary-chat=true，读取页面标题、临时聊天标记、登录状态和输入框状态，再结束本次任务。不会填写表单、发送消息或操作用户现有标签页。

任务状态：queued、running、success、error、timeout、interrupted。结果保留最近 200 项。服务重启时，不会自动重发未完成任务，而是标记为 interrupted。

## 运行环境

扩展声明 requires node>=22。启动脚本优先使用可用的系统 Node.js 22+；否则下载固定版本 Node.js 24.22.0 Windows x64 便携包，并通过官方 SHASUMS256.txt 验证 SHA-256。服务不依赖 npm 第三方包。

## 日志与数据

运行日志、API 令牌和任务记录位于：
%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\ego-browser-api\

服务端口：43820。

## 快速调用（PowerShell）

从本机其他 Agent 或脚本调用时，先读取令牌，再提交异步任务：

    $base = 'http://127.0.0.1:43820'
    $tokenPath = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\ExtensionStorage\ego-browser-api\api-token.txt'
    $token = (Get-Content -Raw -Encoding UTF8 $tokenPath).Trim()
    $headers = @{ Authorization = 'Bearer ' + $token }
    $body = @{ operation = 'ego_smoke_test' } | ConvertTo-Json -Compress
    $job = Invoke-RestMethod -Uri ($base + '/api/jobs') -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    Invoke-RestMethod -Uri ($base + '/api/jobs/' + $job.id) -Headers $headers

完整的端到端自检脚本位于 tests\smoke-test.ps1。它会提交一次真实的只读浏览器检查并轮询结果；执行失败时返回错误，不会自动重复提交。

## 网站分类适配器（v0.3.0）

网站目录与参数定义位于 site-adapters.mjs，与 HTTP 服务入口分离。GET /api/sites 返回每个站点的操作目录；POST /api/sites/{site}/actions 将常用操作映射到 Ego Browser 任务队列。支持 ChatGPT 临时对话、提示词填入/确认发送/读取回答，以及小红书、抖音、哔哩哔哩的搜索、页面读取、可见结果提取、站内结果打开和刷新。

新建或搜索时可省略 sessionId，服务会创建该网站独立会话；后续操作传回 session.id。send_prompt 必须显式传 confirm: true。页面内容与快照保存在本地任务结果中；当前版本不自动发布笔记、视频、评论或弹幕。

## v0.4.0 网站详情提取
- 新增 site-details.mjs，通过可见 DOM、标准 meta 标签与 JSON-LD 结构化数据读取当前内容页。
- 小红书、抖音、哔哩哔哩新增 extract_details 操作，输出标题、描述、作者、时间、互动指标、标签、可见媒体 URL、结构化数据和缺失字段提示。
- 提取范围仅限当前浏览器已加载页面；不调用平台私有接口、不绕过登录或验证码。

## v0.4.1 结果质量状态
- 详情提取会返回 pageState、contentAccessible、warnings，区分内容已加载、需要验证、限流、网关错误和内容不可用。
- 工作台对无法读取真实详情的成功诊断任务显示警告，而不是只提示完成。
- 新增 tests/site-adapters-contract.mjs，覆盖适配器目录、生成脚本语法、在线 API、认证与二次确认防护。

## v0.4.2 运行时会话校验
- 非导航网站操作会先检查当前浏览器真实 URL 是否属于该网站；过期会话指向 Chrome 新标签页时会返回明确错误，不再将空结果报告为成功。
- ChatGPT read_response 增加 hasResponse 与 pageState；没有检测到助手回答时，工作台显示警告。

## v0.4.4 失效会话状态
- 运行时发现站点与实际页面不匹配时，将会话标记为 interrupted 并保留错误原因；用户可通过“打开网站”操作重新导航恢复。
