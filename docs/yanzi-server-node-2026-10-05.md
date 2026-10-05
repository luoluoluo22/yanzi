# 燕子远程服务器节点：第一阶段

日期：2026-10-05

## 定位

远程服务器不是燕子的云控制面，也不是唯一数据源。Cloudflare 继续优先承担认证、设备目录、消息路由和轻量持久化；VPS 只承接需要真实 Linux 环境、持久文件系统或后续长时间计算的能力。

服务器必须视为可替换节点。当前实例到期后，新服务器应能仅依靠 Git 仓库、环境变量和外部备份恢复。

## 第一阶段已实现

目录：`server/yanzi-node`

能力：

- `server.status.get`
- `server.files.list`
- `server.files.hash`
- `server.archive.create`

节点基于现有 `protocol/sdk/device-client.mjs` 接入燕子设备消息协议，平台标识为 `server`，能力通过 `capabilityCatalog` 动态上报。Cloudflare 无需增加新的 platform 枚举。

安全边界：

- 不提供任意 shell capability。
- 文件访问限制在 `/srv/yanzi-workspace`。
- 消息执行再次校验 `account-owner` 或精确的 `capability.invoke:<name>` scope。
- DeviceClient 在副作用前持久化执行状态，崩溃后不盲目重复执行。
- 本地诊断 API 只监听 `127.0.0.1`。

## 部署与恢复

systemd 单元：`infra/server/yanzi-server-node.service`

部署入口：

```bash
sudo REPO_ROOT=/opt/yanzi bash infra/server/deploy-yanzi-server-node.sh
```

服务器本身不应保存唯一源码。正式修改应提交 Git；重要工作区产物后续还需要同步到 R2、对象存储或其他持久位置。

## 下一阶段

1. 由已登录燕子客户端签发服务器专用 device credential，实现真实 Cloudflare 注册和远程 capability.invoke。
2. 增加任务型能力，例如 FFmpeg 媒体探测/转码和较长 Python 作业，但仍坚持高层能力白名单，不暴露任意 shell。
3. 任务产物接入 R2，服务器磁盘只做缓存和工作区。
4. 在燕子设备/能力目录中显示 server 节点在线状态、负载和能力。
