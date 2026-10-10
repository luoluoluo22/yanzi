# 燕子长期 Agent 本机部署权限：实施与端到端验收（2026-10-09）

## 授权与配置

用户授权五个长期 Agent 在满足门禁时将验证过的修复部署到当前 Windows 燕子正式环境，并独立验收；无需每轮向用户再申请许可。仍禁止自动 GitHub push、公开发布、远程生产部署与破坏性用户数据操作。

- 已新增 docs/continuous-improvement/PRODUCTION_DEPLOYMENT_POLICY.md；说明权限边界、测试、备份、版本核查、发布、真实反馈与回滚门槛。
- 已新增 scripts/ai-loop-promote-extension.ps1；可受控升级已安装小程序的指定文件，默认仅预览，使用 -Apply 才执行。
- 五个长期任务的 Prompt 均已追加明确授权与结构化结果字段；原有七条任务与历史执行结果保留。备份位于 F:\Desktop\kaifa\OpenQuickHost\.artifacts\ai-loop-production-authorization-20261009-170438。
- 性能守护、能力网络完善：重新启用，首次最早检查 2026-10-09 17:09:13+08:00，后续最小重复间隔 360 分钟。
- 稳定性守护、同步可靠性：启用，保留 2026-10-10 04:49:42+08:00 最早反馈窗口，最小重复间隔 720 分钟。
- AI 使用质量：获得相同权限但仍保持禁用，缺少有效真实会话质量基线时不重复调用。

## 合成小程序实际部署回归

专用测试扩展 ai-loop-release-gate-fixture，不接触正常用户数据：

1. Dry-run：PLAN_ONLY=True，旧版 0.0.1，候选 0.0.2，2 个文件；未停止进程或修改正式目录。
2. 成功发布：独立 preflight PASS，保存回滚备份，启动并验证健康状态，PRODUCTION_ACCEPTED=True，升级至 0.0.2。
3. 故障注入：将候选 0.0.3 的 C# 入口故意写成无效内容；启动失败后返回 PRODUCTION_REJECTED，自动恢复之前版本。
4. 故障验证：RESTORED_VERSION=0.0.2、RESTORED_CODE_VALID=True。
5. 测试后核实未运行并清理专用临时生产 Extensions 目录。合成备份保存在 %LOCALAPPDATA%\OpenQuickHost\AiLoopReleaseBackups\ai-loop-release-gate-fixture-*。

此 E2E 验证的是“小程序定向发布与回滚入口”；宿主 Runtime 的版本冲突、灰度及真实业务效果仍需其对应的部署门禁与长期样本。此次没有推送任何公开版本。
