# ChatGPT Gateway

服务器端 ChatGPT Plan HTTP 网关的可恢复源码副本。

## 当前部署

- 线上路径：`/opt/chatgpt-gateway/gateway.cjs`
- systemd：`chatgpt-gateway.service`
- 仅监听服务器本机 `127.0.0.1:8791`
- Nginx 对外暴露 `/chatgpt/`
- ChatGPT 凭据与网关 Token 不进入仓库，分别保存在服务器受限目录。

## 燕记

`POST /yanji/feed` 接收燕子移动端登录 Bearer Token，先通过燕子账号接口校验，再在服务器内部调用 ChatGPT。请求可包含：

- `cursor` / `limit`
- `seenTitles`
- `interests`
- `systemPrompt`
- `liked`
- `disliked`

手机 APK 不持有 ChatGPT 凭据。

## 部署

将本目录的 `gateway.cjs` 同步到服务器 `/opt/chatgpt-gateway/gateway.cjs`，运行：

```bash
node --check /opt/chatgpt-gateway/gateway.cjs
systemctl restart chatgpt-gateway.service
systemctl status chatgpt-gateway.service
```

不要把 `/etc/chatgpt-gateway/token` 或 `/var/lib/chatgpt-gateway/credentials.json` 提交到仓库。


## 只读工具桥接（2026-10-09）

新增受原网关 Bearer 鉴权保护的 POST /v1/tool-chat。
请求体示例：{"input":"请检查服务器状态和工作区目录"}，可选 model 必须是可用模型之一。

- 新增文件：tool-broker.cjs；测试：test/tool-broker.test.cjs。
- 以 Responses API 的 function_call / function_call_output 实现最多 4 轮、6 次调用的工具循环，store: false。
- 只开放 server_status_get（主机状态）和 server_files_list（服务工作区目录）；使用本机 127.0.0.1:8789 的燕子 Server Node。
- 需配置 YANZI_GATEWAY_TOOLS_ENABLED=1，线上 drop-in 位于 /etc/systemd/system/chatgpt-gateway.service.d/yanzi-tool-broker.conf。
- 没有调用 Codex CLI；没有开放任意 Shell、文件写入或 GitHub 凭证。
- 不改旧 /v1/chat、/v1/chat/completions、/yanji/feed 行为。
- 本地验证命令：node --check gateway.cjs && node --test test/tool-broker.test.cjs。
- 生产现有 gateway.cjs 含有本地副本没有的移动端流式改动，因此不要用本地 gateway.cjs 覆盖线上文件；需增量合并。
- 停用：修改 drop-in 为 YANZI_GATEWAY_TOOLS_ENABLED=0，systemctl daemon-reload && systemctl restart chatgpt-gateway。
- 线上旧文件备份：/opt/chatgpt-gateway/gateway.cjs.backup-20261009-toolbroker。
- 当前验收：模拟模型的 5 项工具调用单元测试通过，Server Node 真实只读调用成功；健康接口 HTTP 200，未授权调用 HTTP 401。真实模型经网关发起工具调用仍未完成验收。
- 本地回连仍未打通：yanzi-server-node /health 当前 cloudConfigured:false。应从燕子生成受限设备级授权，通过 Cloudflare 现有消息协议连接，不能把当前 ChatGPT 会话已安装的插件视为网关自动拥有。
