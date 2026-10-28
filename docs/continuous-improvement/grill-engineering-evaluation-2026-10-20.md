# grill-engineering 实跑及燕子借鉴记录（2026-10-20）

## 1. 外部项目验证

- 上游：<https://github.com/GRD-Chang/grill-engineering>。
- 克隆点：`216bc865f353ff8c0a9288e62b5de7cf42c8a086`。
- 隔离位置：远程 Ubuntu 服务器 `/tmp/yanzi-grill-review-20261020`；不安装 systemd Runner、不连接生产项目 GitHub Issue。
- 初始环境：Ubuntu 22.04、Python 3.10.12、Git 2.34.1；不满足上游 Python >=3.11 / Git >=2.40 条件。
- 隔离工具链：在克隆目录 `.sandbox/` 安装 Python 3.12.15、Git 2.50.1，并安装包与 dev 依赖。没有更改系统默认 Python/Git。
- 用 Python 3.12 + 系统 Git 2.34.1 运行 6 个核心测试文件：**219 通过、28 失败**，大量失败来自 `git merge-tree --write-tree` 不受旧 Git 支持。
- 切换隔离 Git 2.50.1，重跑相同 6 个测试文件：**246 通过、1 失败，约 99.6%**。
- 尚有一项 `tests/test_sqlite_state.py::test_work_cost_does_not_grow_with_unrelated_or_older_same_ticket_history[run-1-True]` 失败：历史量变化时测得 SQL steps 5750 -> 10160，高于测试阈值，需上游或进一步调试；不能宣称原项目全套测试通过。
- 验证范围是 mock/fixture + 仓库内核心测试，**未真实登录 Codex 发起长程 GitHub PR，也未运行生产自动合并**。
- 命令：`PATH=$PWD/.sandbox/git/bin:$PATH .sandbox/venv/bin/python -m pytest -q tests/test_run_orchestration.py tests/test_ticket_work_admission.py tests/test_ticket_work_scheduling.py tests/test_run_acceptance.py tests/test_run_scenarios.py tests/test_sqlite_state.py`。

## 2. 借鉴的机制（不是拷贝其生产 Runner）

适配燕子已存在的单进程 ChatGPT Bridge，新增 `tools/chatgpt-bridge/delivery-ledger.mjs`、`delivery-api.mjs`，增加受控的可选 HTTP API：

| 上游机制 | 燕子目前新增 |
| --- | --- |
| Ticket DAG 与依赖解锁 | `createRun` 校验循环、缺失依赖，依赖完成后才能领取后续任务 |
| 受限并发与 FIFO | 默认 3 个非集成席位，按任务顺序领取 |
| 独立 QA | reviewer 不可与该提交 developer 同身份，必须提交 `e2e/standards/spec` 结构化证据 |
| CI 合并单席位 | 所有任务共用一个 integration lease，要求候选 SHA 一致、三类验收全 pass |
| 有界修复 | review 失败退回开发，超过次数停在 blocked |
| 长周期可恢复 | JSON 原子替换 + 单写入锁 + lease ID + generation，防止过期/重复回写 |
| 外部执行结果不明 | 租约超时后 fail-closed 到 blocked，人工确认后显式 requeue，拒绝盲目重复写入 |

### 调度接口（需显式启用）

在已有 Bridge 的 `createBridge({ directory, enableDeliveryLedger: true })` 启动时开通；默认 **false**，不影响正式运行程序。继续复用现有本机 Bearer Token、Host/Origin 检查和 `X-Bridge-Request: 1`。

- `POST /api/delivery-runs` 创建 run（`{runId,tasks:[{id,dependsOn:[]}]}`）。
- `GET /api/delivery-runs`、`GET /api/delivery-runs/:id` 读取状态与事件。
- `POST /api/delivery-runs/:id/claim` 领取 `development/reviewer/integration`。
- `POST /api/delivery-runs/:id/development` 回写 SHA 与开发证据。
- `POST /api/delivery-runs/:id/review` 结构化独立验收。
- `POST /api/delivery-runs/:id/integration` 登记已通过的外部 CI/合并证据。
- `POST /api/delivery-runs/:id/retry` 明确人工确认后新一代重派。

状态文件：Bridge 数据目录下的 `delivery-ledger.json`，锁文件 `.lock`。锁残留需要人工核实，不主动抢锁。

### 验证

`cd tools/chatgpt-bridge && npm ci --ignore-scripts && node --test test/delivery-ledger.test.mjs test/delivery-api.test.mjs`

首次定向测试 **8/8 通过**（随后新增超长证据拒绝用例）。完整 Bridge `npm test` 须以实际测试日志为准。

## 3. 当前边界和下一阶段（不能冒充已完成）

- 这次接入的是**调度账本和安全的本机 HTTP 接口**，还没有绑定自动开发 Executor、真正的 GitHub PR/CI 校验器、云端跨设备一致性。回写的 CI/merge 证据目前只校验形状和 SHA 一致，不能代替从 GitHub 独立读取的真实验证，因此**不可启用自动生产合并**。
- **没有在正式燕子宿主打开特性开关，没有重启生产 Bridge，没有发布新安装包**。
- 文件锁只针对同一机器/文件系统，多个设备不能直接共用文件；下一步应由服务器持有唯一权威任务账本，并使用带 fencing token 的远程租约。
- 真正的后续步骤：增加具体 Executor 适配器（Antigravity CLI / Raccoon / Codex），独立读取 GitHub CI 和 Merge SHA，增加心跳续租、任务失败归因、垃圾 Worktree 自动清理，最后在受控设备上做长程真实验证。
- 生产系统不应原样移植上游使用的宽松 Codex 权限，执行环境及凭据须隔离。

## 4. 源码边界

本轮参考上游公开架构与测试语义，新建燕子适配组件，不更改上游仓库代码。燕子原有 ChatGPT 子 Agent 身份、回传队列和正式 Host/Origin 校验逻辑保持不变。
