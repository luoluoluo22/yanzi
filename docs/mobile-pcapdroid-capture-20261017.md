# K70 手机流量分析：PCAPdroid 真机首轮验收

时间：2026-10-17，手机 Redmi K70（23113RKC6C），PCAPdroid 2.0.2（Android versionCode 95）。本流程在本人授权设备上执行，采集到的网络数据只存电脑本地。

## 结果（已经执行成功）

1. 用户安装 PCAPdroid 并确认 Android 的 VPN 授权；系统创建 `tun0`，PCAPdroid 显示「停止」按钮和累计流量。证实其本地 VPN 流量采集已真实运行，不是仅安装了 App。
2. 使用 PCAPdroid 的「连接」页面核验应用归属、远端域名/IP、协议标签、端口、连接状态和流量大小。可区分微信、燕子、浏览器、相册以及系统服务等来源。
3. 采集模式为 **「无转储」**（不保存原始数据包）。通过「连接 → 更多选项 → 另存为 CSV」将本次连接信息保存到 `/sdcard/Download/PCAPdroid_17_10月_20_27_03.csv`，然后用 ADB 复制到电脑本地。
4. 导出的 CSV 共 **170 行、17 个字段、12 个应用或系统来源类别**；包含 72 条 HTTPS、41 条 DNS、23 条 TCP、17 条 HTTP、17 条 TLS。155 条记录的 Info 补充字段非空。按协议 + IP + 端口去重后有 **52** 个目的端点组合。这是实际导出结果，不是全部手机网络活动的完整历史。
5. 已运行本地 CSV 导入与汇总脚本，正确生成 JSON 报告，并实测 `--pull-latest` 从手机自动拉取已导出的 CSV。重复拉取同一份 CSV 时 SHA-256 去重成功，不继续堆积同样的数据。
6. 验收完成后通过 PCAPdroid 的「停止」按钮结束捕获，界面变为「就绪」，系统 `tun0` 接口消失，避免长期占用 VPN、增加耗电和无必要地监听私人通信。

## 已有字段与真实含义

17 个导出字段：

`IPProto, SrcIP, SrcPort, DstIp, DstPort, UID, App, PackageName, Proto, Status, Info, BytesSent, BytesRcvd, PktsSent, PktsRcvd, FirstSeen, LastSeen`

- **可以分析**：每个 App 的连接频率、远端 IP/端口、可见域名线索、TCP/UDP 类型、DNS/HTTP/TLS/HTTPS 协议标记、连接状态及字节/数据包计数。
- **不能证明**：HTTPS 解密、API 请求路径和响应正文、微信消息正文、联系人数据库。即使显示 `HTTP` 或 `HTTPS` 标签，也不意味着 CSV 导出了逐条请求/响应包。Info 字段可能包括敏感域名或 URL，按私人数据处理。
- **注意**：每行是一次连接记录，不等于一条消息、一次 API 请求或一次用户操作。按 IP/端口统计的目的端点数量也不等于唯一服务器数量。

## 本地数据存储

实际存储在 Windows 用户目录下，不在 Git 仓库或 Cloudflare：

```text
%LOCALAPPDATA%\OpenQuickHost\NetworkCaptures\K70\
  pcapdroid-20261017-203234\
    pcapdroid-connections.csv  # 手机原始连接元数据，含域名/端点，私有
    summary.json               # JSON 汇总：应用、协议、字节和记录数
  latest-pcapdroid-summary.json # 最新汇总路径，无需搜索采集目录
```

原始 CSV 31,452 字节，SHA-256 为
`a1905b3c20cfc76ab8139193bb7a7ad6b32d435a2d0d7953aadfb99bea4ebbfd`。

本地还有一次测试性导入目录 `pcapdroid-20261017-2027`，保存了相同原始 CSV。新导入脚本能识别并跳过进一步重复导入。保留策略：**每次运行时**自动检查并清理超过 7 天的本工具采集会话；不是 7 天后自动触发清理的后台计划。可设置 1–30 天。不会删除其他目录或未经本工具命名的私人文件。

## 下次复用

1. 在 K70 上打开 PCAPdroid，检查「无转储」和目标应用范围，点击「启动」并批准 Android VPN 请求（系统可能记住本次授权）。
2. 在想要测试的 App 中完成**少量明确的操作**；返回 PCAPdroid 的「连接」查看、必要时使用应用过滤。避免抓取支付、密码和其他用户通信内容。
3. 在「连接」页面右上角菜单选择「另存为 CSV」，使用手机系统文件保存器保存到 Download；之后可以点击「停止」结束 VPN 捕获。
4. 电脑连接 K70 后，在燕子项目仓库根目录运行：

```powershell
& 'C:\Program Files\Python312\python.exe' .\scripts\yanzi-pcapdroid-import.py --pull-latest --adb 'F:\SDK\platform-tools\adb.exe' --serial e3e68545 --retention-days 7
```

脚本只读取已经在手机文件管理器中导出的 PCAPdroid CSV，不偷偷启动监听、绕过 VPN 授权或解密 TLS。重复 CSV 按 SHA-256 去重。还可以使用 `--csv <Windows上已有CSV的路径>` 直接导入本地 CSV。

**未来进一步升级**：如需真正的 PCAP 原始数据包，必须在 PCAPdroid 中切换有限的转储模式，针对单个测试 App 做短时抓取并导出；本轮没有抓原始包，没有安装 TLS 证书。建议优先抓测试应用自身的流量，再考虑其他已获授权的应用。
