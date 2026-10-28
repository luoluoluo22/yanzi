# Ego 小程序宿主仓库隔离修复（2026-10-24）

背景：小程序安装与开发的正式目录是 `%LOCALAPPDATA%\\OpenQuickHost\\Extensions\\<id>\\`，宿主主仓库不允许追踪 `extensions/*/manifest.json`。此前合并的 Ego 小程序源码误存入宿主仓库，导致 Windows CI 的 `verify-miniapp-repository-isolation.ps1` 阻断后续无关任务。

## 本次迁移

从**宿主 Git 跟踪树**移除三个目录，不触碰已安装的用户小程序：

- `extensions/ego-agent-team-service`：家里及办公室都有独立安装副本
- `extensions/ego-agent-team-workbench`：家里及办公室都有独立安装副本
- `extensions/ego-browser-api`：此前**未以该 ID 正式安装**；不能把它当作已交付小程序

迁移前已经将三个 Git 工作目录的全部 24 个文件复制到家里电脑的 `%LOCALAPPDATA%\\YanziTransferLab\\miniapp-recovery\\`，逐文件执行 SHA-256 比对；ZIP 独立归档于同目录。宿主的原始 Git 提交仍保留完整历史，可通过：

```powershell
git show b87a7e579655c775a1fc4bf79256a11166000463:extensions/ego-browser-api/manifest.json
git show 1fb676a1128e078a6d277f6cbe0998b004c81120:extensions/ego-agent-team-service/server.mjs
```

取回历史源码。恢复工作时，应在各自独立小程序仓库或用户安装目录进行开发，经过编译、集成、设备云同步与真机验证后再发布。不要把目录直接重新加入宿主 Git，也不要以取消 CI 隔离检查来处理冲突。

本变更不清除 Git 历史、云端应用数据、设备已安装小程序或二进制安装包。它仅修正宿主仓库源码布局。
