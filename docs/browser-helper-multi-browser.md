# Yanzi Browser Helper 多浏览器发行与升级

更新日期：2026-10-28。本文件区分 **浏览器运行兼容**、**安装渠道** 与 **签名后的自动更新**，不得用“扩展能加载”代替商店已上架。

## 借鉴的做法

- [Bitwarden](https://contributing.bitwarden.com/getting-started/clients/browser/) 为 Chrome/Firefox 等分别生成构建并在对应浏览器真机测试。
- [uBlock Origin](https://github.com/gorhill/uBlock) 通过各浏览器扩展商店和 GitHub Release 分别发行；手动解压版通常不保证自动更新。
- [微软文档](https://learn.microsoft.com/en-us/microsoft-edge/extensions/) 建议为不同浏览器保持各自的商店上架条目。
- [Chrome 官方文档](https://developer.chrome.com/docs/extensions/how-to/distribute) 说明 Windows/macOS 普通用户不能通过任意自托管 CRX 实现无交互安装，除非是受管理环境的企业策略。不得通过静默写注册表、绕过授权或修改浏览器身份来替代商店分发。
- [Firefox MDN](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/background) 要求为 MV3 event page 提供 background.scripts 而非 Chromium Service Worker。

## 分发矩阵

| 浏览器 | 官方渠道 | 当前状态 | 升级机制 |
| --- | --- | --- | --- |
| Edge | [Yanzi 官方商店条目](https://microsoftedge.microsoft.com/addons/detail/yanzi-browser-helper/cflclhbkmiepfgholcobghgapgfbigne) | 0.5.62 已公开；0.5.87 于 10/28 提交审核 | 微软审核通过后由 Edge 商店自动升级 |
| Ego Lite | 燕子桌面端 → 浏览器助手 → Ego | 0.5.87 真机 CDP 检测已运行；宿主自动管理入口开发完成 | 自选安装到稳定目录后由宿主启动时检测并重载；绝不覆盖其他路径/商店扩展 |
| Chrome | Chrome Web Store（待首次上架）或开发者模式“加载已解压扩展” | 非品牌 Chromium 真机已成功打开燕子弹窗并核实 v0.5.87；品牌 Chrome 因 137+ 版本禁用命令行加载，仍需用户授权真机回归 | 商店版自动更新；解压版由用户授权重新加载 |
| Brave | Chrome Web Store（待首次上架）或解压版 | Chromium 理论兼容，未在此设备实测 | 同 Chrome |
| Vivaldi | Chrome Web Store（待首次上架）或解压版 | 宿主已增加识别与引导，待实测 | 同 Chrome |
| Opera | Opera Add-ons / Chrome Web Store（待首次上架）或解压版 | 宿主已增加识别与引导，待实测 | 商店版依赖商店自动更新 |
| Firefox | Mozilla AMO（未上架） | 构建独立事件页 ZIP，未完成 Firefox 真机和 AMO 签名 | 必须由 Mozilla 签名后采用 AMO 更新 |

## Windows 宿主一键入口

在燕子 **AI 对话 → 浏览器助手 → 安装或修复** 选择浏览器（不会默认只选 Edge）。
1. Edge：用 Edge 打开官方商店条目，浏览器原生权限提示；已有扩展打开扩展管理页。
2. Ego：用户主动点击后，通过 Ego 官方 `ego-browser` 的 `Extensions.getExtensions`、`Extensions.loadUnpacked` 安装/升级，逐一核验 ID、版本与启用状态。仅在用户主动安装并验证成功后写入托管标记；下一次宿主启动时仅对这个受托管稳定路径执行自动重载检查。
3. Chrome/Brave/Vivaldi/Opera：准备宿主稳定目录，打开对应扩展管理页，让用户主动选择“开发者模式 → 加载已解压”。不得伪装“点击一次即可自动安装”。
4. 若同一浏览器里已存在其他目录的燕子助手，不再加载第二份，避免重复注入、扩展 ID 更换及 `chrome.storage.local` 丢失。

稳定目录：`%LOCALAPPDATA%\OpenQuickHost\BrowserHelper`。Ego 托管标记放在该目录外部，避免升级时目录原子替换误删标记。浏览器包不包含桌面 API Token。

## 生成发行包与审核

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-browser-extension-release.ps1 -Target Chromium
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-browser-extension-release.ps1 -Target Firefox
```

- Chromium ZIP：提交 Edge Add-ons、Chrome Web Store 的不同商店条目。上传/发布 API 所需凭据彼此独立，不能复用 Microsoft API Key 到 Google。
- Firefox ZIP：Firefox MV3 event-page 构建，保留原 importScripts 加载顺序；当前 web-ext lint 零错误但有 15 个警告（主要为动态 HTML 写入）。审核前必须排查警告、运行 Mozilla `web-ext lint` 与真机测试，之后再使用 AMO 签名/发布。**未签名 ZIP 不是面向普通 Firefox 用户的可安装 XPI。**
- 版本更新必须先验证、后审核、再公开上架；不得在 Edge 0.5.87 审核中擅自撤回或重复提交。
- Chrome/Brave 的商店上架需要 Chrome Web Store 开发者账户、隐私披露、商店资料及发布权限。本仓库没有得到 Chrome Web Store 的发行凭据，不要将可用 ZIP 误写成已发布。
- WebSocket 是否与燕子桌面端通信，必须另行真机测试；浏览器已加载扩展并不代表通信成功。

## 自测

```powershell
node --check src\OpenQuickHost\BrowserHelper\ego-browser-helper.mjs
dotnet build src\OpenQuickHost\OpenQuickHost.csproj -p:SkipStopRunningApp=true
node scripts\test-browser-workflow-checkpoints.cjs
node scripts\test-browser-workflow-v2.cjs
```

非品牌 Chromium 隔离测试脚本 `scripts/browser-helper-smoke-chromium.py` 使用独立的临时用户配置，不读取真实浏览器 Cookies；需要 Python 和 `websocket-client`。品牌 Chrome/Edge 不能通过 --load-extension 自动装扩展，测试脚本对此明确报错；用户必须在扩展管理页手动授权。真实 UI、权限提示与 WebSocket 握手仍需独立 QA。Ego CLI 运行时有隔离沙箱，必须通过受控 JS 参数显式传递非敏感路径，不能假设其能继承 PowerShell 的环境变量。
