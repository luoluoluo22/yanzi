# Redmi K70 非微信 App 数据可获取性实测（2026-10-17）

## 目标

本轮按用户要求**只研究已经装在手机上的其他 App 实际有哪些数据能读取，暂不封装成小程序**。不进入微信，不打开私人聊天、支付、银行、邮箱或密码管理器。测试选取公开内容类 App：哔哩哔哩、小红书、抖音；计算器和燕子/PCAPdroid 为对照；网易云音乐只做一次有限界面尝试。

设备：Redmi K70（ADB e3e68545）；115 个第三方 App 包。技术通道：已授权 USB/ADB、Android `uiautomator dump`、PCAPdroid 2.0.2 应用过滤后短时 VPN 抓包。各捕获请求都经过 PCAPdroid 的应用内控制授权；不使用 Root、证书安装、TLS 解密或逆向私有数据库。

## 第一层：可见页面无障碍结构

| App | 包名 | 界面节点 | 含文字节点 | 可点击节点 | 普通 ADB 可读取 App 私有目录？ |
| --- | --- | ---: | ---: | ---: | --- |
| 小米计算器 | `com.miui.calculator` | 66 | 15 | 27 | 否 |
| 哔哩哔哩 | `tv.danmaku.bili` | 186 | 35 | 34 | 否 |
| 小红书 | `com.xingin.xhs` | 119 | 20 | 32 | 否 |
| 抖音 | `com.ss.android.ugc.aweme` | 256 | 18 | 35 | 否 |
| PCAPdroid | `com.emanuelef.remote_capture` | 44 | 8 | 9 | 否 |
| 燕子 | `cc.luoluoluo.yanzi.mobile` | 65 | 22 | 15 | 否 |
| 网易云音乐 | `com.netease.cloudmusic` | 初次启动未进前台；指定启动器后一次快照有 26 节点 | 5 | 6 | 否 |

**可以获得**：当前屏幕被 App 暴露给 Android 无障碍的文字、按钮描述、资源 ID、控件位置、滚动区域及可编辑输入框；还可获得包名、版本及应用声明的权限。

**不能据此推出**：首页上的每个视频标题、作者、粉丝数等都完整可读，或能遍历未展示的全部推荐/私有历史数据。含文字节点数量不等于可用的业务数据字段数。网易云音乐一次 26 节点仅为启动界面快照，不能当作播放列表已可读。

**沙箱限制**：普通 `adb shell run-as <release-package>` 无法进入这些 App 的私有 `/data/user/0/<package>` 数据目录；不假设 Root 或调试版 App 的特殊权限。

## 第二层：按 App 过滤的真实网络包

三次都通过 PCAPdroid 官方外部控制接口设置 `app_filter=<包名>`、PCAP 文件上限 512 KiB、每包最多 512 字节、每连接最多 12 包，并在用户授权后启动该 App 约 9 秒，然后停止 VPN。实际抓包文件均保存到电脑本地，**未提交到 Git**。

| 指标 | B 站 | 小红书 | 抖音 |
| --- | ---: | ---: | ---: |
| 包名 | `tv.danmaku.bili` | `com.xingin.xhs` | `com.ss.android.ugc.aweme` |
| PCAP 包数 | 817 | 770 | 629 |
| TCP 包数 | 479 | 630 | 396 |
| UDP 包数 | 336 | 138 | 233 |
| 可识别 DNS 查询域名（去重） | 25 | 43 | 91 |
| 可识别 TLS ClientHello SNI（去重） | 15 | 21 | 30 |
| 报告中发现的明文 HTTP 请求方法片段 | GET 14 | GET 7 / HEAD 2 | 0 |
| 本地原始 PCAP 字节数 | 144197 | 116425 | 129982 |

注意：这是单次短时、不同启动/缓存状态下的实验；不能据此比较长期耗流量、后台隐私风险、推荐算法效果或用户活跃程度。PCAPdroid 的按 App 归属是工具层判断，部分 Android 共享解析器/SDK 服务可能混在采样结果里。本轮三份 PCAP 全部在采样后通过 PCAPdroid 停止并确认 `tun0` 消失，手机恢复桌面。

