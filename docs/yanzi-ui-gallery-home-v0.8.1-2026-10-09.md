# 燕子 UI v0.8.1 — 公共组件首页全量预览验收

日期：2026-10-09

## 发布范围
- 独立组件评估中心（Gallery）本机版本升级至 v0.8.1，未重启正式燕子 Runtime。
- 全部首页组件直接遍历公共库 `YanziComponentRegistry.Components`，使用和独立详情页相同的 `TryRenderIsolatedPreview` 预览工厂创建真实 WPF 控件实例。不得单独维护第二份组件标题或静态截图。
- 注册 64 项、首页 64 张组件卡片，每项均有可点的标题和对应可视预览。搜索支持按名称/分组/API 筛选。
- 点击标题进入组件详情（真实样例、官网对照、API、核对记录），点击“返回全部组件”恢复搜索条件、组件列表与合理的原滚动位置。
- 首页响应式 2/3/4 列，保留旧业务场景演示区在下方；缩略图是当前 DLL 内真实控件的 WPF Viewbox 实例，公共库变化后需要重新构建并重新安装 Gallery 才能在**新进程**中生效，旧进程不会热重载已加载的 DLL。
- 清理冗余 Card 备用预览代码（Card 本身通过 `CardShowcase` 调用真实 `YanziCard`）；Direction 更改为 `YanziContentPrimitives.Direction`；Field 和 Label 使用 `YanziPrimitives.Field`，杜绝名称覆盖但展示伪组件的情况。
- `verify-yanzi-ui-gallery.ps1` 适配组件墙变长后的 WPF 弹出菜单时序：触发控件聚焦并最多等待 2 秒验证菜单弹层，而不是固定延迟假定它位于最初视口。

## 真实验收
- Release 构建：0 errors, 0 warnings。
- 组件库 `Yanzi.UI.Verification`：289 项设计系统断言通过。
- `scripts/verify-yanzi-ui-home-wall.ps1`：全部 64 个首页标题存在，搜索 Badge 64→1，Badge 和 Typography 进入详情及返回、搜索状态恢复；真实 Card / Field / Direction 预览可见；可见组件点击返回后，滚动百分比 65%→65%。
- `scripts/verify-yanzi-ui-gallery.ps1`：15 个页面、104 项 UI Automation 断言通过。
- `scripts/verify-yanzi-ui-official-catalog.ps1`：官方 64 组件详情页逐项打开，0 错误。
- 正式 v0.8.1 进程对真实窗口 `PrintWindow` 获取截图成功（1180×820），Windows UI Automation 检查 64/64 首页组件按钮，非网页原型。
- v0.8.1 首页截图：`F:\Desktop\kaifa\OpenQuickHost\.tmp\ui-gallery-v081-final.png`。

## 安装及回滚
- 已激活：`%LOCALAPPDATA%\OpenQuickHost\Tools\YanziUiGallery\preview-20261009-112741-477\Yanzi.UI.Gallery.exe`。
- `%LOCALAPPDATA%\OpenQuickHost\Extensions\yanzi-ui-gallery\manifest.json` 中 version = 0.8.1，openTarget 对应上述版本。
- 桌面快捷方式 `燕子 UI 组件评估中心.lnk` 的 TargetPath 与 manifest 一致。
- v0.8.0 `preview-20261009-103458-876` 及先前预览快照均保留；需要回滚时只切换入口指向旧 exe，必要时恢复先前源码后独立重建。
- 没有提交其他工作流的仓库修改，没有发布公开安装包，也没有重启正式燕子 Runtime。

## 维护注意
- 加组件：先把公共 API 放在 Yanzi.UI.Wpf 中并登记 `YanziComponentRegistry`，然后为详情与首页**共用**的 `TryRenderIsolatedPreview` 增加真实 WPF 实例，主页便会自动出现对应新组件。
- 对 UI/模板改动：需重新构建/安装 Gallery 并重启 Gallery 进程，避免旧 CLR 加载的 DLL 缓存造成“首页没更新”错觉。
- 首页以滚动方式显示全部 64 项，并非要求同时把 64 项压在一个物理屏幕里。如果未来扩至几百项，考虑可见区域虚拟化。
