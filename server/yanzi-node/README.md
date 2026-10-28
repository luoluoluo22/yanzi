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

## 服务器向燕记手机发送消息

Cloudflare 的自定义防火墙要求 `User-Agent` 包含 `YanziClient`。Server Node 和发送 CLI 均携带 `YanziClient-Server/1.0`，不需要关闭防火墙。

首次授权由已登录的 Windows 燕子客户端执行；账号所有者令牌只用于签发，不离开 Windows：

```powershell
.\scripts\provision-yanzi-server-chat.ps1 -SshDestination "root@YOUR_SERVER"
```

要求预先信任 SSH 主机指纹，并设置密钥登录；远端执行 `sudo -n` 必须可用。只有绑定本服务器、K70 目标和 `device.presence/messages.receive/chat.send` 的设备 JWT 会通过加密 SSH 输入传到服务器。失败时脚本尽力撤销刚签发的凭据。每次授权最多有效 30 天，到期前需要重新执行。

授权成功后，可在 Ubuntu 用 stdin 发送一条测试文本（不会把正文或凭据写入进程命令行）：

```bash
printf '%s\n' '燕子服务器连接测试' | sudo bash /opt/yanzi/server/yanzi-node/scripts/send-message.sh
```

返回 `cloudStatus=pending` 表示云端已经接受但手机尚未 ACK；只有 `acked` 才表示指定手机确认接收。手机离线时云端消息仍可保留，系统通知另取决于 Android 推送服务配置。发送使用独立持久 outbox，`YANZI_MESSAGE_CLIENT_ID` 可设置为稳定 ID 以便失败重试时去重。

## 每天 10:00 新闻早报（2026-10-11）

服务器使用 `scripts/news_digest.py` 抓取中新社国内/国际/财经及 BBC 世界/科技 RSS，仅取过去 36 小时带发布时间的新闻，去重后按类别选取最多 9 条。ChatGPT Gateway（`gpt-6-sol`）只根据标题与简介生成导读；若不可用则降级为带原文链接的标题列表，新闻源不足 3 条则不发送，以免旧闻充数。

已部署到 Ubuntu 的 `/etc/systemd/system/yanzi-news-daily.service` 和 `.timer`，系统时区为 `Asia/Shanghai`，每天上午 10:00 运行；`Persistent=true` 可在重启后补执行错过的任务。同一日期的消息 ID 和正文持久化到 `/var/lib/yanzi-news`，重新运行不会重复发送，服务日志不包含令牌。

恢复新服务器时，在部署 Node 和设备级凭据后运行：

```bash
sudo install -d -m 0700 /var/lib/yanzi-news
sudo install -m 0644 infra/server/yanzi-news-daily.service /etc/systemd/system/
sudo install -m 0644 infra/server/yanzi-news-daily.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now yanzi-news-daily.timer
```

`sudo python3 server/yanzi-node/scripts/news_digest.py` 为只采集和预览模式；`--send --test` 发送隔离测试消息，`--send` 会按当天的幂等键正式推送。运行需访问 `/etc/chatgpt-gateway/token`（只读），消息发送仍走 `/etc/yanzi-server-node.env` 的受限设备令牌。令牌最多有效 30 天，过期前 7 天的新闻正文会提示重新授权；正式轮换须在已登录 Windows 电脑运行 `scripts/provision-yanzi-server-chat.ps1`。

## 数据目录

- 运行状态：`/var/lib/yanzi-server-node`
- 专用工作区：`/srv/yanzi-workspace`
- 环境变量：`/etc/yanzi-server-node.env`

运行状态允许丢失；工作区中的重要产物仍应同步到持久云存储或仓库，不能把本 VPS 当唯一副本。