### 哪些具体数据实际上可见？

- **B 站**：解析出 `api.bilibili.com`、`app.bilibili.com`、`api.live.bilibili.com` 等域名线索；更严格的 HTTP 首行分析识别到 11 个完整 GET 首行，均为 HTTPDNS `/resolve` 路径（查询参数不记录到研究文档）；还能辨别部分 `application/json`、HLS 播放清单和视频分片的响应 Content-Type。14 是宽松包级方法统计，11 是严格可解析 HTTP 首行的数量，两个口径不同。
- **小红书**：可识别 `rec.xiaohongshu.com`、`search.xiaohongshu.com`、`mall.xiaohongshu.com`、`live-feed.xiaohongshu.com`、图片 CDN 等服务域名；9 个可解析明文 HTTP 请求（7 GET、2 HEAD），可见部分响应 HTTP 状态及类型。**域名只是潜在接口入口，不是推荐/搜索响应正文。**
- **抖音**：可识别 `amemv.com`、`douyinvod.com`、`douyinpic.com`、`zijieapi.com` 等 API/视频 CDN/实验和监测服务域名；本次没有看到明文 HTTP 请求方法。不能据此推断抖音所有 API 均不可观测，只能说本次有限抓包未获得其明文方法。

### 当前仍然不可直接读取的内容

- 第三方 App 的私有数据库、离线缓存、未在屏幕显示的全部历史内容。
- 加密 HTTPS 的请求路径、Cookie、身份令牌、JSON 业务响应和服务端数据；即使抓到了 TLS SNI，也无法解密 TLS1.3。
- 完整的短视频文件、用户账号后台 API 或可无限拉取的内容列表。截断抓包不能代表完整媒体下载。

## 实验数据存放（仅电脑本地）

位于 `%LOCALAPPDATA%\OpenQuickHost\NetworkCaptures\K70\`：

- `app-surface-audit-20261017-125605/summary.json`：7 个 App 的结构化 UI/包信息摘要，不存原始 UI 文字或截图。
- `app-surface-audit-20261017-125605/findings.md`：早期摸底记录。
- `app-data-access-index.json`：汇总 7 个 App 的 UI 能力与已关联 PCAP 实验路径、关键计数，供后续直接读取；不包含原始 UI 文本或流量载荷。
- `pcap-limited-20261017-130257/capture.pcap` 和 `summary.json`：B 站。
- `pcap-limited-20261017-130618/capture.pcap` 和 `summary.json`：小红书。
- `pcap-limited-20261017-131058/capture.pcap` 和 `summary.json`：抖音。

PCAP 中可能包含明文 HTTP 数据或 DNS/访问行为。整个采样都不输出或提交查询参数、原始内容、会话凭证。**已核对本轮三份手机测试 PCAP 与电脑副本的 SHA-256 完全相同，再仅删除本轮创建的 `audit-*.pcap` 手机临时副本，电脑研究数据完整保留。**解析器只在**下次运行时**清理超过 7 天的同类实验目录，并非后台自动清理；不要误将其当作自动到期删除。

## 现阶段客观结论与下一步

**最可靠的两条路径**：（1）对当前可见界面做控件树/文字提取，不可见文字才用截图 OCR；（2）通过单 App 过滤的短时 PCAP 提取域名、协议、连接状态以及少量明文 HTTP 信息。手机端私有存储及加密服务端业务响应不可直接访问。

如果目的是分析公共内容，下一轮值得针对 B 站、小红书、抖音测试**可见首页卡片的标题/作者/互动数是否都有无障碍控件**，并通过只读滚动对照截图验证；然后再选一个正式公开的 API 测试接口进行协议级对照。不要仅凭抓包中的 host 名字猜测接口权限或模拟未知登录态。
