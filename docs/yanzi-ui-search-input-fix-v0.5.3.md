# Yanzi UI 0.5.3 — 搜索输入框裁切及 Lucide Search 图标修复

日期：2026-10-08

## 问题

- 聚焦首页搜索框后，光标难以看见，中文和英文输入上下截断。
- 右侧放大镜原为文本字符 `⌕`，随系统字体替换，和 shadcn 首页使用的 Lucide Search 线性图标不一致。
- 原多行文本框也继承了单行输入框的纵向居中约束。

## 根因

`Themes/Controls.xaml` 中 `Yanzi.Input` 为 `TextBox` 设置了 `Padding`，又在自定义模板中的 `ScrollViewer x:Name="PART_ContentHost"` 上重复绑定同一份 Padding 作为 Margin。WPF 内部文本编辑器已经应用 Padding，这层额外 Margin 继续缩小真实文字视口，使光标和中文字形产生裁切。

## 修复

1. 移除 `PART_ContentHost` 的额外 Margin，保留 WPF 本身的内边距、CaretBrush、SelectionBrush 和键盘/IME 原生行为。
2. `Yanzi.Textarea` 和 `Yanzi.Textarea.Soft` 明确采用 `VerticalContentAlignment=Top`。
3. 新增公共控件 `YanziSearchBox`，包含真正可编辑的 `TextBox`、空值占位提示层和矢量图标；占位提示和图标均不拦截鼠标点击。
4. 放大镜按 Lucide Search 绘制：24x24 视区，中心 (11,11) 半径 8 的圆形镜片及从 (16.65,16.65) 到 (21,21) 的斜向手柄，线宽 2，端点圆润。通过 WPF Viewbox 缩放至 18 DIP。
5. 首页总览改为调用公共 `YanziSearchBox("Name")`；不再使用字体字符 `⌕`。
6. 保留旧 `Yanzi.Input`、`InputSoft` 的接口和配色，不改变其他正式小程序的部署状态。

## 验收

- Gallery Release 编译：0 错误，0 警告。
- 组件验证：158/158；验证 32 DIP 可用文本视口、混合中文/拉丁文本、caret index、占位层显示状态、真正 Lucide 矢量结构和多行顶部对齐。
- Windows UI Automation：85/85；实际搜索框获得键盘焦点，接受“中文搜索 Search123”并恢复空值。
- 现场再次键入“问问 AI 搜索测试 ABC123”，焦点属性为 True，编辑框高度 38 DIP，截图确认文字未上下裁切。
- 隔离 Runtime：18/18，0 错误；宿主旧有编译警告 14 条。生产 Runtime 没有替换。
- 已更新独立 Gallery 0.5.3 和桌面快捷方式。

## 截图

`F:\Desktop\Yanzi-UI-视觉对照\yanzi-search-input-focus-0.5.3.png`

截图来自运行中的 Windows UI Gallery，包含真实的聚焦搜索文字与主题界面。
