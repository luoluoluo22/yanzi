# 浏览器工作流 V2（2026-10-22）

## 目标
保留现有主 Edge 登录态和燕子浏览器小程序；把跨页面状态和任务调度从可被导航销毁的 content.js 移至扩展 service worker。V2 不是重做 Chromium，也不引入高权限 debugger。

## 设计
- `background.js` 把普通 `browser.workflow` 交给 `workflow-orchestrator.js`；带有 `fetch` 的专用蓝奏云工作流保留旧路径，避免变更认证接口安全边界。
- 每个任务通过 `chrome.tabs.create({active:false})` 获得独立 Tab；后台按任务顺序调用 `chrome.scripting.executeScript`，切换页面后仍能继续。
- `click` 识别锚点跳转，等待 Tab URL 变化；脚本上下文在导航时被销毁仍以新 URL 为准，不虚报成功。
- `waitForUrl`、`assert`、`navigate` 为新步骤；现有 `wait`、`fill`、`click`、`scroll`、`scrape` 兼容。
- 结果中返回 `_workflow.{tabId,finalUrl,completedSteps,elapsedMs}`；异常明确标注失败步骤、已完成步数和耗时，任务按调用方选项关闭测试 Tab。
- 三个 Tab 共享当前浏览器 Profile 的登录态，不是独立 Cookie 容器；独立账号隔离需不同 Profile/浏览器实例。
- 禁止任意 caller JS，`navigate` 仅接受 http/https，未增加扩展权限。

## 新步骤示例
```json
{
  "url": "https://en.wikipedia.org/wiki/Artificial_intelligence",
  "steps": [
    {"type":"wait","selector":"h1"},
    {"type":"click","selector":".sidebar-list-title a[href*='History_of_artificial_intelligence']","timeout":15000},
    {"type":"waitForUrl","urlIncludes":"History_of_artificial_intelligence"},
    {"type":"assert","urlIncludes":"History_of_artificial_intelligence","selector":"h1","contains":"History of artificial intelligence"},
    {"type":"scrape","selectors":{"title":"h1"}}
  ]
}
```

## 测试
`node scripts/test-browser-workflow-v2.cjs`（使用在 `F:\Desktop\kaifa\ego-evaluation\node_modules\playwright-core` 的 Playwright-Core）。
真正的 Microsoft Edge Chromium 页面测试：三个任务并发跨页、填写唯一任务码并校验隔离；负面断言；SPA 路由变更；显式导航；关闭标签页。测试页面只监听 127.0.0.1，不持久运行。

`dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Debug --no-restore` 完成宿主编译。新的 C# 步骤校验需要升级宿主版本后才能通过正式燕子能力接口使用。尚未把 browser.debugger、Native Messaging 或自动修复机制加入本版本。

## 部署注意
先构建宿主并验证扩展当前载入路径，再将 V2 模块同步到实际扩展目录。不要替换整个 browser-extension 文件夹：活跃工作区有 Gemini/OpenCLI 等尚未合并的改动。更新后通过 scripts/dev-browser-extension-refresh.ps1 重新加载，并使用主 Edge 在公开网页验证“实际 URL + 新标题”。

完整任务的浏览器服务超时时间由调用方 timeoutSeconds 控制。当前实现负责跨页和步骤级超时，service worker 异常终止后的持久恢复、Agent 任务队列和独立 Profile 隔离属于下一阶段。
