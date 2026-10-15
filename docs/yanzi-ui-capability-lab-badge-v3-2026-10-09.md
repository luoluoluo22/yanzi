# 能力实验室状态徽标：公共胶囊组件修复（2026-10-09）

## 问题与原因
正式环境截图里的“已就绪”使用小程序手写的 `Border + TextBlock`：
`CornerRadius=10`，没有固定外层高度，被 `Grid` 拉伸后呈现成偏方形的绿色块。
尽管小程序已经直接引用 `Yanzi.UI.Wpf`，这处状态指示在此前的视觉迁移中遗漏了公共 `YanziBadge`。

## 修复内容
- `capability-lab/CapabilityLab.cs` 使用 `YanziBadge`（公共库 `Yanzi.UI.Wpf`），替代手写 `Border` 和 `TextBlock`。
- 公共库控件模板固定高度 22 DIP、端部圆角 11 DIP、最小宽度 52 DIP、左右内边距 12 DIP，宽度随状态文字自适应。
- `HorizontalAlignment.Right`、`VerticalAlignment.Top` 避免再被父级 Grid 撑高。
- 状态显示集中到 `SetBadgeStatus(...)`，覆盖：未检查、已就绪、需准备、失败、已完成、安装中/验证中/检测中；状态颜色沿用业务原本语义值，模板统一由公共库维护。
- 未改依赖检查逻辑、实验运行逻辑或安装行为；不重启燕子宿主，只关闭再重新打开能力实验室窗口。

## 真实验证（本机正式环境）
- 上线宿主：`YanziRuntime/versions/20261009-095950-424`；PID 14876。
- 原正式文件 SHA256：`41D1CA4CF86FA0FF39F016A67CFECF6E0F3545C22274C9FDACD01259E04B7064`。
- 修复后正式文件 SHA256：`68313E64063D01558AEBD53462BC8B4D52B7B000CD79CDC27EA6D782818C6979`。
- 独立已验证的 Host Roslyn 动态编译 PASS 1/1，WPF 样式解析 PASS。
- 正式 Runtime 日志中 `ScriptRunner csharp build done: success=True`，新缓存产物为 `.yanzi-csharp-cache/d88f2fcd57989ac5/.../YanziExtension.dll`。
- 正式窗口截屏：`F:\Desktop\kaifa\OpenQuickHost\.tmp\lab-after-shared-badge.png`，从图像确认四个“已就绪”均呈横向胶囊，不再是竖向状态块。
- 正式 UI Automation 操作真实“刷新全部”按钮通过，刷新后四个“已就绪”，4/4 依赖满足；操作按钮恢复可用。
- 生产 Runtime 及其他后台小程序未重启。

## 可复现的 V2→V3 补丁
- `tools/yanzi-ui-extension-adoption/badge-v3.json`：旧新版 SHA256 固定值。
- `tools/yanzi-ui-extension-adoption/patches/capability-lab-badge-v3.patch`：只含能力实验室状态 Badge 的差分。已在临时目录用 `git apply --check` 和实际 `git apply` 复放，归一化 Windows CRLF 后与正式上线源文件完全一致。
- 先校验源文件是 manifest 的 `previousSha256`，备份后应用补丁、归一化 UTF-8 LF，并核对 `updatedSha256`，再重启对应小程序。
- 如现有源码已是 `updatedSha256` 则无需再应用；若是其他哈希，必须停止并人工合并差异，禁止覆盖未知修改。
- 完整宿主版本化升级仍走原候选校验链路；本次无需重发宿主 DLL。

## 回滚
- 原 V2 已备份：`%LOCALAPPDATA%\OpenQuickHost\Extensions\capability-lab\CapabilityLab.cs.before-shared-badge-20261009`。
- 只需关闭能力实验室窗口、将该备份恢复为 `CapabilityLab.cs`、重新打开小程序。不会影响宿主公共库或其他小程序。
