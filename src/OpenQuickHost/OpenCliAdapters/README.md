# OpenCLI 个人数据读取与完整性验收（2026-10-20）

## 结构与调用路径

燕子浏览器扩展的「个人数据读取」面板 → `opencli-background.js` 的只读操作白名单 → 宿主 `LocalAgentApiServer.OpenCli.cs` → 本地 OpenCLI（通过已登录 Edge 的 Browser Bridge）→ JSON 返回面板。

只支持显式触发的只读查询。凭证、Cookie、登录令牌不被写入仓库或同步云端。浏览器 WebSocket 必须与本机配对的扩展 ID 一致。用户只可选择平台、固定操作与抖音作品页码（1～20）；不能输入任意 CLI 命令、URL 或 JavaScript。

## 适配器

- 小红书：`creator-notes --limit 20`，无分页。真实读取返回 6 篇，9 个字段均有值，ID 唯一，前 5 篇与 --limit 5 一致。返回不足 20 篇不代表 20 篇读取失败。
- 抖音：OpenCLI v1.8.6 内置 `videos --page` 使用 `page_num`，但创作者接口忽略该参数。直接翻页会重复返回首批 6 条，**不要再用该命令作为分页依据**。
- 真实 API 端点 `/janus/douyin/creator/pc/work_list` 提供 `max_cursor` 与 `has_more`。使用 `OpenCliAdapters/douyin-cursor-reader.mjs` 在已登录浏览器内以 `max_cursor` 查询；按 `aweme_id` 去重、对游标停滞报错、按每页 20 条输出。该脚本仅由宿主的固定白名单调用，不开放任意命令。
- 2026-10-20 测试：抖音 API 报告总数 62 条，四页返回 20+20+20+2，所有作品 ID 唯一、跨页无重叠、末页 `has_more=false`；部分作品本身没有标题描述。API 返回顺序并非严格按时间逆序，因此 UI 不宣称“最新 20 条”。

## 验证与回退

`node tests/browser-opencli-contract.test.mjs`（请求白名单、页码边界、扩展身份、断线、响应回传）；`dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --no-restore`；验证发布包中 `OpenCliAdapters/douyin-cursor-reader.mjs` 确实存在，然后通过本地 `/v1/browser/execute` 实际读取。

当前 OpenCLI 安装前置条件：本机 npm 包 `@jackwener/opencli`、Node.js、Edge 的 OpenCLI 浏览器扩展已连接、目标平台浏览器处于登录状态。重启电脑后若 CLI 守护进程在线但显示 `Extension: disconnected`，先启动 Edge，必要时执行 `opencli daemon restart`，不可将此种情况误判为空数据。

若后续 OpenCLI 自身修复游标分页，可在完整验证后改回官方适配器；不要删除重复检测或读取限额。
