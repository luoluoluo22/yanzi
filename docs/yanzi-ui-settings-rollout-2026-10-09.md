# 燕子设置中心：全页面公共组件视觉统一与卡片收口

日期：2026-10-09
仓库：F:\Desktop\kaifa\OpenQuickHost

## 用户反馈与目标

用户指出基础设置卡片最后一项下方额外分割线距离卡片底部还有一段空隙，要求让圆角卡片的底边成为最终收口；并同意将统一设计组件推广到设置中心全部页面。

## 公共组件改造

- 新增 **YanziSettingsSectionCard**（`src/Yanzi.UI.Wpf/YanziSettingsSectionCard.cs`），以实际 `YanziCard` 为主体。调用 `AddRow(row, dividerBefore)` 时仅在相邻行之间插入分割线，最后一行不会生成额外分割线。
- `YanziCard` 新增 `BodyPadding`，设置卡片使用底部 4 DIP 的紧凑内容区，原有 Card 调用保持原默认值。
- `SettingsWindow.UiPilot.cs` 不再把末项多余的 Rectangle 搬入新卡片，但关闭新版时会把所有 **5 行 + 5 个原始分割线**按照原顺序放回页面，保证可逆。
- 在设置窗口资源中增加 `Yanzi.Settings.Surface`，继承既有 `SurfaceCard` 并按设计库规范指定 14 DIP 圆角、语义色卡片背景、边框色。现有复杂业务结构无需复制控件或重新实现。
- 逐页采用 **原控件实例 + 原事件/绑定 + 视觉 Style 替换**：普通 ComboBox → Yanzi.Select、PasswordBox → Yanzi.Password、单行输入 TextBox → Yanzi.Input、旧 RaycastSwitchStyle → Yanzi.Switch.Shadcn，以及原 SurfaceCard → Yanzi.Settings.Surface。
- 小尺寸、专用按钮、复杂列表选择框、只读或多行特殊输入保持原业务样式，避免误改操作语义。开关“新版样式”是非持久化比较开关，切回原版时依次还原所有先前的 Style。
- 使用 `IsVisibleChanged` 补充懒加载页面的样式处理；不批量写入用户配置，也不改变原有设置保存与刷新逻辑。

## 页面范围

已纳入公共 UI 统一适配的 **12 个真实设置区域**：常规、模型服务、环境变量、同步与备份、小程序、鼠标触发、鼠标手势、径环、燕选、燕幕、燕窝、关于。

模型服务、同步与备份、小程序等大页面优先统一可安全复用的卡片与基础控件；拖放、录制、设备状态及其他专用控件维持其原本的操作实现。**“纳入统一主题”不等同于所有复杂控件已逐像素重写或全部完成视觉签收**。

## 验证结果

- Release 构建 `src/OpenQuickHost/OpenQuickHost.csproj`：**0 错误**，历史宿主源码仍有 **14 条警告**。
- 设计组件测试：**289/289 通过**，新增验证三行卡片仅有两条分割线且最后一个元素为真实设置行。
- `verify-yanzi-settings-all-pages.ps1`：实际 Windows UI Automation **12/12 页面导航通过**，每页有可见真实内容。
- `verify-yanzi-settings-ui-pilot.ps1`：**新版 On → Off → On** 可逆；预览前后五项设置值没有变化。
- Runtime 独立兼容测试：**18/18** 通过，不替换正式燕子主程序。
- 开发预览以 `--dev --settings-preview` 打开真正的 SettingsWindow，配置目录独立于正式实例。

## 留存截图

在用户 Windows 桌面文件夹 `F:\Desktop\Yanzi-UI-视觉对照`：

- `settings-unified-all-pages-general-final.png`：常规设置新卡片底边收口，最后一条内部冗余分割线已消失。
- `settings-unified-sync-final.png`：同步与备份页，主题卡片与实际输入控件。
- `settings-unified-ai-final.png`：模型服务页，公共输入、选择与主题控件。

## 部署与回退

这轮只在独立开发版中验证 **真实设置页面**；正式运行中的 `YanziRuntime\shells` 未替换或停止。试用开关本地只作用于视觉，关闭即可恢复老样式。未推送远程仓库。

下一阶段如需覆盖某些专有组件的复杂状态，应逐个测试交互、键盘与 DPI，并在同窗口大小下留截图；不应仅凭“通过页面导航”直接认定与 shadcn/ui 像素一致。
