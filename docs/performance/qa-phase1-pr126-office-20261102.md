# PR #126 物理 GET 诊断：办公室独立 QA（2026-11-02）

- 设备：CHINAMI-3IF988E；北京时间 12:39–12:49。
- 独立源码：D:\Projects\qa-phase1-pr126-20261102-1239；commit d2008bde8f4d3433137d0d16992e76aa6fb03df4。未修改 PR #127/#128 的工作树。
- Windows PowerShell 5.1 scripts/verify-phase1-physical-get.ps1：PASS；两个闭合窗口、13 次物理 GET 模拟、其他 HTTP 分类、非法时间戳、状态分类不一致、缺失字段、覆盖不足与脱敏边界均通过。该测试是合成日志，不是生产 Gitee HTTP 量。
- CI 41337286788：completed/success，Windows Desktop build 和 Phase-one physical GET closed-window parser regression 均成功。
- 办公室独立 .NET SDK 9.0.320：Release build exit 0，0 warnings，0 errors。原仓库构建脚本只找系统 SDK，首次失败；在本次独立 QA clone 临时复制 PR #127 已验证的 DOTNET_ROOT 兼容脚本后构建通过。该临时脚本改动未加入 PR #126 提交，本 PR 仍仅涉及诊断脚本、CI 和文档。
- Canary：从独立 Release bin 复制 143 个文件到 %LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-pr126-office-20261102-1248，Yanzi.dll SHA-256 419AC4C7956163DDECA8BE0EA381478F5FC49C4AE9832B83654AA23B3B721109 与构建产物一致；--dev --show PID 8020 存活 20 秒，私有内存 66.22 MiB、20 线程、584 句柄；测试进程已关闭。正式版 PID 29104 前后不变。
- 机器本地隔离证据：%LOCALAPPDATA%\OpenQuickHost\QA\phase1\office-pr126-canary-20261102.json。
- 此 PR 仅新增只读统计脚本及 CI，不改生产同步执行逻辑。--dev 禁用真实账户同步；该 Canary 只验证独立启动，不代表可见仓库 UI 或真实 Gitee 业务通过。第一阶段仍缺同负载物理 GET 降幅、家里同版本 Canary、自动同步新增/修改/冲突/删除/恢复一致性，绝不以本 PR 合并宣布阶段一完成。
- 安全：未读取或修改生产密钥、账号数据、IndexedDB、回滚包、ReleaseQueue；未重启正式版。
