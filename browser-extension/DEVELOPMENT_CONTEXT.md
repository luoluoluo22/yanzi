# 燕子浏览器助手：最小开发背景

> 用途：供“帮帮忙”按钮与其他 AI 开发工具交接使用。这里记录可验证的代码导航和维护约束，不替代源码检查。

## 项目身份与入口

- 产品：燕子浏览器助手（Chrome/Edge Manifest V3 扩展）。
- 清单：`browser-extension/manifest.json`；Service Worker 入口为 `browser-extension/background.js`。
- 弹出窗口：`browser-extension/popup/popup.html`、`popup.js`、`popup.css`。
- 网页小程序目录：`browser-extension/webapps/catalog.json`。
- 小程序运行时：`browser-extension/webapps/runtime-background.js`，负责按目录中的站点匹配规则注入已安装小程序并处理安装状态、数据读写和同步。
- ChatGPT 网页桥接：`browser-extension/chatgpt-background.js`、`chatgpt-content.js`；Gemini 相关模块以 `gemini-*.js` 命名。
- 小程序采用 `appId` 作为稳定身份；目录项中的 `contentScript` 指向页面脚本，`syncKeys` 声明需要同步的数据键。

## 弹出窗口 UI 约定

- 分类从 `catalog.json` 的 `category` 动态生成，不要为每个分类硬编码独立页面。
- 搜索应同时覆盖名称、唯一 ID、描述、分类和能力标签。
- 卡片只展示 `summaryCapabilities` 中最多 4 个主要能力；完整能力列表通过折叠入口按需展开，避免标签增多后挤满界面。
- 每个小程序的“帮帮忙”按钮合并本文件、对应小程序的 `DEVELOPMENT_CONTEXT.md` 与运行时版本信息，生成可复制的 Markdown 交接上下文。
- 复制上下文不应包含浏览器当前页面正文、Cookie、Token、密码或其他凭证。
- 扩展弹出窗口无法直接读取本机 Git 工作树状态，因此复制文本会明确要求接手者先检查 Git，而不会伪造分支或提交信息。

## 数据与消息边界

- Popup 与 Service Worker 通过 `chrome.runtime.sendMessage` 通信。
- 常见小程序消息包括 `yanzi_webapp_market_list`、`yanzi_webapp_set_installed`、`yanzi_webapp_storage_get`、`yanzi_webapp_storage_set`、`yanzi_webapp_sync_now`。
- AI 笔记相关消息由小红书页面脚本发出，并在 `chatgpt-background.js` 中处理；修改这条链路时需同时检查消息双方，不要只改调用端。

## 开发与验证

在仓库根目录执行，按本次改动选择必要检查：

```powershell
node --check browser-extension/popup/popup.js
node --check browser-extension/background.js
node --check browser-extension/webapps/runtime-background.js
node -e "JSON.parse(require('fs').readFileSync('browser-extension/manifest.json','utf8')); JSON.parse(require('fs').readFileSync('browser-extension/webapps/catalog.json','utf8')); console.log('JSON valid')"
git diff --check
```

- 只改 Popup UI 时通常只需递增扩展版本 `manifest.json`；改动小程序页面逻辑时，再按项目发布规则更新对应的目录版本。
- 开发期扩展重载脚本：`scripts/dev-browser-extension-refresh.ps1 -WaitSeconds 25`。它依赖正在运行且已连接的燕子 Local Agent API；重载后应确认扩展重新连接，并验证实际 Popup/页面行为。
- 真实源码以当前仓库文件为准；执行任何修改前必须先检查 `git status --short --branch` 与 `git diff --stat`，不得覆盖其他工作树里的未提交改动。

## 本次开发环境记录

- 本次整理时的主工作树：`F:\\Desktop\\kaifa\\yanzi-development-integration-20261019`，分支 `main`。
- 曾观察到 Edge 加载目录：`F:\\Desktop\\kaifa\\OpenQuickHost\\browser-extension`。它位于另一个 Git 工作树；该工作树可能存在其他未提交开发内容。同步文件前必须比较差异，只同步本次确实需要的文件，禁止直接覆盖整个目录。
- 以上绝对路径仅是本次开发环境线索；其他机器或工作树应优先使用仓库相对路径并重新定位。
