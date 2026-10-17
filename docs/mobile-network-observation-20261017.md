# 燕子 K70 网络采样首轮实验（2026-10-17）

**后续结果：PCAPdroid 已在 K70 安装，获用户确认的 Android VPN 授权，实际运行采集，并成功导出 170 条连接记录；详见 [PCAPdroid 真机首轮验收](mobile-pcapdroid-capture-20261017.md)。本文件中的“尚未安装”仅是更早阶段的历史记录。**

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

早期 PCAPdroid 安装尝试被 Android 拒绝，ADB 输出 `INSTALL_FAILED_USER_RESTRICTED: Install canceled by user`。**后续用户已手动安装成功，并授予 VPN 权限；采集结果和结束状态见 [最新验收记录](mobile-pcapdroid-capture-20261017.md)。**未尝试绕过用户安装决定或安卓 VPN 授权。

已经把官方 APK 复制到手机公共下载目录：`Download/PCAPdroid_v2.0.2.apk`。需在手机文件管理器手动安装，并在 PCAPdroid 中首次启动抓包时手动允许 Android 的 VPN 连接授权。它使用本机 `VpnService` 处理流量，不连接远程 VPN 服务器；一个 Android 用户同一时间通常只能启用一个 VPN，因此不能与其他 VPN 无根并行。

## 下一轮可观测性验证

首轮已经在用户授权后完成「无转储」模式下的真实网络连接捕获和 CSV 导出（不包含 PCAP 原始数据包）。后续可考虑只过滤燕子 App 和一个用户主动测试的公开网页（例如 example.com），抓取短时间流量以验证应用归属、IP/端口、DNS/TLS 主机名与有限的 PCAP 文件导出。暂时不开 TLS 解密、根证书安装、代理劫持或手机全局长期抓取；任何需要读私人聊天消息的业务应使用独立的授权与数据采集路径。

本次网络实验没有将任何完整 URL、聊天内容、手机号码、账户凭证或流量负载加入 Git。