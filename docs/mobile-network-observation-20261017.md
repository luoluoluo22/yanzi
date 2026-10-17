# 燕子 K70 网络采样首轮实验（2026-10-17）

## 已落地工具

Windows 本机基于 ADB 的非 Root 网络连接元数据采样器：`scripts/yanzi-phone-network-sample.py`。不需在手机开启 VPN，不注入证书，不获取 TLS 明文，不上传原始数据。

复用命令（在仓库根目录）：

```powershell
& 'C:\Program Files\Python312\python.exe' .\scripts\yanzi-phone-network-sample.py --adb 'F:\SDK\platform-tools\adb.exe' --serial e3e68545 --seconds 30 --interval 3 --retention-days 7
```

本地会话位置：
`C:\Users\Administrator\AppData\Local\OpenQuickHost\NetworkCaptures\K70\probe-20261017-120954\`。

该会话包含 `connections.jsonl` 和 `summary.json`，仅保存在个人电脑，不纳入 Git。采样时长 18 秒，共 6 次快照，335 个不同远端连接端点，TCP 观测 3282 条、UDP 观测 30 条，总事件记录 377272 字节。连接观测不等于新建连接数、HTTP 请求数或消息数。仅提供远端地址、端口、TCP/UDP、连接状态和收发队列值；**不能获取**原始数据包、HTTPS 正文、微信消息/联系人或可靠的逐 App 归属。队列字节数不是收发流量总量。

每次运行会清理超过 7 天的本工具会话，单个会话限制在 2 MB；电脑关机或 USB 断开时不能采样。当前不是全天候手机后台监听。

## 真正的数据包监听工具

已从开源项目 `https://github.com/emanuele-f/PCAPdroid/releases/tag/v2.0.2` 下载 PCAPdroid 2.0.2 APK 并核验开发者签名及 SHA-256：

`eb437cbe10e2c7e332c06d7401d8f9036c3722747f541e6e231d1bd9ab6dbffc`

开发者签名证书 SHA-256：`ca59622be5fed99a072a4b03e9a1aa05ac26354d3ffb1a2c7208cdd755463456`。

PCAPdroid 安装尝试被 Android 拒绝，ADB 输出 `INSTALL_FAILED_USER_RESTRICTED: Install canceled by user`。因此 **尚未安装/启用**，不能声称已经开始抓取数据包。未尝试绕过用户安装决定或安卓 VPN 授权。

已经把官方 APK 复制到手机公共下载目录：`Download/PCAPdroid_v2.0.2.apk`。需在手机文件管理器手动安装，并在 PCAPdroid 中首次启动抓包时手动允许 Android 的 VPN 连接授权。它使用本机 `VpnService` 处理流量，不连接远程 VPN 服务器；一个 Android 用户同一时间通常只能启用一个 VPN，因此不能与其他 VPN 无根并行。

## 下一轮可观测性验证

待用户完成 PCAPdroid 安装及 VPN 授权后，先只过滤燕子 App 和一个用户主动测试的公开网页（例如 example.com），抓取短时间流量以验证应用归属、IP/端口、DNS/TLS 主机名、明文 HTTP 可见部分及 PCAP 文件导出。暂时不开 TLS 解密、根证书安装、代理劫持或手机全局长期抓取；任何需要读私人聊天消息的业务应使用独立的授权与数据采集路径。

本次网络实验没有将任何完整 URL、聊天内容、手机号码、账户凭证或流量负载加入 Git。