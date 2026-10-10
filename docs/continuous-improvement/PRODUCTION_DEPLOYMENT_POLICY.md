# 燕子 AI 长期任务：本机正式环境部署授权与验收契约（2026-10-09）

## 用户授权与范围

用户明确授权长期改进 AI Agent：可以在不再次逐轮申请许可的情况下，将自己验证通过的修复部署到这台 Windows 电脑正在使用的燕子正式环境，并进行真实运行验收；失败时可自动回滚。之前任务中的“不要自动正式发布”对本机受控部署不再生效。

【2026-10-09 新增授权】用户进一步允许 Agent 在通过完整本机正式环境验收后，自主向其现有 GitHub 燕子仓库发布经过审阅的 Windows 版本和安装包，无需每次再次申请许可。仅限明确属于当前燕子项目、与改动相关的版本化产物；公共发布不得包含未经审阅的主工作树改动、调试数据、用户资料或凭证。必须使用 scripts/ai-loop-publish-reviewed-release.ps1 的强制门禁，并提供真实 GitHub Release 链接及下载资产核验。禁止直接使用旧 scripts/release.ps1（内部 git add .），禁止从主仓库脏工作树直接打包发布。

仍未授权：向不相关仓库发布、公开小程序市场上架尚未测试的程序、修改远程生产服务/数据库 schema、上传或泄露凭证、支付/购物、删除用户数据、改写真实笔记/照片/聊天记录、强制重新登录 ChatGPT。以上动作须另行授权。

## 强制门禁

1. 一次只推进一项有基线和可重复测试的改动；不得为了消除日志而吞异常。
2. 部署前对照正式 Runtime 版本、Git HEAD、独立工作树及未提交变更，按文件或代码块移植，不得整目录覆盖主工作树，禁止 git add .、reset --hard、clean。不得用旧版闲置任务源码覆盖 v0.2.2 的重试功能。
3. 先运行隔离回归、Release 构建、受控 E2E；损坏/冲突/回滚测试只能用隔离假数据，禁止破坏正式同步状态。
4. 先备份，再更新；失败时优先恢复此次更改的部署文件与程序实例，不覆盖用户数据、任务历史或新版本 SyncState。
5. 上线后核对真实安装版本、进程/健康探针/后台心跳、非破坏性业务操作。必须等待真实反馈门槛，部署不等于业务效果验收。
6. Runtime 发布前查询其他正在运行的 Agent/Bridge 任务，避免中断用户当前操作；多个 Agent 不能并发部署同一目标。
7. 在没有可测试改动、实际样本或部署条件不满足时，停止反复 GPT 巡检，记录下一次最早允许检查时间和阻断原因。

## 小程序发布（提供已实现的受控入口）

入口：scripts/ai-loop-promote-extension.ps1。脚本仅针对已经安装的小程序的显式文件清单，不碰 ExtensionStorage。

- 默认仅预览；传入 -Apply 才执行。
- 自动执行隔离测试、排他锁、备份、停止目标小程序、复制指定文件、启动与健康/版本检查，失败则恢复原文件和之前的运行状态。
- 源码和正式版 manifest 的 ID 必须一致；不允许未经审阅的版本降级、路径穿越或复制敏感数据文件。
- 必须传入仓库或隔离工作树内的、无破坏性的 PowerShell 验证脚本。
- 示例：

    $deployArgs = @{
      ExtensionId = 'idle-chatgpt-tasks'
      SourceDirectory = 'F:\Desktop\kaifa\OpenQuickHost\tools\idle-chatgpt-tasks'
      Files = @('manifest.json', 'IdleTaskApp.cs')
      VerificationScript = 'F:\Desktop\kaifa\OpenQuickHost\scripts\test-idle-task-list-capability.ps1'
    }
    & 'F:\Desktop\kaifa\OpenQuickHost\scripts\ai-loop-promote-extension.ps1' @deployArgs
    & 'F:\Desktop\kaifa\OpenQuickHost\scripts\ai-loop-promote-extension.ps1' @deployArgs -Apply

## Runtime 宿主发布

- AI 的实验只在各自独立工作树做；必须把经过审阅的补丁移植至具有最新正式源代码的集成候选，避免部署较旧工作树丢掉近期其他人的修改。
- 串行构建并运行必要的 Yanzi.SyncVerification、Yanzi.RuntimeVerification。所有测试通过后才能升级正式 Runtime。
- 检查用户没有当前操作、Bridge 没有不能中断的 Job，再调用 scripts/install-shared-runtime.ps1 -SkipBuild -Activate -ProjectRoot <审阅后的集成目录>。它必须有版本快照、启动健康门禁和失败回滚。
- 验证已运行实际 Yanzi.dll 的版本、补丁标记与进程 PID、idleTrigger 心跳和已有小程序实例；任何关键回归都应立即回滚。
- 无法安全合并当前脏工作区、无法证明目标版本不退化、不能有效回滚时禁止强行部署，改记录 blocked 状态。这是门禁阻断，不是撤销了用户的正式部署授权。

## 每轮回传标准

- 代码：taskId、changeId、changedFiles、baseRevision、tests。
- 部署：deploymentAttempted、deploymentStatus（blocked/staged/installed/rolled_back）、installedVersion、backupLocation。
- 验收：smokePassed、realWorldSamples、metricBefore、metricAfter、rollbackTriggered、nextEligibleAt。
- 真实验收未达到样本门槛时写 awaiting-production-feedback，不得把技术测试通过冒充用户环境问题已解决。
- 更新对应 docs/continuous-improvement 下的文档，并交付明确可核查的运行日志、回滚位置、下一次检查时间。


## 公共 GitHub 版本发布（新增明确授权）

- 与“本机正式环境部署”是两个独立门禁：只有特定修复已在本机安装并通过规定的真实反馈/安全样本后，才允许把它作为公共版本发布。
- 现有 scripts/release.ps1 含 git add .，当前仓库还存在大量其他任务的未提交文件；**严禁自动运行旧脚本的 push 默认路径，严禁将脏主工作树直接打包发布**。
- 新的公共发布入口：scripts/ai-loop-publish-reviewed-release.ps1。必须传入独立 ai-loop/* Git 工作树、具体对照基线、精确 ApprovedFiles 白名单、明确版本、源版本匹配的 Windows Installer 资产、真实构建验收脚本、可审查更新说明。源码 Git 工作树必须 clean 且已提交，不得包含未审查的额外文件/凭证。旧 Release tag 不覆盖。
- 默认无副作用，只预演变更文件清单和资产；只有 -Apply 会重新验证，上传**审核过的单独发布分支**（不自动推送 main）、创建 GitHub vX.Y.Z Release、上传指定资产、再从 GitHub 查询校验。版本不得与已有 Release 冲突。
- 如果本机已有改动但尚未形成干净的隔离 release commit，状态是 blocked/awaiting-integration，不许直接发布。执行者应先完成最小安全合并、将确定范围的文件提交到隔离工作树、按相同源码重新构建安装包和回归测试。
- 推送任何公开内容前先检查 diff 的文件路径及新增敏感字符串，禁止将本机 token、.env、同步数据、任务历史、操作日志上传。禁止把未通过测试的代码和仅有实验室成功的候选当作最终发布。不要仅凭“用户量很小”跳过数据安全、版本回归或备份机制。
- 发布后需给出 GitHub Release URL、源码 commit、各文件 SHA-256 和实际资产清单；缺任一项则标识发布不完整并停止。
