# 燕子账号绑定 P2P 独立传输模块

## 用户体验

两端均登录同一燕子账号时，直接从账号设备目录选择目标设备，无独立注册、二维码、手动网络名、IP 地址或 `pairing.json` 编辑步骤。独立服务只是当前设备的后台进程，不是新设备。

小程序可在 `manifest.json` 的 `capabilities.requires` 声明 `files.p2p.connect` 或 `files.p2p.status`。首次需要能力时，宿主自动发现模块、安装经过验证的依赖与固定版本服务。文件发布能力可显式传 `targetDeviceId`；多台在线电脑且目标不明确时返回 `p2p_target_required_when_multiple_or_no_online_desktops`，绝不任选一台。

## 信任根及独立组网

- 账号身份：`SyncSessionStore` 中有效的当前登录身份；设备：`DeviceIdentityStore` 中的电脑设备 ID。
- 服务端已有 `/v1/me/devices` 和 `/v1/me/devices/lan-links`，为同账号设备签发期限受限、互相一致的 LAN 连接授权。客户端仅接受当前账号的 `account.owner` 授权，再通过 Windows DPAPI 本地解密。
- P2P 网络上下文：`yanzi.p2p.account.v1\n{accountId}\n{sortedDeviceIdA}\n{sortedDeviceIdB}`。基于 32 字节已有授权对密钥分别计算 HMAC-SHA256（`network` 与 `secret` 独立上下文）。网络名、网络密钥仅在本机生成，不经聊天、日志或云端明文分发。
- 虚拟地址按有序设备 ID 和派生网络字节决定。EasyTier 只使用短主机名 `yanzi-` + SHA-256(deviceId) 前 22 位十六进制，解决 EasyTier 设备主机名长度限制。
- 传输服务首次安装使用本机生成的 `%LOCALAPPDATA%\YanziTransferService\pairing.json`，这是**机器内部配置文件，不是用户配对操作**；同时创建 ACL 限权的 `private-network.secret`。已安装/正在传输的 Worker 永不被覆盖或重启。
- 设备之间使用原有已认证 `capability.invoke` 消息，远端按需启用模块并连接。只允许同账号、在线、授权明确的目标；首次链接失败不降级至明文、公共文件转存或未认证中继。

## 依赖与安装

独立模块固定仓库提交，不从可变 main 执行未审核脚本。宿主依赖管理器确保 Git/Node；EasyTier 下载官方固定 2.6.4 归档，校验 SHA-256 后使用文件白名单解压。组网协调节点从受信任模块清单读取，可以保留已配置的受信任节点。密钥只通过进程私有环境变量交给 EasyTier，不输出在命令行/日志中。

## 测试及已知限制

- 双端同账号 DPAPI 连接对密钥 SHA-256 摘要一致；双方独立派生同一网络名/秘钥、镜像虚拟地址。
- 单元验证 `--account-p2p` 通过（账号隔离、密钥隔离、重复稳定性、短主机名）。
- 在家里现存已安装的服务上执行 `--account-p2p-readonly <目标设备ID>`，同账号授权确认后保持原独立 Worker 不变。
- 隔离组网（单独监听端口）确实互相发现，但因当前网络/NAT 情况未协商出 UDP P2P，观测 `relay(2)`；这是**未通过完整数据直连验收**，独立服务的零中继文件策略会拒绝使用该路由。
- 退出账号、设备被撤销授权、密钥轮换之后仍在运行的 Worker 的即时撤权/密钥轮换机制需要单独做端到端验收。不能以账号已登录替代底层加密授权。

旧服务迁移保持兼容：不改变已经运行的服务进程/任务，不能无损迁移的对端切换明确失败，而不是将活动任务转接到错误设备。