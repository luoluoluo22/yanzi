# Yanzi UI · 官方组件源码参考包工作规范

## 原理

此前主要从官网页面和截图人工还原 WPF 控件。现在改成：**官方源码 → 可版本化的组件规范 → 原生 WPF 公共库 → 实机视觉/交互验收**。源码包作为上游参考，不是能够在 WPF 里直接执行的 React 库。

官方来源：[GitHub](https://github.com/shadcn-ui/ui)、[Base UI 目录](https://ui.shadcn.com/docs/components)、[Registry](https://ui.shadcn.com/docs/registry)、[主题](https://ui.shadcn.com/docs/theming)。

## 下载内容

本地：`docs/yanzi-ui-official-catalog/reference-package/`

- `manifest.json`：64 个组件与 WPF API 的映射；源 URL、上游 Git SHA、获取日期及每个下载文件的 SHA256。
- `registry/<name>.json`：官网 Base UI 的 `base-nova` 组件 JSON 与 TSX 内容，共 **61** 个。
- `examples/<name>-example.tsx`：从 GitHub 固定 commit 获取的示例，共 **60** 个。
- `theme/globals.css` 与 `theme/components.json`：主题及项目配置。
- `theme/design-tokens.json`：自动提取的浅色、深色及主题映射变量。
- `LICENSE.md`：官方 MIT 许可证。

特殊项目：官网有 64 个条目，但 Data Table、Date Picker、Typography 没有对应的独立 base-nova Registry 包（按组合能力处理）；Direction 没有对应的单文件 example.tsx。缺失项在 manifest 里明确记录，**不能虚构已下载 64 个独立组件包**。

## 刷新和验收

在仓库根目录：

```powershell
# 已有参考包时只校验、不覆盖
python scripts/sync-shadcn-reference.py

# 不联网校验 125 个文件的 SHA256 与 64 项目录一致性
python scripts/sync-shadcn-reference.py --verify

# 明确允许刷新；先检查现有包完整性，然后从官方重新抓取
python scripts/sync-shadcn-reference.py --update
```

刷新流程先写临时目录，下载与校验通过才替换旧包。GitHub 示例/CSS 锁定 commit；官网 Registry 是当时实时获取的 JSON，本地通过 SHA256 锁定。两者的部署版本可能存在短暂差异，应结合 manifest 核对。

## WPF 实施准则

| 源码字段 | 设计意义 | 燕子实现 |
|---|---|---|
| `:root`、`.dark` 的 `--background`、`--foreground`、`--border` | 语义颜色 | `src/Yanzi.UI.Wpf/Themes/Tokens.{Light,Dark}.xaml` |
| `--radius` 及 `--radius-sm/md/lg` | 圆角等级 | 统一 `Yanzi.Radius.*`；避免每个控件任意写数值 |
| `font-*`、`text-*`、`leading-*` | 字号、字重、行高 | `Yanzi.Font.*`、WPF DPI、CJK 回退及文字基线 |
| `px-*`、`py-*`、`gap-*`、`size-*` | 间距和尺寸 | WPF Padding、Margin、Width/Height |
| `data-state`、`hover`、`focus-visible`、`disabled` | 状态/交互 | WPF Trigger、VisualState、键盘与焦点语义 |
| 官方 example.tsx | 代表性示例结构和初始状态 | `GalleryShadcnShowcases.cs` 和独立预览页面 |
| npm dependencies、registryDependencies | 上游实现依赖 | WPF 行为映射，不能直接加载 React 组件 |

每项开发必须先读 Registry JSON 中的具体源代码和示例，确定同一套 style、主题、字体、DPI 后再实现控件。需要改变公共视觉规范时优先修改 Yanzi.UI.Wpf，不能只改 Gallery 伪装完成。最后仍须用实机截图逐状态核对；自动化通过与源码下载成功**都不等于逐像素验收通过**。

注意：本次选择 `base-nova` 作为可复现的首个参考样式，但这不自动证明用户过去截图当时官网使用的配色、基础圆角、字体或预设完全相同。CSS 中的 OKLCH 颜色也不能直接当作 WPF HEX 颜色，必须明确转换。
