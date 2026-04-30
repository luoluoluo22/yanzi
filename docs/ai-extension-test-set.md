# AI 扩展生成测试集

这份测试集用于验证 AI 是否能为 Yanzi / OpenQuickHost 生成可导入、可测试、可运行的扩展 JSON。

统一要求：

1. AI 只能返回一个完整 JSON，不要解释，不要 Markdown 代码块。
2. JSON 必须能被 `System.Text.Json` 直接解析。
3. 除非案例明确要求，否则不要输出 `x:Class`、事件处理函数、代码隐藏文件路径。
4. `hostedViewXaml` 必须按 WPF 可解析子集来写，避免 `LetterSpacing`、`LineHeight` 这类当前宿主不兼容属性。
5. 按需求优先选择最简单稳定的实现：能用 `openTarget` 就不要上脚本，能用脚本就不要硬上复杂 XAML。

---

## 测试 1：打开桌面目录

给 AI 的需求：

> 请生成一个扩展，名字叫“打开桌面”，点击后直接打开 `F:\\Desktop`。

验收点：

- 使用 `openTarget`
- `id`、`name`、`version`、`category`、`description`、`keywords` 完整
- 不要误用脚本或 `hostedViewXaml`

常见错误：

- 把路径写成不存在的 Linux 风格路径
- 误输出说明文字而不是 JSON

---

## 测试 2：网页搜索扩展

给 AI 的需求：

> 请生成一个网页搜索扩展，前缀是“bing”和“必应”，输入关键词后打开 Bing 搜索结果。

验收点：

- 使用 `queryPrefixes`
- 使用 `queryTargetTemplate`
- 模板中包含 `{query}`

常见错误：

- 把搜索词直接写死
- 忘记配置 `queryPrefixes`

---

## 测试 3：打开记事本

给 AI 的需求：

> 请生成一个扩展，点击后打开 Windows 记事本。

验收点：

- 使用 `openTarget`
- `openTarget` 可以是 `notepad.exe`
- 不需要脚本

常见错误：

- 硬编码用户机器上不稳定的绝对路径
- 误用 PowerShell 启动进程

---

## 测试 4：C# 输入回显

给 AI 的需求：

> 请生成一个 C# 内联脚本扩展，把用户输入的文本原样返回，并额外显示长度。

验收点：

- `runtime` 为 `csharp`
- `entryMode` 为 `inline`
- `script.source` 中包含 `RunAsync`
- 读取 `context.InputText`

常见错误：

- 输出多文件项目结构
- 使用宿主不存在的命名空间

---

## 测试 5：PowerShell 剪贴板读取

给 AI 的需求：

> 请生成一个 PowerShell 扩展，读取当前剪贴板文本，如果没有文本就返回“剪贴板为空”。

验收点：

- `runtime` 为 `powershell`
- `entryMode` 为 `inline`
- 脚本返回字符串结果

常见错误：

- 调用不存在的模块
- 把脚本写成需要外部 `.ps1` 文件

---

## 测试 6：前台窗口标题读取

给 AI 的需求：

> 请生成一个扩展，读取当前前台窗口标题并返回，方便我查看当前工作窗口。

验收点：

- 优先使用 `csharp` 内联脚本
- 返回标题文本
- JSON 能直接导入测试

常见错误：

- 直接假设可以调用任意 NuGet 包
- 写成控制台程序入口而不是内联动作

---

## 测试 7：宿主便签工作区

给 AI 的需求：

> 请生成一个 hostedViewXaml 扩展，左侧是多行便签编辑框，右侧是实时预览，打开时加载本地存储，点击按钮后保存到本地和云端。

验收点：

- 使用 `hostedViewXaml`
- 根元素或按钮动作使用 `oqh:HostedViewBridge.Action`
- 至少包含 `loadStorage` 和 `saveStorage`
- XAML 只使用当前宿主兼容属性

常见错误：

- 使用 `x:Class`
- 使用 `Click="..."` 事件
- 使用 `LetterSpacing`、`LineHeight`
- 忘记声明 `xmlns:oqh`

---

## 测试 8：翻译工作区

给 AI 的需求：

> 请生成一个宿主工作区扩展，左侧输入原文，点击按钮后通过脚本把结果输出到右侧。先用假翻译占位，不调用真实 API。

验收点：

- 可选择 `hostedViewXaml + runScript`
- 或使用结构化 hosted view
- 输出区可显示脚本结果

常见错误：

- 直接依赖外网 API
- 没有状态字段，导致输出区无法绑定

---

## 测试 9：时间戳工具

给 AI 的需求：

> 请生成一个扩展，把当前时间格式化为 `yyyy-MM-dd HH:mm:ss` 返回，并附带用户输入内容。

验收点：

- 可用 `csharp` 或 `powershell`
- 返回值同时包含时间和输入
- 不需要 `hostedViewXaml`

常见错误：

- 忘记处理空输入
- 返回对象而不是字符串

---

## 测试 10：错误修复回归

给 AI 的需求：

> 请修复下面这份 hostedViewXaml JSON。要求保留“沉浸式笔记工作区”的交互意图，去掉宿主不兼容写法，只返回最终修复后的完整 JSON。

建议附上的故障样例：

- 使用 `LetterSpacing`
- 使用 `LineHeight`
- 使用 `x:Class`
- 使用 `Click="Save_Click"`

验收点：

- AI 能保留原有功能意图
- 删除或替换不兼容属性
- 返回一个可再次测试的完整 JSON

常见错误：

- 直接把复杂界面退化成普通 `openTarget`
- 删除存储动作，导致功能退化

---

## 推荐使用方式

可以把这 10 条逐条喂给 AI，观察：

1. 首次生成是否可解析
2. 点击“测试扩展运行”是否通过
3. 失败后是否能根据日志二次修复
4. 是否倾向于选择过度复杂的实现
5. 是否会输出宿主当前不支持的 WPF 属性

## 建议记录字段

每次测试建议记录：

- 模型名称
- 测试编号
- 首次是否通过 JSON 解析
- 首次是否通过运行测试
- 是否需要二次修复
- 最终是否可用
- 典型错误类型
