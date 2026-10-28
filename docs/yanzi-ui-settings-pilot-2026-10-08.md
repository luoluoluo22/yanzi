# 燕子设置界面：公共 UI 组件库接入试点

日期：2026-10-08
仓库：`F:\Desktop\kaifa\OpenQuickHost`

## 目标与范围

本轮先对**真实 SettingsWindow 的“常规”设置页**进行小范围、可回退的接入，供用户比较后决定是否推广。没有创建静态假页面，也没有重写设置模型或数据存储方式。

- 第一组设置项仍是原来的实例：主题模式 ComboBox、开机启动、自动更新、关闭到托盘、启动时刷新云状态共 **5 个**。
- 启用新样式时：通过 `YanziUi.ApplyTo` 注入窗口级、可响应深浅色主题的公共组件资源；Theme Selector 使用 `Yanzi.Select`，4 个开关使用 `Yanzi.Switch.Shadcn`，原有 10 个行和分割线被移动到真正的 `YanziCard` 的 Body 内。
- 顶部“新版样式”开关仅影响外观，对原设置的双向绑定、`SelectionChanged`、`Click` 保存事件没有修改。
- 关闭试用时：原来的控件按顺序放回 `GeneralSectionRoot`，恢复各自先前的 Style；再次开启重复使用同一控件。**不克隆表单、不重复触发保存、不修改设置值。**
- 主题切换会同步更新窗口的公共颜色资源；“跟随系统”按 Windows 应用主题注册表判断。
- 其他高级设置、快捷键、云同步、小程序、模型服务等界面保持旧版，仅作为之后的渐进迁移目标。

## 文件

- `src/OpenQuickHost/SettingsWindow.xaml`：常规页上的视觉切换入口。
- `src/OpenQuickHost/SettingsWindow.UiPilot.cs`：局部样式应用/还原、移动真实控件到 YanziCard、主题资源同步。
- `src/OpenQuickHost/SettingsWindow.xaml.cs`：初始化/主题切换挂钩。
- `src/OpenQuickHost/App.xaml.cs`：仅当 `--dev --settings-preview` 同时存在时，启动后自动打开真实设置窗口。
- `scripts/verify-yanzi-settings-ui-pilot.ps1`：UI Automation 验证开发版标题、卡片切换与五项设置不变。

## 独立开发预览

正常情况下，用户当前的 `YanziRuntime\shells` 正式实例**不会被停止或覆盖**。本次预览使用：

```powershell
dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release -p:SkipStopRunningApp=true
& "src/OpenQuickHost/bin/Release/net9.0-windows/Yanzi.exe" --dev --settings-preview
```

- 开发版实例使用独立单实例 ID 和 `%LOCALAPPDATA%\OpenQuickHost.Dev` 配置目录，并使用不同的开发 API 端口。
- 开发窗口标题为 `燕子设置 · 统一 UI 试用（开发版）`，与用户正式燕子区分。
- 不应将上述构建的开发产物直接替换正式 Runtime；完整发布需独立验证原有启动器/Runtime 生命周期和业务设置。

## 实际验收

1. `dotnet build ... -p:SkipStopRunningApp=true`：**0 错误**，仍有原有的 **14 个宿主警告**。
2. `Yanzi.UI.Verification`：**287 项共享组件测试全部通过**。
3. 实际窗口 UI Automation：`新版样式 On → Off → On`，基础设置卡片出现、撤销和恢复均正常。
4. 开发配置 `appsettings.local.json` 中的五项：`ThemeMode`、`LaunchAtStartup`、`EnableAutoUpdate`、`CloseToTray`、`RefreshCloudOnStartup` 在切换前后值均相同。
5. 已确认用户当前正式 `YanziRuntime\shells` 中的主程序进程继续运行。
6. 实机截图：`F:\Desktop\Yanzi-UI-视觉对照\settings-ui-library-pilot-v1-new-style.png`。

## 下一步

先由用户检查：导航栏、基础设置卡片、下拉和开关的位置与间距、中文字体、深浅主题切换、上下区域的视觉过渡。确认视觉方向后，再按“常规 → 模型服务 → 同步与备份 → 小程序 / 快捷键”逐页迁移。

当前仍属**第一阶段试点**，不宣称整套设置界面已经切换到公共组件库。
