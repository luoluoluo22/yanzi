# 开发知识库第三阶段：自动候选映射 + 真实测试回执

这是一套独立的开发辅助工具，不修改燕子客户端，也不扫描用户密钥、聊天记录或本地小程序安装目录。

## 自动发现未登记的 UI 入口

    .\scripts\dev-knowledge.ps1 -Mode coverage
    .\scripts\dev-knowledge.ps1 -Mode coverage -Query '剪贴板' -Limit 30
    .\scripts\dev-preflight.ps1 -Query '模型菜单' -DiscoverUnmapped -Json

扫描 Git 已跟踪的 XAML 定义，提取具备明确事件处理器的控件，对照 features.json 中人工核实过的处理器；输出控件、源码路径、行号、处理器和静态标签。统计口径仅限 XAML 事件入口，不代表项目功能覆盖率、测试覆盖率或缺陷数量。候选项不会自动并入 features.json，必须先审查业务归属与专项测试。

## 将实际执行的 QA 与功能关联

    .\scripts\dev-preflight.ps1 -Query 'ai-model-menu' -Expect '50个模型可滚动' -RunTargeted -RunHiddenUi -Json
    .\scripts\dev-knowledge.ps1 -Mode qa -Features 'ai-model-menu'

只有显式执行 -RunTargeted / -RunHiddenUi，并且测试命令实际返回结果，才能在开发知识库中生成回执。回执仅记录业务功能 ID、隔离测试范围、测试名、通过/失败、退出码、Git 提交 SHA、是否存在未提交修改、UTC 时间；不写入日志正文、密钥或对话内容。运行失败也会留下失败记录，不能只存成功结果。

回执存储在 %LOCALAPPDATA%\OpenQuickHost\DevNavigator\<git-common-dir-hash>\qa-ledger.json。该目录不属于 Git 仓库且不参与云同步；同一仓库多个 worktree 共用回执，但每个工作树的源码索引缓存独立。仅保留最近 250 条记录。

## 如何判断记录是否有效

- 提交 SHA 完全匹配、工作区无未提交改动、回执来自干净工作区，才允许报告 freshForCurrentCommit=true。
- 源码发生新提交、被修改但未提交，旧 QA 立刻不能作为当前版本的通过证据。
- 独立 .NET 测试、隐藏桌面 WPF 验收分别记录作用域；隐藏桌面是模拟数据与隔离窗口，不等于已安装燕子的物理鼠标/键盘/网络操作。
- installedAppUi 始终显示 not-verified，除非未来另行建设可信真机验收采集机制。本阶段不会把合成 UI 测试冒充真机验证。
- 缓存文件损坏会报错并保留原文件，不会静默覆盖。仅本机开发权限能够修改记录；它不是防恶意篡改的签名审计系统。

## 开发闭环

1. 开始前：dev-preflight 自动定位文件、事件、推荐测试及上次可信 QA 状态。
2. 遇到新界面：dev-knowledge coverage 按关键词列出未映射候选，确认后编辑 features.json。
3. 修改后：dev-preflight 显式运行对应专项测试 / 隐藏桌面 UI 测试，自动留下结果回执。
4. 代码提交后：因 SHA 和 dirty 状态改变，旧测试记录按过期处理；在干净的目标提交上重跑，才可标注当前提交的独立测试通过。
5. 仍按 PR/CI 门禁集成主线；软件发布和真实设备安装是不同流程，不由本工具触发。

回归：python -m unittest discover -s tools/dev-navigator -p 'test_*.py' -v
完整统一检查：docs/dev-preflight.md
