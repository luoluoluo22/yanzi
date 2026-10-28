# 燕子浏览器增强 V3：结构化快照与安全检查点

开发日期：2026-10-22。

## 新能力

1. `browser.snapshot`：独立只读能力，复用现有主 Edge 会话打开指定 http(s) 网页，获取 `semantic` 结果（URL、标题、标题层级、可交互元素的 role/name/CSS selector、开放 Shadow DOM 数量、iframe 标题及 origin）。
2. `browser.workflow` 新步骤 `{"type":"snapshot","key":"semantic","limit":60,"maxTextLength":120}`，可嵌入导航、点击之后；允许 `#host >>> button` 风格的开放 Shadow DOM 选择器。
3. `browser.workflow.status`：读取扩展持久检查点，不含表单字段值、页面 HTML、完整 URL。每个检查点保存任务 ID、Tab ID、网页 origin、已完成步骤数量、当前步骤类型、状态与时间。
4. 扩展 Service Worker 重新启动时把未完成的 `running` 任务标记成 `interrupted`，禁止重放填表、提交或点击行为，以免产生重复副作用。
5. 同一个 taskId 同时提交的工作流只允许执行一个，第二个返回 `Workflow already running`。持久记录最多保留 40 条，仅在最近 20 条范围内展示默认状态。

## 使用示例

```json
{"url":"https://www.iana.org/domains/reserved","steps":[
  {"type":"wait","selector":"h1"},
  {"type":"snapshot","key":"before","limit":40},
  {"type":"click","selector":"#nav_dom_top a[href='/domains']"},
  {"type":"waitForUrl","urlIncludes":"/domains"},
  {"type":"snapshot","key":"after","limit":40},
  {"type":"assert","selector":"h1","contains":"Domain Name Services"}
],"closeOnComplete":true,"timeoutSeconds":45}
```

## 权限和边界

- 保持原有扩展权限，未添加 `chrome.debugger` 或任意脚本执行功能。这是 DOM/开放 ShadowRoot 结构化快照，**不等同于 Ego 的 Chromium 内核级 Snapshot**。
- 不返回 `input`、`textarea`、`select` 的当前值或密码，不抓取关闭的 Shadow Root 和跨源 iframe 的正文。网页上其他可见文本仍属于读权限内容，调用者应遵守账号权限。
- 不同步任何 Cookie、密码或 Edge Profile 数据。
- 检查点不代表可以在浏览器意外中断后自动恢复所有写操作；恢复策略需要由主管 Agent 根据检查点与目标站实际状态确认。
- 浏览器扩展开发版可独立热重载；新增宿主能力需编译、更新宿主程序后生效。

## 测试

- `node scripts/test-browser-workflow-v2.cjs`：在真实 Edge Chromium 内并行运行三个多步骤任务，测试跨页、SPA、严格断言、快照 Shadow DOM、用快照生成的 CSS 选择器点击按钮、任务隔离、异常后清理以及中断处理。
- `node scripts/test-browser-workflow-checkpoints.cjs`：无浏览器依赖，测试并发写入日志、40 条上限、中断检测及无自动重放；已接入 GitHub `engineering-checks`。
- `dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Debug --no-restore`：验证宿主能力注册和参数检查。

## 后续

需要进一步实现 Chrome Debugger（需明确授权与更高权限）或受控 CDP 以提供更完整的 iframe/AX 快照；独立 Cookie Profile/Agent Space 与自动恢复可作为后续阶段。目前优先保留主 Edge 登录态与已稳定使用的燕子扩展。
