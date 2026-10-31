# 燕子 (Yanzi) — Agent 工作入口

所有 AI 编程助手、子 Agent、台式机/笔记本/远程服务器的开发任务，都应优先阅读：

1. [持续开发、即时验证、及时合并、按需发布](docs/development-integration-release-policy.md)（2026-10-10 起最高优先级的开发/发布分离规则）
2. [.agents/AGENTS.md](.agents/AGENTS.md)（平台、编码、扩展和具体调试约束）
3. [正式部署与回滚门禁](docs/continuous-improvement/PRODUCTION_DEPLOYMENT_POLICY.md)（仅在确实需要安装/发布时适用）
4. [**Windows 小程序开发七步工作流（必读）**](docs/miniapps/DEVELOPMENT_WORKFLOW.md)（凡新增、修复、测试或安装用户小程序，均先查真实用户目录，不能在宿主 Git 仓库写小程序源码）

**开发前统一检查（2026-10-31）。** 宿主/共享 UI/接口开发前，优先执行 `scripts/dev-preflight.ps1 -Query '<功能描述>'`，一次返回功能源码、界面事件、改动影响和测试清单；有明确行为的 UI 修复补充 `-Expect` 与 `-Preserve`，需要时显式启用 `-RunTargeted` 和 `-RunHiddenUi`。其专项 UI 测试仍不等于真实用户桌面操作验收。详见 [开发前检查](docs/dev-preflight.md)。单独追查底层符号时可继续运行 `scripts/dev-locate.ps1 -Mode find -Query '<用户描述>'`；如果涉及界面，再运行 `-Mode ui -Query '<控件文案或事件名>'`，并在修改前记录预期行为、保持不变的约束及对应测试。修改后运行 `-Mode impact -Paths <改动文件>`（或 `-Base <基线提交>`），核对建议测试及 `REVIEW_UNMAPPED`。索引未匹配不表示零风险，不得因此跳过代码审查与测试。详情：[开发功能导航](tools/dev-navigator/README.md)。该索引仅作为代码导航辅助，不改变小程序唯一真实开发目录与按需发布规则。

**功能索引候选与 QA 知识库（2026-10-31）。** 如果功能没有业务映射，可用 `scripts/dev-knowledge.ps1 -Mode coverage -Query '<关键词>'` 查找真实 XAML 控件与事件候选，不得把候选关系直接当已确认实现。`dev-preflight.ps1` 显式运行测试后会把有来源的本机 QA 回执关联到功能，但工作区未提交变更或 Git SHA 不同的记录不能被标为当前通过；隔离桌面测试不能冒充真机验收。详见 [知识库规范](docs/dev-knowledge.md)。

**故障定位优先（2026-10-31）。** 收到故障描述、错误截图文字或授权的本机日志后，先执行 `scripts/dev-diagnose.ps1 -Description '<症状>'`；需要时显式传入 `-ScreenshotNote`（人工/视觉读出的文字）或白名单 `-Log`。诊断工具只返回可能相关功能、源码、最近改动、QA 记录及下一步检查，不宣称已定位根因，不自动上传原始日志/密钥。再使用 `dev-preflight.ps1` 制定可验证的修改计划，按 PR/CI 门禁集成。详见 [故障诊断规范](docs/dev-diagnostics.md)。

**Windows 小程序唯一开发位置：`%LOCALAPPDATA%\OpenQuickHost\Extensions\<extensionId>\`。** 直接修改用户已安装的小程序，由原有账号同步机制处理，不要为业务小程序新建宿主仓库副本、PR 或安装包；该规范的完整操作和验收门禁见上述七步工作流。

**开发任务的强制结束条件：已验证的代码必须进入 GitHub main。** 每个 AI（含并行子 Agent）在任务完成后先提交自己的隔离分支，再运行 `scripts/finish-agent-task.ps1 -VerificationScript <仓库内验收脚本> -Apply` 创建 PR、等待 CI、无冲突时合并，并回读 main 的合并提交。不得把“本地已修改、已 Commit、已 Push、安装包已发布”说成“开发集成完成”。若存在 CI 失败、PR 冲突、生产部署风险或超时，必须明确标记 `awaiting-integration`，保留独立 worktree 并继续协调修复；不得静默遗留在其他分支。详见 [自动集成门禁](docs/development-integration-release-policy.md#多-ai-开发任务强制收尾门禁2026-10-22)。

**合并前必须做全工作区遗漏审计（2026-10-24）。** 由当前主管 AI 对所有相关 Git worktree、分支、未提交文件、未合并提交及开放 PR 做完整盘点，逐项建立“已集成 / 待验证 / 冲突待处理 / 明确不纳入（附理由）”清单。不能只合并当前任务的一个文件或一个提交就声称没有遗漏。所有已完成且验证通过的改动应分别提交并集成到最新 main；未完成、失败、敏感数据及相互冲突的改动必须保留并标注责任与下一步，禁止盲目 git add -A、强制覆盖、把半成品捆绑发布。合并结束后再次盘点剩余差异并核对 origin/main，报告遗留数量和具体原因。详细门禁见 docs/development-integration-release-policy.md。

**Commit ≠ Merge ≠ Release。** 开发允许隔离分支 WIP 提交，合并 main 前必须完成必要验证；验证通过后及时集成，不要攒到发布前。默认只推送代码，不自动打 Tag、发布安装包、更新正式宿主或重启用户程序。已有 Cloudflare main 自动部署属于例外，涉及其触发路径时须先批准生产部署并确认迁移安全。旧规则冲突时，以新开发规范为准。

**工作树数量控制**：创建任何新 Git worktree 之前，先阅读 [工作树生命周期规范](docs/worktree-retention-policy.md)，运行 scripts/check-worktree-cap.ps1。长期最多保留 5 个活动工作树；合并后立即清理，旧代码不优先于 origin/main。有未提交修改先做完整离线备份和哈希验证，不得强制删除用户数据。
