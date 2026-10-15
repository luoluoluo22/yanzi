# 燕子：Git 专有能力（2026-10-05）

## 已实现

只读：
- git.status
- git.log
- git.diff
- git.branch.list
- git.remote.list

写操作：
- git.fetch
- git.pull
- git.commit
- git.push

## 安全约束

- 不提供 reset --hard
- 不提供 rebase
- 不提供 force / force-with-lease push
- git.pull 固定使用 --ff-only
- Git 子进程通过 ProcessStartInfo.ArgumentList 传参，不拼接 Shell 命令
- 关闭交互式终端提示，避免后台挂起
- remote URL 返回能力层前移除 URI userinfo，避免认证信息泄露
- diff 有最大输出长度

## 本机验证

Git 版本：2.53.0.windows.1。

主仓库只做只读验收：
- status 能读 branch / upstream / ahead / behind / changes
- log 能结构化读取提交历史
- branch.list 能区分本地与远程分支
- remote.list 返回的 URL 已脱敏

写能力全部在 artifacts/git-capability-test 隔离仓库验收：
1. 创建本地 bare remote
2. git.commit(stageAll=true) 创建首个提交
3. git.push(setUpstream=true) 成功
4. 修改文件并再次 git.commit
5. 普通 git.push 成功
6. 第二工作副本 git.pull --ff-only
7. 文件内容从 v1 更新为 v2
8. git.fetch 成功

临时 bare remote 没有预先设置 symbolic HEAD 到 main，clone 因而产生“remote HEAD refers to nonexistent ref”测试警告；能力调用本身均成功。

Shared Runtime 已恢复为单 Runtime + 单 Shell，并加载全部 git.* 能力。
