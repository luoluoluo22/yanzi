# 博客园专用能力（2026-10-09）

## 目标

把文章工作流收敛为燕子宿主级能力，不再依赖模拟浏览器后台点击。固定连接博客园官方 HTTPS 开放接口，不允许能力调用者指定网络目标，避免 PAT 随意转发。

官方接口说明：https://www.cnblogs.com/cmt/articles/19246558

## 已实现的能力

| 名称 | 权限 | 用途 |
|---|---|---|
| `cnblogs.status` | `blog.cnblogs.read` | 确认加密存储内是否已有凭证，不返回值 |
| `cnblogs.review` | `blog.cnblogs.read` | 查询指定 postId 的远端审核状态 |
| `cnblogs.draft.save` | `blog.cnblogs.draft` | 保存 Markdown 本机加密草稿 |
| `cnblogs.draft.list` | `blog.cnblogs.draft` | 列本机草稿的标题、编号、时间 |
| `cnblogs.draft.get` | `blog.cnblogs.draft` | 读取一篇本机草稿 |
| `cnblogs.post.publish` | `blog.cnblogs.publish` | 发表 Markdown 并返回 postId/postUrl |
| `cnblogs.draft.publish` | `blog.cnblogs.draft` + `blog.cnblogs.publish` | 将本机草稿正式发表 |

例如：

```json
{"name":"cnblogs.draft.save","payload":{"title":"文章标题","body":"# 正文","isAigc":true}}
```

```json
{"name":"cnblogs.post.publish","payload":{"title":"文章标题","body":"# 正文","isAigc":true,"confirmPublish":true,"requestId":"blog-20261009-unique001"}}
```

```json
{"name":"cnblogs.review","payload":{"postId":23236265}}
```

正式发布必须显式 `confirmPublish: true`，且调用者需获得 `blog.cnblogs.publish` 权限。能力元数据同时标记 `RequiresConfirmation`，但 **SDK 当前不能将其等同于系统级弹窗确认**，宿主接入时还应执行真实用户确认策略。用唯一 `requestId` 对相同内容的重试进行去重，网络结果不明时保留 pending 并拒绝盲目重发，用户需要先核对博客园页面。

## 凭证管理

环境变量名仍为 `CNBLOGS_TOKEN`，存储由 `AppEnvironmentVariableStore` 管理。其磁盘值由 Windows DPAPI `CurrentUser` 加密保存在 `environment-variables.dat`。宿主中的 `AccountEnvironmentSecretVault` 将环境变量的密文纳入账号级 E2EE 同步；此流程能否跨设备恢复，需要在登录新设备后实测，不能仅凭代码推断已完成云端同步。

`CNBLOGS_TOKEN` 被列入 `ProviderOnlySecrets`：不会被 `ScriptExtensionRunner` 当作普通环境变量注入其他小程序，`GetEnvironmentNames` 也不列出该名字。网络调用只能通过固定博客园域名发起；禁用 HTTP 302 认证重定向；返回结果与能力日志不包含 PAT。

**安全限制**：同一个 Windows 用户下运行的未隔离第三方代码仍可能拥有文件权限或进程内反射能力。上述过滤并不构成操作系统强制隔离边界。未来应进一步隔离小程序进程、限制文件权限和宿主公开的读取 API。

从旧 `.env` 导入：构建验证项目后，可在已授权的本地维护环境中执行 `--cnblogs-import`。此命令仅读取项目根目录预设的 `.env` 条目，调用 `AppEnvironmentVariableStore.Save` 并检查加密回读，无需通过参数、控制台或日志传递凭证。不要把 `.env` 纳入 Git。

## 草稿存储

官方当前发布文档只确认 `POST /openapi/v1/posts` 和 `POST /openapi/v1/posts/reviewStatus:check`。没有把未验证的“云端草稿”当作已支持的能力。当前草稿为本机 DPAPI 加密的 `cnblogs-drafts.dat`，发布记录为本机 DPAPI 加密的 `cnblogs-publish-ledger.dat`。这两类文件 **目前不参与账号同步**，也不能在另一台电脑上直接解密。发布后保留草稿作为本地备份。

## 构建与验收

```powershell
dotnet build src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj -c Release -p:SkipStopRunningApp=true --no-restore -v:q -clp:ErrorsOnly
dotnet run --no-build -c Release --project src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj -- --cnblogs
# 账号凭证在受保护存储可用时可执行如下只读联网验收：
dotnet run --no-build -c Release --project src/Yanzi.CapabilityVerification/Yanzi.CapabilityVerification.csproj -- --cnblogs-live-read 23236265
```

测试包括：独立权限拒绝、伪造授权无效、加密持久化、普通小程序环境隔离、草稿 CRUD、API 认证 Header、Markdown/AIGC 字段、远程审核状态、重复 requestId 去重及内容变更拒绝。

发布验证测试使用模拟传输，不会直接向博客园发表垃圾测试文章。真实 API 仅在明确授权、有文章内容和正式确认的情况下调用。

## 发布维护注意

工作区存在同时开发的其他功能，不能直接用本次提交覆盖整个分支。应先检查 `git status`，按项目 `scripts/install-shared-runtime.ps1` 的隔离快照方式先进行构建和只读验收。正式激活前需要确认其他能力注册无异常，并避免中断正在运行的任务。
