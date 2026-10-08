# Yanzi UI 0.6.0：Geist 字体与字号试点

日期：2026-10-08

## 用户反馈与范围

根据官网对照，燕子原先混用 Microsoft YaHei UI（窗口继承）和 Segoe UI（按钮与 Badge），首页 Pill Button 刻意指定 12 DIP / Semibold 600，网站常见展示按钮使用约 14 CSS px / Medium 500，造成字形、笔画和相对大小差异。

本轮**仅**在 UI Gallery 的“总览”和“徽标 Badge”两页试用 Geist 字体。普通业务按钮、旧版 Badge、其他小程序和正式 Runtime 不做全局替换。

## 来源与授权

官方来源： https://github.com/vercel/geist-font
字体：Geist-Regular.ttf、Geist-Medium.ttf、Geist-SemiBold.ttf（2026-10-08 官方 main 分支）
许可证：SIL Open Font License 1.1，见项目 Fonts/OFL.txt。
字体通过 WPF Resource 嵌入 Yanzi.UI.Wpf 程序集；OFL.txt 随预览应用复制发布，不要求 Windows 系统安装字体，不使用第三方 CDN 或共享用户字库。

## 公共设计令牌

- `Yanzi.Font.Geist`：pack URI 指向共享程序集内部 Geist，并使用 Microsoft YaHei UI 做中文回退。
- `Yanzi.Font.PreviewButton`：14 DIP。
- `Yanzi.Font.PreviewBadge`：12 DIP。
- `Yanzi.Badge.PreviewGeist`：新 opt-in WPF Style，Geist 12 / Medium 500。原 `Yanzi.Badge` 仍为 Segoe UI 12 / SemiBold 600。

首页 Pill、操作栏和搜索示例：Geist 14 / Medium 500；多行 Textarea 14；Badge 文本 12 / Medium 500。内容区正文默认继承此字体，原全局窗口与其他页面不受影响。

`YanziSearchBox("Name", useGeist: true)` 为首页特例；现有 `YanziSearchBox("Name")` 仍保持默认排版，中文输入光标与占位文字横坐标相同。

## 验收

- WPF 字形资源验证：Typeface.TryGetGlyphTypeface 显示实际读取 `pack://application:,,,/Yanzi.UI.Wpf;component/Fonts/geist-medium.ttf`，不是 Segoe fallback。
- 混排检查：FontFamily 声明包括 Microsoft YaHei UI；输入框可正常编辑“中文 Search ABC”，无二次 Padding。
- Public UI Verification：173 项通过，包含新 Geist Badge 与旧 Badge 兼容、搜索框字体及混排。
- Gallery 自动化：85 项通过，15 页面；首页宽度随 Geist 字形自然变化，实测 Default 104px、Secondary 110px、Outline 90px，高度 32px。
- Gallery Release build：0 错误，0 警告；随包 OFL.txt 已验证存在。
- 隔离 Runtime 回归：18 项全部通过；Runtime 构建 0 错误、原有 14 条宿主警告。
- 预览版本 0.6.0 已安装到组件评估入口和桌面快捷方式。未激活正式版 Runtime。

## 现场截图

- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-geist-typography-v0.6.0-overview.png`
- `F:\Desktop\Yanzi-UI-视觉对照\yanzi-geist-typography-v0.6.0-badge.png`

## 后续（尚未完成）

人工对照 shadcn 网站的实际浏览器字号和 line-height，在 100/125/150/200% DPI 及中文输入法下专项验收。WPF 与浏览器的字形子像素抗锯齿、字距和分数像素排列仍可能出现差异。确认试点效果后，才考虑替换整个公共库的默认字体与其他小程序。
