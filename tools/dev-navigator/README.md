# 燕子开发功能导航（Dev Navigator）

目的：在**修改代码之前**，把用户说的中文功能、屏幕上的 WPF 控件、C# 事件方法、配置与测试关联起来，减少重复全文搜索和误改。

本工具是独立开发工具，仅扫描 Git 已跟踪的 src/、tests/、scripts/、tools/ 中的 C#、XAML、PowerShell 和项目文件；不会扫描 .env、安装用户数据、小程序真实目录或密钥。它不是运行在 Yanzi.exe 中的驻留服务。

**统一开发前入口（第二阶段）：** 优先使用 `scripts/dev-preflight.ps1 -Query '模型列表不能滚动' -UiQuery AiChatModelMenuButton_Click`，在一次调用中同时输出功能、界面、影响和测试计划。测试默认不执行，按需 `-RunTargeted`、`-RunHiddenUi`。参见 [开发前统一检查](../../docs/dev-preflight.md)。以下保留细粒度定位命令。

**第三阶段开发知识库：** `scripts/dev-knowledge.ps1 -Mode coverage -Query '设置'` 可发现尚未登记的 XAML 事件入口；统一开发前检查在执行专项/隐藏桌面测试后自动生成带 Git 版本和测试范围的本机 QA 回执。它不会把测试结果冒充真实客户端 UI 验收。详见 [开发知识库](../../docs/dev-knowledge.md)。

## 一条命令定位功能

在项目仓库根目录执行：

```powershell
.\scripts\dev-locate.ps1 -Mode find -Query '模型列表不能滚动'
.\scripts\dev-locate.ps1 -Mode ui -Query AiChatModelMenuButton_Click
.\scripts\dev-locate.ps1 -Mode ui -Query '选择供应商和模型'
.\scripts\dev-locate.ps1 -Mode impact -Paths 'src/OpenQuickHost/AiModelMenuOrdering.cs'
.\scripts\dev-locate.ps1 -Mode impact -Base HEAD~1 -Json
.\scripts\dev-locate.ps1 -Mode index
```

也可以执行 `python tools/dev-navigator/navigator.py find --query "模型列表"`。

**首次索引**从源码生成，后续只重新解析变更的文件。缓存位于
`%LOCALAPPDATA%\OpenQuickHost\DevNavigator\<worktree-hash>\index.json`，
不加入 Git、不云同步。每次查询自动检查 Git 跟踪文件列表和文件的
大小与修改时间，及时失效缓存；如有异常，可使用 `-Force` 重建。

## 输出说明

- **find**：根据业务名称/中文别名返回关联文件、测试入口、事件名称；优先显示人工校验的业务路由。
- **ui**：通过 XAML 控件名称、事件处理器或静态文本定位 XAML 声明，并列出可解析的 C# 方法定义及行号。
- **impact**：读取明确给定的文件，或用 `-Base` 读取 Git diff，列出关联的功能及推荐测试；不在映射中的改动明确标记为 `REVIEW_UNMAPPED`，绝不默认为没有风险。
- **index**：显示索引文件数、扫描数、缓存复用数和耗时，可配合 `-Json` 供 AI 调用。

业务别名与已确认的关联关系配置在 `tools/dev-navigator/features.json`。开发者新增功能时，应将对应中文别名、关键源文件、测试清单补入该文件。XAML 控件和方法位置是自动提取的，**不在 JSON 中保存行号**，因此代码移动后会自动更新。

**边界**：这属于启发式导航，不是完整的 Roslyn 调用图。方法同名可能存在多个候选，动态绑定、反射、运行时小程序及跨仓库调用必须人工复核；建议的测试不是覆盖率证明。修改共享组件时尤其要额外检查下游影响。禁止仅因为 `impact` 没有结果就绕过测试。

## 开发前 SOP

1. 查 `find`，确认功能链；必要时使用 `ui` 核对控件/事件。
2. 明确期望行为、保持不变的约束、失败路径、当前运行实例和修改范围。
3. 查 `impact` 得到最小回归测试清单，并补充边界测试。
4. 按既有规范在隔离分支修改、验证、审查、合并。
5. 如果新增 UI 或功能入口，完善 `features.json`；索引会在下一次调用时自动增量更新。
6. 实际是否发布仍按已有开发/发布授权与 `scripts/release-lifecycle.ps1` 规则执行。

离线回归：`python -m unittest discover -s tools/dev-navigator -p 'test_*.py' -v`。
