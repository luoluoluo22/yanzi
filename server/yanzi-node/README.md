# 燕子 Server Node

这是燕子能力网络的 **可替换 Linux 执行节点**。它不是云端控制面的真相源，也不保存不可替代的业务数据。

架构约束：

1. 源码、部署脚本和恢复说明全部保存在 Git 仓库。
2. Cloudflare 继续承担账号、设备注册、消息路由和长期控制面；只有 Cloudflare 不适合的 Linux / 持久计算能力才放到本节点。
3. 服务器到期或损坏时，可以在新的 Ubuntu 主机重新克隆仓库并部署。
4. 不暴露任意 shell。对外只注册明确、可审计的高层 capability。
5. 文件能力只能访问 `YANZI_SERVER_WORKSPACE`，默认 `/srv/yanzi-workspace`。

## 第一批能力

- `server.status.get`：读取 CPU 数量、负载、内存、运行时间和工作区磁盘空间。
- `server.files.list`：列出服务器专用工作区。
- `server.files.hash`：计算工作区文件 SHA-256，单文件上限 1 GiB。
- `server.archive.create`：把工作区内文件/目录打包成 tar.gz，不提供任意命令执行。

这些能力的共同点是依赖真实 Linux 主机状态或持久文件系统，不适合直接塞进 Cloudflare Worker。

## 本机验证

```bash
cd server/yanzi-node
npm test
YANZI_SERVER_STATE_DIR=/tmp/yanzi-node-state \
YANZI_SERVER_WORKSPACE=/tmp/yanzi-node-workspace \
node src/index.mjs
curl http://127.0.0.1:8789/health
curl http://127.0.0.1:8789/capabilities
```

本地 HTTP 只监听 `127.0.0.1`，用于服务器自检，不是公网 API。

## 接入 Cloudflare

服务端不长期保存账号所有者 Token。推荐由已登录的燕子客户端为这台服务器创建 **设备级凭据**，至少授予：

- `device.presence`
- `messages.receive`
- 需要执行的 `capability.invoke:server.*` scope

然后把 Cloudflare 地址、账号标识和设备凭据写入服务器的 `/etc/yanzi-server-node.env`。不要提交 Secret 到 Git。

服务器注册为 `platform=server`，并通过设备的 `capabilityCatalog` 自动上报当前能力。收到 `capability.invoke` 后仍会二次校验消息中的 owner / device-grant 授权。

## 数据目录

- 运行状态：`/var/lib/yanzi-server-node`
- 专用工作区：`/srv/yanzi-workspace`
- 环境变量：`/etc/yanzi-server-node.env`

运行状态允许丢失；工作区中的重要产物仍应同步到持久云存储或仓库，不能把本 VPS 当唯一副本。
