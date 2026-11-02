# 第一阶段 PR #127 — 办公室真实公网 Gitee GET 计数与隔离 Canary QA（2026-11-02）

## 范围与安全边界
- 北京时间 14:35–14:42；办公室 CHINAMI-3IF988E；隔离工作树 D:\Projects\qa-phase1-pr127-20261102，原 head bcc71cd9ff7b0e19b1e55c4bf1885994d7757152。
- 仅向公开 Gitee API https://gitee.com/api/v5/repos/openharmony/docs 发送两次匿名 HTTPS GET；不使用账号 token、不写入任何 Gitee 对象、不修改用户同步状态。
- 扩充 tests/GiteeHttpReadMetricsVerification/Program.cs 的可选 YANZI_GITEE_PUBLIC_PROBE=1 测试；默认 CI 不依赖外网。测试使用真实 HttpClientHandler（禁用 Cookie 与重定向），而非 FakeHandler。

## 自动测试和真实网络证据
- 隔离 .NET SDK：D:\Projects\_qa_tools\dotnet9\dotnet.exe（9.0.320）。
- 带可选公网探针的 GiteeHttpReadMetricsVerification 全部断言通过；真实公网 HTTPS GET 2/2 返回 HTTP 200，物理 handler 窗口合计 sent=2、completed=2、success2xx=2，异常/取消/429或5xx=0。
- GiteeReadFlightVerification 回归通过；并发 singleflight 的 64→1 仍为模拟 handler，不是公网并发负载。
- 真实 GET 证明计数器可观察网络发送，不能代表生产 Gitee 账号、实际业务同步、同负载降频比例或 429/503 重试。没有以 2 次请求推算生产请求降幅。
- PR #127 之前 head bcc71cd9 的 GitHub Actions #41396755237：classify、desktop-and-worker、portable-contracts 全部 success；本次新增测试提交后必须重新验证 CI。

## 独立编译和 Canary QA
- 隔离 win-x64 self-contained Release publish 退出码 0，586 文件；Yanzi.dll SHA-256 868911355A463DD7F9611E7576DA3E7748315ABE0DF29E8391C1F36A77ADF30C。编译存在既有 nullable/unused 警告，无编译错误。
- 独立 Canary 安装目录 D:\Projects\_qa_tools\canary-phase1-20261102-1435；以 --dev --show 启动 PID 12668，23 秒后对 20 个顶层 HWND 逐一执行 20 次 WM_NULL：400/400 成功，P95 0.1059ms、P99 0.1771ms。
- Canary 私有内存 229.11 MiB、句柄 861、线程 37；单次启动测量，不与不同启动阶段的旧样本直接比较，不等同仓库业务交互压力测试。
- 测试进程已停止；正式版 PID 29104 前后保持不变，未修改密钥、IndexedDB、账号同步、备份、回滚包或正式 ReleaseQueue。
- 本机聚合证据：%LOCALAPPDATA%\OpenQuickHost\QA\phase1\office-pr127-public-http-20261102.json。

## 尚缺门禁
- 家里 PC-202409131508 的浣熊读取调用持续 pending，Desktop Commander 报 Offline；没有唤醒设备，也未取得同 head 家里 Canary。
- 仍缺同负载真实账号 Gitee 物理 GET 前后对照、真实失败/Retry-After、双端新增/修改/冲突/删除墓碑/恢复一致性；开发版 --dev 不开启真实账号同步。
- 第一阶段未验收，PR #127 保持 Draft；第二至第四阶段未完成。
