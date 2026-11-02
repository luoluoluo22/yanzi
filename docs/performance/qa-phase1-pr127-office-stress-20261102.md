# PR #127 办公室独立重复回归（2026-11-02）

- 设备：CHINAMI-3IF988E；北京时间 10:37–10:40；源码 SHA 5702d8048da5df8c06a6d03b5e1f33bb52897a8d。
- 使用隔离 .NET SDK 9.0.320；独立工作树 D:\Projects\qa-phase1-pr127-20261102；测试使用 dotnet run --no-build -c Release，未重新编译，也未触碰正式版或其账号数据。
- GiteeReadFlightVerification：30/30 轮通过，平均 3171.7 ms，最长 18712 ms。
- GiteeHttpReadMetricsVerification：15/15 轮通过，平均 4377.7 ms，最长 6486.9 ms。
- 证据：%LOCALAPPDATA%\OpenQuickHost\QA\phase1\office-pr127-stress-20261102.json，SHA-256 7CC3527709C4C93A59B4F05EAFFD782BDE70733CFD7C7E6647160F5F07E48A44；仅保存聚合统计、测试版本与失败次数，不含 token 或 URL。
- 正式版 PID 29104 在回归后保持不变。以上测试为模拟网络并发与计数回归，不是真实 Gitee GET 减量、鉴权重试或跨端同步一致性验收。
- 家里 PC-202409131508 MCP 读取仍处于 pending；同提交双端 Canary 与真实业务 QA 缺失，PR #127 继续保持 draft，不合并。