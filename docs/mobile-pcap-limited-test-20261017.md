# K70 · 燕子单应用限量 PCAP 采集（2026-10-17）

## 目标与安全边界

只观察用户已授权的自研燕子 Android App（包名 `cc.luoluoluo.yanzi.mobile`）的网络请求。**不涉及微信、联系人、聊天、支付、密码和其他 App 的包数据**。此实验不尝试绕过 HTTPS/TLS，不注入证书，不启用中间人解密，没有网络数据上传至外部服务器。

已安装 PCAPdroid 2.0.2，并在上一轮完成 VPN 手动授权、元数据 CSV 导出及本地持久化（170 条记录）。与之前的全应用元数据记录不同，本次需限定 `app_filter` 后才能写入原始 PCAP 文件。

## PCAPdroid 官方 API

PCAPdroid 官方文档：<https://github.com/emanuele-f/PCAPdroid/blob/master/docs/app_api.md>

官方 `CaptureCtrl` 可经 ADB Intent 启动采样，并指定 `app_filter`、`pcap_dump_mode`、`pcap_name`、`snaplen`、`max_pkts_per_flow`、`max_dump_size`、`tls_decryption`。

本次已向 PCAPdroid 发出如下**受限采集请求**。PCAPdroid 曾弹出独立控制授权，随后授权生效，`tun0` 建立且 PCAP 文件开始写入。已完成原始 PCAP 真机采集、停止和离线解析：

```powershell
$adb = 'F:\SDK\platform-tools\adb.exe'
& $adb -s e3e68545 shell am start `
  -n com.emanuelef.remote_capture/.activities.CaptureCtrl `
  -e action start `
  -e pcap_dump_mode pcap_file `
  -e pcap_name yanzi-app-20261017-limited.pcap `
  -e app_filter cc.luoluoluo.yanzi.mobile `
  --ei max_dump_size 1048576 `
  --ei snaplen 768 `
  --ei max_pkts_per_flow 16 `
  --ez full_payload false `
  --ez tls_decryption false
```

限制：PCAP 文件最大约 1 MiB、每条连接最多截取 16 个包、每包最多 768 字节。用于网络握手/域名/协议识别；它不等于完整流量镜像，也无法解密 HTTPS。

PCAPdroid 的外部控制授权弹窗文本为“一个应用程序想要捕获你的设备流量”，提供「拒绝」和「允许」。**此权限是用户亲自确认后才生效的**，没有通过 ADB 强制授予长期 API Key；不能把先前 Android VPN 授权当成外部控制授权。之后需要新的授权时，仍必须由用户完成系统/应用界面授权。

## 工具准备和本地分析

已新增 `scripts/yanzi-pcap-limited-analyze.py`：不依赖第三方 Python 抓包库，针对已经导出的经典 PCAP 文件进行离线解析，**已在真实 K70 PCAP 上通过验收**。支持 RAW IPv4、IPv6、以太网和 Linux cooked 链路类型，并统计：

- IPv4 / IPv6、TCP / UDP 观测数，目的端口数量，独立目的连接端点数。
- 可见的 UDP/53 DNS 查询名，以及单个 TCP 包中能提取到的 TLS ClientHello SNI。
- 若出现明文 HTTP，统计方法数量，但**不提取/打印路径和正文**。
- 这些字段属于**最佳努力识别**，TLS 分片、QUIC、ECH、加密 DNS 等情况下可能无法获取主机名。

原始 PCAP、含域名的 JSON 摘要保存在电脑私有目录：
`%LOCALAPPDATA%\OpenQuickHost\NetworkCaptures\K70\pcap-limited-<时间戳>\`，
不上传到 Git 或云端。单份输入限制 2 MiB，按需重复运行时会清理超过 7 天的本工具采集会话。

下载已经经 PCAPdroid 许可生成的 PCAP 并解析：

```powershell
& 'C:\Program Files\Python312\python.exe' `
  .\scripts\yanzi-pcap-limited-analyze.py `
  --pull --adb 'F:\SDK\platform-tools\adb.exe' `
  --serial e3e68545 --retention-days 7
```

也支持 `--pcap <已有本地 PCAP 路径>` 分析，无需手机在线。测试代码位于 `scripts/tests/test_pcap_limited.py`；已通过三项离线模拟单元测试，另外用 K70 实际导出的原始 PCAP 完成一次完整端到端验收；重复拉取相同文件时不会生成冗余目录。

## 本轮已完成的真机验收（2026-10-17）

- 通过 PCAPdroid 官方 API 对 `cc.luoluoluo.yanzi.mobile` 发起过滤采集，单次 PCAP 文件上限约 1 MiB，单包截取上限 768 字节，每连接最多 16 包，不启用 TLS 解密。
- PCAPdroid 在外部控制授权生效后实际开始采集，Android 建立 `tun0`，原始文件 `/sdcard/Download/PCAPdroid/yanzi-app-20261017-limited.pcap` 真实写入 **11,329 字节**。
- 通过 PCAPdroid 界面的「停止」结束采集，确认界面出现「启动/就绪」且 `tun0` 已消失。
- Windows 通过 ADB 拉取并解析：**58 个数据包**，其中 TCP **48**、UDP **10**，全部能解析到 IPv4 层，没有解析失败的记录。
- 单包分析提取到 **1 个 DNS 查询域名**及 **1 个 TLS ClientHello SNI**，均为自研同步服务域名 `sync.luoluoluo.cc.cd`。未检测到明文 HTTP 方法。
- 解析器还提供 **SHA-256 重复导入保护**：再次从手机拉取同一份 PCAP 时返回 `PCAP_ALREADY_IMPORTED=True`，保留原始会话，不新增重复文件（已真机验证）。
- 原始 PCAP 和域名摘要只保存在 `%LOCALAPPDATA%\\OpenQuickHost\\NetworkCaptures\\K70\\pcap-limited-20261017-124640\\`；绝不纳入 Git 或云端同步。保留期限 7 天，但仅在再次执行清理命令/解析器时会清理超期会话。
- 注意：解析统计包含两个方向的数据包，因此目的端口计数不能直接等同于发向远程服务器的请求数；DNS/SNI 提取属于最佳努力解析，不代表获得 HTTPS URL、正文或应用内部账号信息。

## 以后重复验收的操作

1. PCAPdroid 若再次出现控制授权，则由用户确认「允许」（此前本轮已经得到授权）。
2. 确认 PCAPdroid 处于抓包状态、`tun0` 存在、过滤目标为燕子 App，文件最大为约 1 MiB。
3. 只触发燕子 App 的已知非敏感网络请求（如同步心跳/状态），采样 15–20 秒。
4. 用 PCAPdroid「停止」结束 VPN，确认 `tun0` 消失。
5. 只从 `Download/PCAPdroid/yanzi-app-20261017-limited.pcap` 拉取限量包，并运行离线解析器。必须报告实际包数、可见域名数量和数据限制；不能把“已申请抓包”表述成“已经捕获成功”。

### 当前已完成与待改进

- **已通过**：官方 API 指定自研 App 包名及抓包限制、用户授权、真实 PCAP 落盘、ADB 拉取、TCP/UDP/DNS/SNI 解析、单元测试、采集停止以及 VPN 接口消失。
- **未覆盖**：TLS 解密（故意不启用）、HTTPS 请求正文、微信联系人/聊天、系统后台事件触发式自动采样及长期容量统计。
- **下阶段建议**：将限定时长采集、确认抓包目标、自动结束会话、拉取与解析整合成一个受用户控制的简短命令，但仍必须保留 PCAPdroid 自身的控制授权。
