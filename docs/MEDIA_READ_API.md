# 燕子统一媒体只读接口 media.v1

## 能力

三站（小红书 / 抖音 / B 站）的搜索 → 详情 → 评论，统一结果结构，数据仅在本机处理，不同步账号内容到云端。Requires Yanzi host v1.1.2 and browser helper v0.5.91.

## 本机 Agent API（需现有本地 API Token）

POST http://127.0.0.1:53919/v1/media/read
Content-Type: application/json
Authorization: Bearer <燕子本地 API Token>

请求 JSON 示例：

    {"site":"bilibili","operation":"search","value":"人工智能","limit":5}

site：xiaohongshu、douyin、bilibili。
operation：search、detail、comments。
value：search 时为关键词（1～80 字）；detail / comments 时为官方 HTTPS 作品地址，B 站亦可使用 BV 号。
limit：1～20，默认 5。
profile：可选，指定已连接的 OpenCLI 浏览器配置。
本机 API 端口应读取当前燕子配置，53919 只是默认值。

浏览器助手内部调用：

    const response = await chrome.runtime.sendMessage({
      action: "yanzi_media_api",
      site: "bilibili",
      operation: "search",
      value: "人工智能",
      limit: 5
    });
    if (!response.ok) throw Error(response.error);
    const works = response.data.items;

浏览器扩展消息仅允许自身调用；本机 HTTP 接口受燕子已有 Token 认证保护。所有命令只读、平台和操作有严格白名单、目标 URL 必须是平台官方 HTTPS 地址。不接受可执行命令或发布、删除操作。

## 标准输出

搜索：{ schemaVersion, site, operation, query, source, items, returned, collectedAt }。
详情：{ schemaVersion, site, operation, source, item, collectedAt }。
评论：{ schemaVersion, site, operation, source, target, items, returned, collectedAt }。

每条作品：id、url、title、author、metrics、source。
详情附加 description、publishedAt、duration。
评论字段 id、author、text、likes、replyCount、publishedAt、source。

metrics 始终包括 plays、likes、comments、collects、shares、danmaku、coins，每项有 value、status、source：
- available：平台响应或页面实际提供了该指标。真实零允许为 0。
- not_provided：当前来源没有该字段，value 是 null，不能理解为 0。
- unverified：读取到未验证的占位数字；例如 OpenCLI 抖音搜索偶尔把播放、评论、分享默认填为 0。该数字不向下游呈现为有效值。
- failed：仅当某一个单独字段确定读取失败时使用。
网页展示近似数字（如 25.7万）额外带 display 与 precision=rounded_display，value 仅表示换算近似值。

## 平台约束

B站：搜索、官方作品详情（含播放、点赞、投币等）、评论正文。
小红书：搜索、公开笔记正文与点赞/收藏/评论数、评论正文。公开阅读量可能不提供。
抖音：OpenCLI 搜索；公开作品详情和评论在已登录浏览器页 DOM 读取，播放量不提供。无法可信分离评论作者/点赞时返回 null，不猜测。
没有登录态、被风控、API 结构变化或窗口关闭失败时，不造成功数据。

OpenCLI 调用加后台窗口、临时站点会话、任务结束释放标签参数。抖音页面使用原窗口的 inactive tab，结束自动关闭本次创建的页；已由用户打开的标签页不得擅自关闭。OpenCLI 第三方自动化窗口的最终销毁行为由 OpenCLI 自身决定，不保证它会关闭整座窗口。

## 验证

C# 白名单/归一化：
    dotnet run --project tests/OpenCliBridgeVerification -c Release
JS 浏览器 API 合约：
    node --test tests/media-api-contract.test.cjs
旧版浏览器桥接回归：
    node --test tests/opencli-extension-bridge.test.cjs

正式安装后继续检查 /v1/media/read 的 HTTP Token 认证、三站搜索→详情→评论和标签页数量。

本地 Token 与平台登录凭证不可硬编码、提交 Git 或写入日志。
