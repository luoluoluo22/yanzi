# 燕子 (Yanzi) — Agent 工作入口

所有 AI 编程助手、子 Agent、台式机/笔记本/远程服务器的开发任务，都应优先阅读：

1. [持续开发、即时验证、及时合并、按需发布](docs/development-integration-release-policy.md)（2026-10-10 起最高优先级的开发/发布分离规则）
2. [.agents/AGENTS.md](.agents/AGENTS.md)（平台、编码、扩展和具体调试约束）
3. [正式部署与回滚门禁](docs/continuous-improvement/PRODUCTION_DEPLOYMENT_POLICY.md)（仅在确实需要安装/发布时适用）
4. [**Windows 小程序开发七步工作流（必读）**](docs/miniapps/DEVELOPMENT_WORKFLOW.md)（凡新增、修复、测试或安装用户小程序，均先查真实用户目录，不能在宿主 Git 仓库写小程序源码）

**Windows 小程序唯一开发位置：`%LOCALAPPDATA%\OpenQuickHost\Extensions\<extensionId>\`。** 直接修改用户已安装的小程序，由原有账号同步机制处理，不要为业务小程序新建宿主仓库副本、PR 或安装包；该规范的完整操作和验收门禁见上述七步工作流。

**开发任务的强制结束条件：已验证的代码必须进入 GitHub main。** 每个 AI（含并行子 Agent）在任务完成后先提交自己的隔离分支，再运行 `scripts/finish-agent-task.ps1 -VerificationScript <仓库内验收脚本> -Apply` 创建 PR、等待 CI、无冲突时合并，并回读 main 的合并提交。不得把“本地已修改、已 Commit、已 Push、安装包已发布”说成“开发集成完成”。若存在 CI 失败、PR 冲突、生产部署风险或超时，必须明确标记 `awaiting-integration`，保留独立 worktree 并继续协调修复；不得静默遗留在其他分支。详见 [自动集成门禁](docs/development-integration-release-policy.md#多-ai-开发任务强制收尾门禁2026-10-22)。

**合并前必须做全工作区遗漏审计（2026-10-24）。** 由当前主管 AI 对所有相关 Git worktree、分支、未提交文件、未合并提交及开放 PR 做完整盘点，逐项建立“已集成 / 待验证 / 冲突待处理 / 明确不纳入（附理由）”清单。不能只合并当前任务的一个文件或一个提交就声称没有遗漏。所有已完成且验证通过的改动应分别提交并集成到最新 main；未完成、失败、敏感数据及相互冲突的改动必须保留并标注责任与下一步，禁止盲目 git add -A、强制覆盖、把半成品捆绑发布。合并结束后再次盘点剩余差异并核对 origin/main，报告遗留数量和具体原因。详细门禁见 docs/development-integration-release-policy.md。

**Commit ≠ Merge ≠ Release。** 开发允许隔离分支 WIP 提交，合并 main 前必须完成必要验证；验证通过后及时集成，不要攒到发布前。默认只推送代码，不自动打 Tag、发布安装包、更新正式宿主或重启用户程序。已有 Cloudflare main 自动部署属于例外，涉及其触发路径时须先批准生产部署并确认迁移安全。旧规则冲突时，以新开发规范为准。

**工作树数量控制**：创建任何新 Git worktree 之前，先阅读 [工作树生命周期规范](docs/worktree-retention-policy.md)，运行 scripts/check-worktree-cap.ps1。长期最多保留 5 个活动工作树；合并后立即清理，旧代码不优先于 origin/main。有未提交修改先做完整离线备份和哈希验证，不得强制删除用户数据。
