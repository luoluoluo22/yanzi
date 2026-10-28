# 燕子：Git 专有能力（2026-10-06）

## 已实现并注册

- git.status
- git.log
- git.diff
- git.branch.list
- git.remote.list
- git.fetch
- git.pull
- git.commit
- git.push

## 安全边界

- Git executable 使用本机真实 Git：2.53.0.windows.1。
- 所有参数通过 ProcessStartInfo.ArgumentList 传入，不拼接 shell 命令。
- GIT_TERMINAL_PROMPT=0。
- GCM_INTERACTIVE=Never。
- pull 固定使用 --ff-only，禁止能力自动制造 merge commit。
- push 不暴露 --force / --force-with-lease。
- remote/branch 名称限制为简单 token。
- remote URL 返回前移除 URI UserInfo 中的用户名、密码、Token。
- commit 默认只提交已暂存内容；stageAll=true 时显式执行 git add -A。

## 开发过程中发现的问题

Git Provider 源码已存在，但此前：
1. 没有接入 YanziBuiltinCapabilityRegistration；
2. 缺少 System.IO using，因此实际上从未成功作为正式能力完整编译；
3. 一次 Runtime 验收中只看到前 5 个只读能力，因此增加了启动期注册自检日志。

现在 Runtime 启动日志会记录：

[Capabilities] Git registered: ...

正式运行时已经确认 9/9 Git 能力全部出现。

## 隔离真实验收

测试目录：

artifacts/git-capability-test/

结构：

- remote.git：本地 bare remote
- work1：主测试工作区
- work2：制造远端变化的第二工作区

没有对 OpenQuickHost 主仓库做提交或推送实验。

### status

初始：
- branch=main
- upstream=origin/main
- ahead=0
- behind=0
- clean=true

### commit + push

通过 git.commit 创建提交：

git provider commit test

得到真实 commit hash。

随后 git.push 成功将 main 推送到本地 bare remote。

### log / branch / remote

均通过正式 Capability API 返回结构化结果，包括：
- commit hash / author / time / subject
- 当前分支
- upstream
- remote branch
- remote fetch/push URL

### fetch + pull

work2 制造一个新的远端提交后：

git.fetch 执行成功。

随后 git.status 正确返回：
- ahead=0
- behind=1

git.pull 执行：

git pull --ff-only origin main

成功 fast-forward。

最终：
- ahead=0
- behind=0
- clean=true

## 最终用途

燕子现在可以形成稳定的代码闭环：

读取仓库状态
-> 修改代码
-> git.diff
-> 构建/测试
-> git.commit
-> git.push

并且可以和 Visual Studio / VS Code / Raccoon 的开发能力组合，而不需要让 AI 每次临时拼 shell 命令。
