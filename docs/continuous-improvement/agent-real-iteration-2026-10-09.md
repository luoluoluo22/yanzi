# 燕子：父子 Agent 真实代码迭代实战验收（2026-10-09）

## 结论

**真实 ChatGPT 网页模型输出 → 本机结构化补丁执行器 → 不可修改的固定测试 → 校验和/回滚** 已经在隔离目录上成功验证，并且统一循环控制器又完成了一次**无需逐步手动接力的真实网页任务**。

不要将此等同于“生产燕子代码已经可以无人值守安全发布”。当前验证对象只包括 \`.tmp/yanzi-iteration-lab\` 下的允许文件，临时 ChatGPT 对话不是持久受管理父子对话，且后台浏览器偶发冻结/草稿继承，需要进一步治理。

## 任务结果

| 任务 | 真实模型回应 | 本机执行器 | 独立 Node 测试 | 结论 |
|---|---|---|---|---|
| T1 标题空白归一化 \`title.mjs\` | 1 次有效输出 | 写入源码，校验成功 | 通过 | 完成 |
| T2 秒数格式化 \`duration.mjs\` | 第 1 次格式被拒，收到反馈后第 2 次修正 | 先拒绝不安全语法，再写入正确源码 | 通过 | 完成真实纠错循环 |
| T3 标签去重 \`tags.mjs\` | 网页 Job 报回复超时，但原对话随后出现完整 JSON | 读取最后一组匹配的问答、校验任务和 SHA-256 后执行 | 通过 | 恢复成功 |
| T4 完全由程序编排的标题任务副本 | 第一个旧后台页无响应；新建的预检通过的临时页，一轮成功 | \`lab-iteration-runner.mjs\` 自行发送请求、解析、写入、验证 | 通过 | 自动化实测成功 |

原始三项固定测试合计 **3/3** 通过，自动运行副本也通过独立复测。Bridge 的自动化回归 **70/70** 通过。未改写任何 \`*.test.mjs\` 测试文件。

### 原始三项可信源码哈希

| 文件 | 初始 SHA-256 | 实际修复后 SHA-256 |
|---|---|---|
| \`title.mjs\` | \`8C612A4F8D1A567B0BC767CE0155F0C47F44C0192F6B226D9D61EC122A651DED\` | \`A28A0D7C4AFD609229267AABB9CF728E4F8AC7CF24526FB734D118BEBF2AE41A\` |
| \`duration.mjs\` | \`E79FFED474BD9E544CC934C930A02A673828FCCC3C39DC4B75A85F407B8B1B48\` | \`9828E1E428B39D21D3FAB138CF51100C883F919ED154D7E34B00C918D009D62D\` |
| \`tags.mjs\` | \`4FECDCA2C8271C39338DF1A202C990ABEE2CE06E6810E7E818EB9A73F8F06D91\` | \`8C3ADA4276259A706BE8AAF4147557ACF99F96C47D65EB283BEE7690D19373D5\` |

真实模型 Job：
- T1 \`b376e448-a4a8-4899-ab03-fe2dec5a13d8\`
- T2 初版 \`cc3bca9d-bdf0-4c18-b1bd-037367cc42ce\`，修正版 \`bd8f12e5-2c55-4724-8438-2796fae38f98\`
- T3 \`26c3f8cd-babe-42db-b500-51c727481862\`，原 Job 是 error，但晚到回复经只读恢复并通过验收
- T4 自动成功 Job \`3b15df07-5d65-40ba-a888-6336ad9dc4a6\`；此前对旧测试页的自动请求 \`c0850bfb-f98b-43c4-b6c5-6976e5c999fc\` 未成功且未动文件

## 已实装的可复用模块

\`tools/chatgpt-bridge/lab-patch-executor.mjs\`：
- 严格限制任务到 \`title\` / \`duration\` / \`tags\`，验证源码 SHA-256 及限定语法。
- 只修改隔离目录中的单个允许文件。预先保存回滚副本，失败恢复原始字节；禁止修改固定测试文件。
- 使用 Node 22 权限限制运行已固定的测试文件，限制进程执行时间和输出长度。测试通过后才报告 \`passed\`，即使 AI 自述成功也无效。
- 仍是实验性受限执行器，不应把词法限制或 Node 权限视为完备的系统级安全隔离；不直接运行于生产仓库。

\`tools/chatgpt-bridge/lab-iteration-runner.mjs\`：
- 支持持久子 Agent 模式，以及 \`--temporary-tab-id\` 指定已验证、已有登录态的临时 ChatGPT 测试标签页。
- 每轮检查渲染状态、登录/验证状态、是否生成中、是否存在草稿。
- 只接收绑定 \`task\` 和 \`expectedSha256\` 的 JSON 补丁，失败时最多反馈 1–2 次（最大轮次严格限制为 3）。
- 发生回复超时时，只读检查原会话是否存在**匹配轮次关联标记**的完整问答，若匹配且尚未生成中，可以接受晚到的模型回复。绝不因超时自动把原提示词重新发一次。
- 过程落盘到对应沙盒目录的 \`results/iteration-<runId>.json\`。

\`scripts/diagnose-yanzi-chatgpt-tab.ps1\` 和 Edge 浏览器助手 v0.5.55：
- 只读返回页面渲染状态、输入框、草稿存在与长度、生成中及验证状态（不暴露草稿正文）。
- 已实证主 Edge 中新建正式聊天会继承一份 **149 字**草稿，故默认保护，不覆盖。
- 独立 Edge 无头 Profile 能渲染本地 HTML，但访问 ChatGPT 会遇到站点验证；不复制主 Profile Cookie。

\`scripts/send-yanzi-chatgpt-task.ps1\`：
- 只在预检通过且输入框为空时提交。

\`scripts/apply-yanzi-lab-job.ps1\`：
- 从真实已完成 Job 取模型 JSON，应用补丁并打印独立执行器结果。

\`scripts/reconcile-yanzi-lab-job.ps1\`：
- 将不确定 Job 的 \`tabId\`、任务、原始源码哈希、最后一组用户与模型回复进行交叉验证；通过后应用补丁或返回幂等的 \`already_verified\`。
- T3 复测成功，源文件未被二次改写。

## 完全自动运行示例

需先有专门创建、登录且草稿为空的临时测试标签页：

\`\`\`powershell
node tools/chatgpt-bridge/lab-iteration-runner.mjs --task title \`
  --run <新的唯一执行 ID> --max-rounds 2 \`
  --temporary-tab-id <已预检标签页 ID> \`
  --sandbox .tmp/yanzi-iteration-lab/<专属子目录>
\`\`\`

T4 真实使用的沙盒：

\`.tmp/yanzi-iteration-lab/runner-live-20261009\`

成功的报告：

\`.tmp/yanzi-iteration-lab/runner-live-20261009/results/iteration-live-auto-title-fresh-20261009.json\`

真实记录：\`verified_pass\`，1 轮，\`webJobStatus=success\`，受限执行器 \`passed\`，独立测试通过，沙盒源码从 \`CB4B09...691F\` 更新到 \`1D3509...093C\`。实验主目录中已经成功的 \`title.mjs\` 没有因此被改写。

## 剩余工作

1. 将临时模式下的预检、会话管理、重试/恢复能力收敛为**专用浏览器测试 Profile + 显式一次登录**，解决站点挑战与正式聊天草稿继承。持续受管理的父 Agent 最终还需要正式会话 URL，而不是临时聊天。
2. 加入后台标签生命周期及渲染进程健康检查，识别长时间冻结并安全释放仅属于燕子的空白标签；当前有一个旧实验标签页关闭操作因渲染无响应卡住，不能强制终止整个 Edge。
3. 扩大到真实燕子仓库之前，需要 Git worktree、静态与单元回归、权限隔离、发布审批与回滚门禁。当前不是生产级任意代码执行沙盒。
4. 为持久父 Agent 提供由质量测试产生的可信审计事件，确保父 Agent 看的是实际测试证据，而非子 Agent 自述。

## 验收纪律

代码修改的通过与否以**独立运行的固定测试和源码哈希**为准。Bridge 的 \`success\` 只代表一次网页任务被成功执行和回复；与源码修改的事实分别记录。网页 \`timeout/error\` 若经过只读校验找回迟到的回复，仍保留原 Job 的错误状态，不伪造其成功。

正式 Windows 燕子宿主、小程序 UI、用户其他浏览器标签均不属于本次自动修改范围；实验不会自动部署正式版本。
