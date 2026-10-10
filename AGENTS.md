# 燕子 (Yanzi) — Agent 工作入口

所有 AI 编程助手、子 Agent、台式机/笔记本/远程服务器的开发任务，都应优先阅读：

1. [持续开发、即时验证、及时合并、按需发布](docs/development-integration-release-policy.md)（2026-10-10 起最高优先级的开发/发布分离规则）
2. [.agents/AGENTS.md](.agents/AGENTS.md)（平台、编码、扩展和具体调试约束）
3. [正式部署与回滚门禁](docs/continuous-improvement/PRODUCTION_DEPLOYMENT_POLICY.md)（仅在确实需要安装/发布时适用）

**Commit ≠ Merge ≠ Release。** 开发允许隔离分支 WIP 提交，合并 main 前必须完成必要验证；验证通过后及时集成，不要攒到发布前。默认只推送代码，不自动打 Tag、发布安装包、更新正式宿主或重启用户程序。已有 Cloudflare main 自动部署属于例外，涉及其触发路径时须先批准生产部署并确认迁移安全。旧规则冲突时，以新开发规范为准。