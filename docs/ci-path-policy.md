# 燕子 CI 分层加速与安全回退

## 设计目标

开发导航/诊断工具不依赖 Windows 客户端、Cloudflare D1、移动端或浏览器扩展。纯工具变更应尽快收到反馈；任何不能明确证明是纯工具变更的 PR 或 main 提交继续运行原有完整回归。

工作流：`.github/workflows/engineering-checks.yml`

分类器：`scripts/classify-ci-change.py`

离线测试：`python scripts/test-ci-change-classifier.py`

## 分类策略（fail closed）

仅当 Git 两个提交之间 **全部** 变更都在以下白名单内，分类为 `dev-tools`：

- `tools/dev-navigator/` 下以 `.py`、`.json`、`.md` 结尾的文件。
- 精确列举的 `scripts/dev-{locate,preflight,knowledge,diagnose}.ps1` 及相应 `scripts/test-dev-*.ps1` 开发辅助命令。
- `docs/dev-preflight.md`、`docs/dev-knowledge.md`、`docs/dev-diagnostics.md`。

任何源码、同步、安全、API、部署脚本、`AGENTS.md`、`.github/workflows`、分类器本身或测试分类器的修改，均走 `full`。空差异、缺失 Git revision、Git 异常、删除或重命名跨出白名单、路径格式异常都走 `full`。

分类使用 PR base/head 或 main push before/after 的 Git diff，**不信任**用户传入的 PR 标签、提交说明或手工开关，也不根据文件数量简单跳过测试。

## 两种执行模式

| 模式 | portable-contracts | desktop-and-worker |
| --- | --- | --- |
| `full` | 原有 Node、.NET、Java、Worker/SDK 回归全部执行 | 原有 Windows 构建及所有桌面/网络/D1 回归全部执行 |
| `dev-tools` | 在 Ubuntu 执行分类器 9 项边界测试和 dev-navigator 全部单元测试 | 在 Ubuntu 快速确认已由独立分类器识别，保留原检查名称及通过/失败门禁 |

另有独立 `classify` 作业检查路径。若分类失败，依赖作业无法正常完成，PR 不能按现有集成脚本的门禁自动合并。原本要求的 `portable-contracts` 和 `desktop-and-worker` 名称保持不变；不通过路径过滤消失检查。

首次改造了 CI 工作流，所以其本身的 PR 一定属于 `full`，必须经过完整回归。合并后可通过一个只修改 `tools/dev-navigator` 目录文件的独立 PR，实证 `dev-tools` 模式、执行耗时和合并流程。

## 保守边界

- 只优化本工程的 `Engineering foundation regression` 工作流，不更改正式发布、生产部署或双端安装触发条件。
- `dev-tools` 并不意味着无需检查：所有导航/诊断单元测试必须通过；出现未知路径自动回退 `full`。
- Python 运行在 PR 自身签出的版本上。这属于现有 PR 执行源码的信任模型，绝不能在快速测试中传入生产密钥或仓库写权限。
- 本机制并非语义上的跨模块依赖证明。未来如果开发工具开始被客户端运行时直接引用，必须撤销对应路径的白名单。
- 是否确实节省 CI 耗时须由 GitHub Actions 同一仓库实际运行结果证明，不能仅据本机离线测试宣称。
