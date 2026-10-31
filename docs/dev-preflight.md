# 开发前统一检查：定位 → 影响 → 测试

第三阶段已支持 `-DiscoverUnmapped`（XAML 未映射候选）和 QA 执行回执：显式运行 `-RunTargeted` / `-RunHiddenUi` 后自动将来源、状态、Git 提交及干净工作树标志存到本机开发知识库，并在后续查询返回 `qaHistory`。参见 [QA 知识库](dev-knowledge.md)。这不是正式客户端 UI 验收。

第二阶段在原有开发功能索引之上，增加一个统一命令。默认只分析已跟踪的源码、控件与测试映射；不会修改工作区、Git、应用安装、真实配置，也不会运行测试。

## 快速使用

在仓库根目录执行：

    .\scripts\dev-preflight.ps1 -Query "模型列表不能滚动" -UiQuery "AiChatModelMenuButton_Click" -Expect "50个模型可滚动到底部" -Preserve "已配置模型置顶"

修改后：

    .\scripts\dev-preflight.ps1 -Query "模型选择" -Paths "src/Yanzi.UI.Wpf/YanziDropdownMenu.cs" -Json
    .\scripts\dev-preflight.ps1 -Base HEAD~1 -Json

可选将报告保存在忽略目录，不默认持久化用户任务文本：

    .\scripts\dev-preflight.ps1 -Query "窗口居中" -Out ".tmp/dev-preflight/window.json" -Json

报告包括 source.features/source.uiControls/source.candidateFiles、impact.affected/impact.unmapped、validation.tests/validation.hiddenUiSmoke、review.concerns。
报告里的测试是候选测试，不是覆盖率保证。若没有验收条件、改动未映射或没有 UI 专项验证，会显示 needs-review。

## 自动测试（显式选择）

    .\scripts\dev-preflight.ps1 -Query "模型列表不能滚动" -RunTargeted
    .\scripts\dev-preflight.ps1 -Query "模型列表不能滚动" -RunHiddenUi

- RunTargeted 仅运行已核验的独立测试项目白名单。其他 PowerShell 测试仅推荐，需先检查副作用；不会自动执行。
- RunHiddenUi 使用现有隐藏桌面 UI 验证脚本。模型菜单对应 ai-chat-model 专项场景，包含 50+ 模型、滚动、层级、选择及截图；其他 WPF 相关任务回退到 shared-ui 通用烟测。
- 隐藏桌面里的模拟数据测试不等于已安装程序真实鼠标滚轮或窗口层级验收；需要真实 UI 行为时仍应单独补充测试。
- 不支持自动输入真实设备密钥、真实剪贴板和物理鼠标。失败退出码非零，脚本不自动合并、发布或安装。

## 开发前 SOP

1. 一条命令生成任务定位、关键源码、影响清单、对应测试与未验证风险。
2. 明确可观测验收条件以及保持不变的约束。未定义条件不能视为完成开发前检查。
3. 在隔离工作树修改，运行 RunTargeted 与所需 RunHiddenUi，必要时做独立真机 UI QA。
4. 用 Git diff 复查共享依赖与 UNMAPPED_CHANGES。按现有 PR/CI 门禁合并，按授权规则决定是否发布。

回归：powershell.exe -NoProfile -File scripts/test-dev-preflight.ps1
原始索引文档：tools/dev-navigator/README.md
隐藏桌面测试说明：tools/yanzi-ui-test-runner/README.md
