# PR #127 — 办公室 FileSystemWatcher 实际事件风暴隔离 QA（2026-11-02）

- 时间：北京时间 12:09–12:12；设备：CHINAMI-3IF988E。
- 代码：`fix/phase1-gitee-http-lifecycle-20261102`（测试时 PR #127 head `7752c79160c80967213d569ae294cbc5d82388b8`）。
- 独立 Canary：`%LOCALAPPDATA%\OpenQuickHost\DevBuilds\qa-phase1-pr127-office-20261102\Yanzi.exe --dev --show --watcher-qa`，Yanzi.dll SHA-256 `C68CDA707145D6F175EFE2E3C57B959D47E5B63E42666C0F585C8BE67B7AE4C6`。
- 隔离：`--dev` 使用 `%LOCALAPPDATA%\OpenQuickHost.Dev`，禁用真实账号/个人同步和自动更新。未操作正式扩展目录、密钥、IndexedDB、历史回滚包或 ReleaseQueue。

## 实际执行与数据

在开发版 Extensions 下创建两个仅用于测试的唯一扩展 ID 目录，分三批对每个目录各写入 110 个文本文件，总计 **660 次文件写入**，批次之间间隔 1.9 秒。测试结束后关闭测试进程，随后删除且仅删除这两个专用目录。

真实 FileSystemWatcher 记录三次批处理：

| 批次 | 有效 watcher 事件 | 扩展 ID 数 | catalogChanged |
| --- | ---: | ---: | --- |
| 1 | 373 | 2 | false |
| 2 | 440 | 2 | false |
| 3 | 335 | 2 | false |
| 合计 | **1,148** | 每批 2 | — |

因此，在这次**特定测试负载**下，1,148 次原始 watcher 通知只产生 **3 次** `Extension watcher batch`，平均每批约 382.7 条通知。不能把该比率解释为真实 Gitee HTTP 请求减少比例；也不能据此断言未记录的 Dispatcher 投递数量精确为 3。

第一次尝试只等待启动 7 秒，记录 0 个批次并判定失败。查看开发日志后发现程序仍在扫描已安装应用（最后扫描完成日志晚于开始写文件）；将**测试脚本**启动等待延长至 25 秒后复测通过。该失败属于 QA 启动时序不足，**没有修改产品代码**。

## 安全与证据

- 正式版 Yanzi.exe PID：测试前 **29104**，测试后 **29104**；测试 Canary PID **13556** 已退出。
- 测试专用目录残留：**0**。
- 机器本地聚合证据：`%LOCALAPPDATA%\OpenQuickHost\QA\phase1\office-pr127-watcher-storm-20261102.json`，SHA-256 `8A7A96489287A674DA7EB7D1FD6CD84B5B66ED3E0253D9948EDAF40523739D79`。
- 可复跑脚本：同一 QA 目录下 `run-office-pr127-watcher-storm-20261102.ps1`，SHA-256 `C3EAD93E3EEEF0D3B0FB585A76E45A73300FB492DC29B397AF20858D93256FD7`。脚本仅在没有其他 --dev 进程且正式实例恰有一个时运行；清理限定到自身创建的唯一目录。
- 关联 CI：PR #127 head `7752c79` 的 GitHub Actions `41379153854` 三个作业（classify、desktop-and-worker、portable-contracts）均成功。

## 结论与剩余门禁

**办公室 watcher 合并真实压力测试通过。第一阶段整体仍未完成。** 本次不验证 `.cs`/manifest 目录刷新（此前家里独立 QA 已覆盖），也不验证真实 Gitee HTTP 发送次数、失败重试、同负载对照、双端新增/修改/冲突/删除/恢复一致性。家里 PC-202409131508 本轮浣熊连接仍 pending。PR #127 维持 draft，不合并、不正式发布。
