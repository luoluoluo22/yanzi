# 燕子浏览器开发规范：Ego 主体、Edge 回归

适用：2026-10-22 起的浏览器小程序与 OpenCLI 集成开发。

## 已验证环境（家里电脑 PC-202409131508）
- **主要开发浏览器：Ego Lite**，已从 Chrome 导入用户选择的数据（用户自行操作；本项目不直接处理密码或 Cookie）。
- **燕子浏览器助手**：Ego 的 unpacked extension ID 为 `hbbaogocjdkfbffpcoiaacdjioamjfef`，版本 `0.5.71`，加载自 `F:\Desktop\kaifa\OpenQuickHost\browser-extension`。
- **OpenCLI Browser Bridge**：官方 GitHub Release v2.0.1 提供的 extension `1.0.26`，ID `ggfhlodljdiecoljeakafmcehljednpd`，路径 `F:\Desktop\kaifa\ego-evaluation\opencli-ego-extension\extension`；独立于燕子弹窗自带的 OpenCLI 面板。
- **OpenCLI CLI**：已安装 v1.8.6；Browser Bridge ID `966dakug` 已命名为 `ego` 并设为默认。已实测 `opencli --profile ego browser ego-verification open https://example.com/` 成功，Ego 实际页面列表中查到新标签页。CLI `doctor` 在存在多 Browser Bridge Profile 时仍报告 ambiguity，实际操作应明确用 `--profile ego`；切勿因此认定扩展未连接。
- 主 Edge 的登录信息与扩展均保持不变，作为兼容性回归环境。

## 日常开发与回归工作流
1. 修改燕子浏览器小程序源码，遵守现有 Git 工作区隔离与主线合并规则。
2. 用 Ego 原生 `ego-browser` 运行浏览器自动化，使用 Ego Space 运行 UI/网页的真实任务；不把独立 Space 误当做独立扩展安装环境。
3. 加载、检查：`ego-browser nodejs -e "await import('file:///F:/Desktop/kaifa/OpenQuickHost/scripts/ego-extension-dev-check.mjs')"`
4. 修改代码后热重载：`ego-browser nodejs -e "await import('file:///F:/Desktop/kaifa/OpenQuickHost/scripts/ego-extension-dev-reload.mjs')"`
5. 使用 OpenCLI 的命令必须显式指定 Ego：`opencli --profile ego browser <session> <command>`。串行任务复用 session，并发 Agent 使用不同 session。
6. 单元与 Chromium 测试通过后，再做 Ego 的真实网页验收；必须验证最终 URL、DOM/语义结果，不能仅以按钮点击成功判定任务完成。
7. 最后再使用主 Edge 验收向后兼容，检查登录态、WebSocket 路由和避免影响正在使用的标签页。
8. 测试通过后提交 Git 并自动合并，确保 GitHub 主分支包含改动；避免未经合并的小程序被安装包或云同步覆盖。

## 浏览器连接的风险和改造
当前 Windows 正式安装的 Yanzi.exe 版本为 1.0.40（基于提交 3956cc8），而仓库当前主分支 csproj 标注版本 1.0.27，**不得用旧主分支构建直接替换已安装正式程序**。

旧 Yanzi Local Agent API 只有单个活动浏览器 WebSocket，新浏览器连接会主动关闭前一个连接。Ego 与 Edge 同时连接可能抢占、循环重连。改造中的新路由使用多连接注册表，保留 Edge 并在 Ego 在线时优先路由至 Ego，Ego 离线时自动回退 Edge。

Ego 的 Yanzi 扩展初始 WebSocket 地址为 `ws://127.0.0.1:53919/v1/browser/ws`，此设备当前燕子实际 API 端口为 `42980`。新版宿主部署后，需要仅在 Ego 扩展的 chrome.storage.local 中设定 `localWsUrl=ws://127.0.0.1:42980/v1/browser/ws?browser=ego`；主 Edge 保持其当前配置。**宿主升级前不要同时启用两个连接争夺旧服务**。

浏览器身份标签只是本机任务路由选项，并不构成额外的安全身份验证。仍需遵循本机 API 鉴权和既有浏览器权限策略。

## 对 Ego 的特殊限制
`Extensions.loadUnpacked` 在 Ego 中针对整个浏览器 Profile 生效，不仅当前 Task Space。不可让不同 Task Space 同时加载开发版与正式版的同一个扩展，以免重复注入。未来独立账号隔离需用不同 Browser Profile，不可依赖 Tab 或 Task Space。
