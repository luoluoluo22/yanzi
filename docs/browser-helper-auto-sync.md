# 燕子浏览器助手独立更新（开发通道）

浏览器助手不再依赖宿主安装包才能跨设备更新。办公室通过 Windows 任务 `YanziBrowserHelperAutoSync` 每 15 分钟检查 GitHub `browser-helper-channel` 分支。远端完整 SHA 未变化且本地文件哈希一致时完全跳过安装。

家里 Edge/Ego 当前加载 `F:DesktopkaifaOpenQuickHostrowser-extension`（开发目录）；办公室 Edge/Ego 加载 `%LOCALAPPDATA%OpenQuickHostBrowserHelper`（稳定目录）。两者的 `manifest.version` 可以相同，部署差异要依据 SHA 与内容校验。

变更先提交 Git、通过 JavaScript 检查和业务验收，再执行 `scripts/publish-browser-helper-channel.ps1 -SourceRepo F:DesktopkaifaOpenQuickHost -Publish` 推进独立更新通道。不会自动发布未提交 WIP。远端提交必须快进，禁止强推；升级任务不修改浏览器个人数据或扩展安装路径。

同步器 `scripts/sync-browser-helper.ps1` 使用 `-InstallTask` 注册当前用户定时任务。该更新器需要 Git 和 Node.js，使用独立 Git 缓存，核对远端 SHA、脚本语法、文件摘要，保留一份回退备份，原子替换稳定目录，通过燕子本地浏览器接口请求重载。断网或校验失败保持原代码，查询 `%LOCALAPPDATA%OpenQuickHostBrowserHelperUpdaterlast-result.json`。

宿主 `BrowserHelperBootstrap.EnsureStableBundle` 在更新器标记有效且当前扩展版本不低于内置版本时不得覆盖独立通道代码；较新的内置版本仍能升级。正式应用发布和浏览器商店扩展更新属于其他通道。
