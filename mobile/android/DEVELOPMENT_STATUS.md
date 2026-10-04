## 2026-10-04：Android 0.2.48 电脑 Tab 与发送导航

- 电脑 Tab 合并原左上角连接状态，四态分别显示局域网、重连中、云端、离线。选中后的再次点击进入原连接详情/重新检测；移除顶部按钮与“远程操作”。
- 文本、照片、文件发送期间隐藏底部导航，finally 恢复；重叠发送必须全部结束才恢复。AI 回复结束/取消也恢复。
- Release 与 Dev 默认都采用精简包，离线唤醒模型与原生库暂不打包，普通系统语音输入保留；-PYANZI_BUNDLE_WAKE_MODEL=true 可以恢复。
- 一加 Dev 0.2.48-dev 原生仪表测试通过连接四态、选中/重复点击、详情、重新检测入口、并发发送、AI 停止、实际消息发送；电脑确实收到了测试消息。
- 正式包 0.2.48/code 48，7,229,629 字节；签名与旧正式版一致。发布记录：docs/mobile-navigation-release-2026-10-04.md。

## 2026-10-04：Dev 404、本机移除和精简包

- 404 已实机定位：Dev 的 sourceDeviceId 登记被用户删除，服务器返回 device_not_found。
- APP 前台进入检查登记；确认本机移除后清除 token/password，提示重新登录。网络暂时失败不会退出账号。后台不可复活已删除登记。
- 只有显式账号登录会重新登记；使用 Android ANDROID_ID 与账号邮箱的哈希生成稳定身份，成功后清理本次安装的旧 ID。旧的已登录会话到下次登录才迁移，不能按手机型号自动合并历史记录。
- Dev/debug 默认不打包 Vosk 离线唤醒模型和 libvosk.so。可用 -PYANZI_BUNDLE_WAKE_MODEL=true 恢复；release 保留。普通语音输入保留。
- 新增 desktop-execution 仪表测试套件，显式 allowPhysicalDev=true，只允许执行无输入的 taskbar-calendar，覆盖实际 APP 路由、公网路由、临时设备移除拒绝、隔离登录凭据清除。
- 最终实机四项全部通过：APP/局域网执行日历、公网执行日历、移除登记拒绝后台恢复、清除登录 token/password。最终精简 Dev APK 9,064,046 字节，已覆盖安装一加真机，正式版未改。详见 docs/mobile-execution-routing-release-2026-10-04.md。

# 燕子 Android / 手机端开发状态

> 更新时间：2026-10-03
> 适用范围：`mobile/android`、账号 Object Sync、Windows ↔ Android 跨端小程序数据、真实手机 Dev 开发闭环。  
> 本文记录“当前真实状态”，规则类约束仍以 `.agents/AGENTS.md` 为准。

---

## 2026-10-04 Android 0.2.47 电脑执行路由与手机页

已发布 0.2.47/code 47。执行电脑小程序优先指定已连接 LAN 电脑或唯一在线电脑，避免历史登记引发 409；LAN 即时执行回执供现有结果查询使用，完成后不再提交第二条云端执行。底部“开发”改为“手机”，电脑小程序列表移除应用中心，手机页保留入口。Dev/Release、Worker 28 项和一加隔离真机 85 项协议及后台消息/同步回归通过，正式包与账号配置保留。main 提交 c7b5227 的 Worker 自动部署成功，GitHub Release、R2 更新清单、完整公开 APK 哈希均确认；真实账号原始 409 场景等待用户升级后复测。见 [发布验证记录](../../docs/mobile-execution-routing-release-2026-10-04.md)。

## 2026-10-03 Android 0.2.46 更新反馈与节点检测

新增 BusyButton，覆盖检查更新、应用中心刷新/获取/下载、连接重新检测与位置上报按钮；操作时禁用变灰并显示处理中或转圈，结束恢复。更新检查和更新下载分别只允许一个在途任务，缓存 APK 校验移到后台。云端 APK 也能构造同版本 GitHub/ghfast/ddlc 候选，实际下载前以 32 KiB 样本并行检测（总预算 7 秒），按测量选择可用路线并显示真实节点名称；更新清单优先系统/VPN 网络。保留目标版本、包名、证书与 SHA256 校验。电脑及一加/模拟器确认云端、GitHub、ghfast 可用，ddlc 限流；kkgithub 证书不匹配已移除。29 项 Java 场景、两设备各 11 项原生断言、正式包升级数据保留通过；红米外出网络仍待用户复测。详见 [验证记录](../../docs/mobile-update-feedback-release-2026-10-03.md)。

## 2026-10-03 Android 0.2.45 局域网恢复

局域网短暂失败保留已知电脑地址并显示重连中，连续探测失败才回退云端；已知地址恢复不依赖广播退避。Wi-Fi/账号变化丢弃旧健康状态，连接检测不重叠；电脑发现拒绝其他手机。17 项状态规则、2 项原生断线故障场景、最终正式包升级保留和一加 Dev 双向加密传输通过；红米现场仍待用户复测。范围、限度和产物见 [验证记录](../../docs/mobile-lan-recovery-release-2026-10-03.md)。

## 2026-10-03 Android 0.2.44 网络修复

应用中心目录/下载与宿主小程序云存储优先使用系统网络（包含 VPN），安全读取在传输失败时最多切换一次物理网络，写入不自动重放。笔记使用宿主 HostStorage，更新主 APP 即获得修复。Dev/Release 构建、Java 故障注入、正式包模拟器升级数据保留与最新 Dev 小程序实际云对象读写通过。红米 K70 的用户现场 5G＋VPN 仍待更新后验收，详情见 [发布验证记录](../../docs/mobile-vpn-network-release-2026-10-03.md)。

## 2026-10-03 位置首版与后台请求优化

新增通用 `device.environment` 原生桥接、独立设置 Activity、LocationManager 限时采集与 15 分钟持久 JobScheduler。用户小程序 `yanzi-location` 在用户 Extensions 目录开发，账号定义通过现有 Object Sync 单对象/CAS 和索引同步；主项目没有该 ID 的业务分支。家的坐标与原生任务设置仅本机保存、按账号/设备/小程序隔离并排除系统备份。默认不采集、不共享精确坐标；前后台权限分别说明。最新快照使用单独 D1 表，不进入带历史的同步对象，默认 TTL 24 小时，支持停止 tombstone 和旧序号保护。

手机连接正常时改为实时事件优先：心跳 60 秒、WebSocket ping 60 秒、数据同步和收件补偿 5 分钟；断线数据同步 60 秒、收件 5 秒；重连指数退避至 60 秒。外部授权申请增加实时唤醒，前台/后台补偿分别 15/60 秒。位置上报独立调度，连接恢复只重试待发、不触发定位。账号切换、定义删除和权限声明撤销均有任务边界处理。

验证：41 项 Node 场景、13 项 Java 规则/调度、13 项模拟器原生集成、隔离 Worker/D1 实际 API 测试通过；Windows 构建、同步设置和 14 项 Local API 边界验证通过。实体一加 Dev 0.2.43 安装与正常小程序入口通过，生产 0.2.42 版本及路径不变。未在实体机静默授予定位；真机定位精度、重启、息屏与 24 小时电量/调度仍需实际验收，不以亮屏或请求频率估算替代。

范围、接口、测试脚本、最终真机回归与自动构建部署状态见 [实现记录](../../docs/mobile-location-implementation-2026-10-03.md)。这节不是正式 APK 发布说明。

## 2026-10-03 工程基础与通信回归

工程评估与完整测试记录见 [工程基础评估](../../docs/engineering-foundation-review-2026-10-03.md)。新增 `MobileApiTransport` / `HttpResponseBody`，前后台共用有上限的 UTF-8 响应读取，空错误流安全处理；云端与加密 LAN 由调用方明确选择，修复私网云 Worker 被误判为 LAN 后出现 `pair_required`。VPN 兜底使用安全请求列表，PUT/认证写入不自动重放，超限响应和线程中断不重试。

桌面消息桥与 HTTP 传输分文件；Worker 设备参数/HTTP 错误/JSON 读取分模块；SDK 补齐并发收件、非法期限、无接收器和非 JSON 网关响应处理。`scripts/test-communication-foundation.ps1` 已接入 `dev-smoke.ps1`。

验证：33 项 Node 场景、Java 响应/重试、桌面网络恢复、两轮各 85 项多设备协议、实际 SDK 文件能力、附件/账号聊天、两种云对象权威模式，以及完整模拟器跨端回归通过。完整模拟器回归后清空测试账号并完成 clean smoke。真机生产包未操作；云端未部署。本节不表示大文件已全部完成服务化，也不替代真机后台与 macOS 验收。

## 2026-10-03 更新通道修复与正式发布

燕子 0.2.36 / 笔记 0.1.4（code 5）。更新检测对连接重置、响应读取失败、HTTP 错误和无效 JSON 增加最多四次请求：公网清单直连/系统路径，再 GitHub 直连/系统路径；每次关闭连接并限制清单 2 MiB。公网 APK 下载失败后换系统路径重试，安装前继续验证包名、版本、签名和已知 SHA-256。7 项故障注入测试通过，宿主 Dev/Release、笔记 Release/lintDev 通过。正式包证书与旧版 0.2.32 及线上笔记一致。

本次检查并修复受限 LAN 配对权限升级、跨扩展接续状态访问、删除设备路由、撤销凭据实时连接、终态回调和永久拒绝队列；桌面/同步/能力/LAN/传输/Outbox、Worker 12 项、SDK 9 项、本地协议 85 项两轮与模拟器笔记 5 项及后台事件往返通过。主应用完整 lintDev 仍有 152 项已有代码错误，不声称全量 Lint 通过。用户附件与 UI 备份保留本机但不提交。发布通过 GitHub Release 和 GitHub CI，不手动部署 Worker；正式渠道已发布并独立下载核验：Android 0.2.36、笔记 0.1.4/code 5，笔记最低宿主 code 36；便签 0.1.0、日历 0.1.1、相册 0.2.0 保持。GitHub Release android-v0.2.36；发布 CI run 37089496316 success。D1 0022/0023/0024 已应用；历史 0015/0016/0020/0021 核对既有字段、补齐幂等建表/索引后登记迁移记录。D1 CI 使用专用 CLOUDFLARE_D1_API_TOKEN，R2 凭据保持原用途。模拟器新版宿主手动/自动读取正式更新清单成功，正式设备读取与账号对象同步 GET 返回 200，未覆盖真机正式包。

## 2026-10-03 当前笔记指定电脑接续

笔记 0.1.4 / 手机宿主 0.2.35 / 桌面适配 0.2.4。手机更多和桌面左下“在电脑上打开”选择同账号 desktop，先保存同步，指定设备、24 小时有效期、离线上线处理与完成回执。通用 Provider/WebView handoff 接口，业务仍在用户扩展。Node 7 项、隔离 instrumentation 5 项加后台无轮询/outbox、桌面真实 editor/path/重复/隔离/过期、真机经正式 cloud 发送与完成回执 1 项通过。Dev 两包已安装打开，生产 0.2.26 保留。线上旧消息缺少 grant 已通过 authenticated /me message ownership lookup 兼容，无 Worker 手动部署，正式应用中心未更新。见开发文档最新章节。

## 2026-10-03 当前笔记全屏详情与已删除

笔记 0.1.3：全屏可滚动编辑、返回保存/稍后继续草稿、时间与字数、分享/保存/更多；首页标题与按钮同排，底部已删除替代同步，墓碑只读详情与确认恢复。桌面适配 0.2.3 保留删除正文以供恢复。Dev/Release/lintDev、Node 4 项、隔离 UI/同步 4 项及后台唤醒/无轮询/outbox 全通过，QA passed，最新产物 bded8cec8bf54f9f8b75d78df614ac47。正式应用中心尚未更新。

## 2026-10-03 当前笔记 UI 与事件驱动同步

用户选择最新参考图第一版，已实现 native 炭黑/暖黄搜索、目录分类、双列卡片、新建、深色编辑，notes 0.1.2。用户明确反对 10 秒轮询：现已移除手机 10 秒、15 分钟 periodic 和桌面 10 秒/30 秒 tick；通用宿主用 extension-storage.changed 元数据消息触发对端拉取，复用现有通道，打开/重连/失败补偿保留。手机主 app 0.2.34、桌面笔记 0.2.2。Dev 两包已安装到一加，正式包 0.2.26 保留；正式应用中心依旧笔记 0.1.1，当前测试改版未发布正式渠道。UI/同步/事件唤醒/闲置无轮询/第二次通知/签名广播限制/后台 outbox 均通过，QA passed，详见 docs/yanzi-notes-development-2026-10-03.md 当前章节。

## 2026-10-03 笔记后台同步和 UI 方案

笔记手机 0.1.1 已在应用中心发布并下载核对哈希。后台 JobScheduler 联网/周期 15 分钟、持久 outbox、进程回收后的下载与上传测试通过；电脑 app.runInBackground 通用声明及宿主 30 秒 tick，笔记 0.2.1 自动启动和关闭隐藏。Android Dev/Release/lintDev、Node 3 项、隔离原有 3 项和后台 outbox 1 项通过。UI 三版图已生成保存在用户笔记 design-options，等待用户选择后实施。真机 Dev 升级被自动审批拒绝，未覆盖。具体见开发文档的后台章节。

## 2026-10-03 独立燕子笔记

手机 notes 0.1.0 已发布应用中心，正式 APK 下载哈希复核通过，只新增 yanzi-notes 项，保留线上相册/日历版本。业务源码在用户 yanzi-notes/android，桌面 NoteGen 同目录适配同步，不把笔记业务放进宿主。宿主补通用 WebView 账号 CAS 存储与 local 读取选项；手机复用同签名 extension-storage Provider。共享 notes/sync.v1.json，Markdown 路径与正文、记录冲突、删除墓碑、离线修改/草稿恢复、账号隔离。Windows→Android→Windows 3 项隔离 instrumentation、Node 2 项测试、Dev/Release/lintDev 和桌面构建通过。一加仅安装笔记 Dev，正式燕子不变。前台同步、256 KiB 集合上限、附件和 Markdown 富文本预览未实现。详细状态见 docs/yanzi-notes-development-2026-10-03.md。
## 2026-10-02 相册深色 UI 改版

已按用户选择的第二版深色设计改为原生深灰/青柠界面、错落图库、底部三项导航、选择发送栏与处理方式弹窗。权限/选图/刷新等收进菜单；任务/作品/弹窗同主题，按可见区域解码缩略图。最终 UI instrumentation 1 项通过，Dev/Release 与 lintDev 通过；跨端回归产物 `6ebe7fdb58d24656852a3b6d0ed1a4eb`，图片 2 项、UI 1 项、大图校验、83 项协议与同步均通过。手机配置恢复、生产包保留、桌面重启通过。相册 Dev 已更新；正式应用中心仍未发布。视觉记录在用户目录 `yanzi-album/design-qa.md`，最终截图 `%TEMP%/YanziDev/album-dark-ui-final/`。

## 2026-10-02 独立相册与图片工作流

手机主应用升至 0.2.33（Dev/Release 构建通过），增加通用 `CompanionTransferProvider`：同签名 + extensionScopes + workflowCapabilities，凭据不出宿主；任务按账号/服务器隔离，私有原图快照、指定电脑、LAN 优先与云端设备消息回执，结果只读 URI。Windows 增加 `files.workflow.run`、受认证文件票据与按账号隔离分块会话。≥2 MiB 复用 1 MiB 缺失块续传，回传支持 offset 与最终 SHA 校验，任务结果持久化去重/参数冲突/结果未知处理。

独立相册 `cc.luoluoluo.yanzi.album` / `.dev`，按日历模式开发，源码遵守用户小程序目录规则，位于 `%LOCALAPPDATA%/OpenQuickHost/Extensions/yanzi-album/`，仓库只保存通用宿主能力、构建/发布/验证入口与文档。Android 支持系统图库授权、系统选图及分享、多选、按相册筛选、日期展示、加载更多、电脑/处理方式选择、任务与作品、自动 MediaStore 保存与分享；提交时启动前台保存服务，退到后台仍保存回传作品，完成后停止。Windows 相册注册 `album.process`，提供缩放、质量压缩、黑白、JPEG 导出与 EXIF 方向规范化；保持原件，桌面查看处理作品。

真机最终图片测试 2 项 instrumentation 均通过，覆盖五组 >2 MiB 的 4000×2000 实际 PNG 工作流（LAN/云端、缩放/压缩/黑白/JPEG）、EXIF 90 度 → 400×800 回传、前台/后台系统图库保存、原图 SHA 不变、重复请求及跨应用 scope 拒绝；分块/offset 字节与错误哈希拒绝通过。同期 83 项设备协议、手机能力/增量/定时/删除同步检查通过。产物：`%TEMP%/YanziDev/message-bridge/3a28e1a98e764178a3c9b091c2385db0`，前一轮大图结果 `d90b10614b9b463ab51410120e56102e`。配置/正式服务器恢复、生产包保留与桌面重启检查均通过。相册 Dev/Release 及 lintDev 通过；真机 Android 11，较新 Android 后台限制仍需对应版本设备验收。独立测试清理只匹配保留的隔离账号 ID 和任务文件名，已删除记录不盲删旧 MediaStore ID，另一个清理 instrumentation 检查通过。

应用中心准备相册 0.1.0 记录、APK 签名/哈希和最低宿主 33；隔离 R2 + Worker 的目录发现、APK 下载与哈希校验通过。交付文件在 `.artifacts/application-catalog/`：相册 APK、主应用 0.2.33 APK、桌面小程序 ZIP、完整源码 ZIP、catalog.json。正式应用中心和主应用更新渠道尚未发布，未覆盖手机生产包；不能把本地验收当作线上已上架。当前桌面可发现 `album.process` 与 `files.workflow.run`。

详细接口、安装依赖、源码位置与边界见 [相册开发与交付](../../docs/yanzi-album-development-2026-10-02.md)。

## 2026-10-02 手机末端能力与统一增量同步

按状态快照 → 能力调用 → 连接同步 → 定时同步顺序实现原生后台基础：`MobileDeviceCapabilities` 发布 7 项只读能力；`DeviceHeartbeatService` 每 5 秒采集变化、30 秒发布心跳，并独立调度 `MobileAccountSync`。启动/实时重连立即检查账号对象，每 60 秒增量检查；Worker `sync-ready` 提示合并到约 3 秒检查。对象与游标持久化、账号/服务器隔离、版本去重、删除 tombstone，燕幕及账号小程序定义刷新到现有缓存与视图。无变化仅更新最近检查时间，不误报最近更新。

桌面增加受现有令牌保护的 `/v1/me/devices/{deviceId}/state|capabilities|invoke`，读取缓存或调用手机原生能力，调用优先加密 LAN，网络失败后云端 claim/ACK/结果恢复。云端结果先写持久缓存后 ACK。手机连接详情展示具体同步内容、最近检查和最近更新。文件能力限定燕子 Documents，读取上限 64 KiB；大文件继续附件协议，不随状态心跳上传。

协议、参数、运行限制和验证入口见 [手机末端能力与自动同步](../../docs/yanzi-mobile-endpoint-sync-2026-10-02.md)。正式 Worker 未发布实时提示变更；60 秒增量轮询不依赖提示。

真机完整回归通过：83 项设备协议检查；7 项目录、后台状态/文件读取、Agent API 加密 LAN 调用；增量对象、无变化定时检查、删除传播、路径穿越拒绝；双向中文消息、图片及二进制附件字节校验、断线与进程恢复、120 秒实际离线过期。产物：`%TEMP%/YanziDev/message-bridge/6ea2fc97b5ff431aa9f86e2ea15659cf`。`DEV_SERVER_ADDRESS_RESTORED`、`PRODUCTION_PRESERVED` 和 `DEV_PREFERENCES_RESTORED=True` 均通过，正式服务器地址恢复；桌面重启通过。桌面 / Android Dev 构建 0 错误，Worker Node 12 项测试通过。

最终针对性回归 `-CapabilitiesOnly -VerifyAccountLan` 通过：Agent API 同账号设备发现、目录与快照、加密 LAN 调用、移除隔离测试 LAN 记录后的云端调用及按目标结果查询；原生文件字节、增量/定时/删除同步与越界拒绝再次通过。产物：`%TEMP%/YanziDev/message-bridge/97b9f4b0776f419f9a21bb7a44927054`。配置恢复、正式地址和生产包保留检查通过，桌面 PID 23508 已启动。

## 2026-10-02 手机电脑页简化

设备管理补齐：Windows 手机消息窗口将目标切换移到左上角，菜单显示简短设备名（同名加短尾号）、在线状态、最近活动、首次登记、网络地区，右侧提供逐设备删除。云端列表不再与历史本地记录无条件合并；云端不可达时才明确回退本地记录。Worker 删除将设备设为停用，撤销设备凭据、取消待发送的指定目标请求并断开实时连接；旧心跳不能重新登记。Windows / Android 成功刷新账号自动连接列表后清理失效直连授权。位置由 Cloudflare 请求地区提供，历史没有采集则显示“未记录”；不是 GPS 或登录地点历史。后端需通过 Git / Cloudflare CI 发布后，正式环境的删除和新地区数据才会生效。

验证：桌面及 Android Dev 构建 0 错误，Dev 覆盖安装真机，生产包未变化。Worker Node 12 项检查通过；隔离 Worker 协议 83 项检查连续两轮及通用 SDK 端到端通过，覆盖未登录/跨账号删除拒绝、删除后旧心跳拒绝、消息读取拒绝和凭据撤销。产物：`%TEMP%/YanziDev/device-foundation/520529923616462fb5821276fe69426a`。桌面应用已重新启动；当前 computer-use 未取得可操作的燕子窗口，菜单视觉验收尚未完成，不将编译通过等同于截图验收。未删除真实账号中的历史设备。

后续调整：顶部仅显示“云端 / 局域网 / 离线”，删除“在线”和没有数据同步依据的“已同步”。左上电脑图标可点击打开连接详情，显示通道、真实 Wi-Fi IPv4（排除 VPN）、直连检测、已授权设备数量和账号连接接口状态，可重新检测。主页面和详情的局域网确认均使用加密协议探测成功（HTTP 200），不再把裸 `/health` 或 HTTP 401 视为已直连。

当前真机阻碍：手机 `192.168.1.72/24`、电脑 `192.168.1.100/24`；正式账号服务 `/v1/me/devices/lan-links` 在真实登录下返回 HTTP 404，手机有效直连授权为 0，因此回退云端。自动连接后端代码尚未在正式服务生效，须通过项目规定的 Git / Cloudflare CI 发布，不能用手动导入密钥或假显示局域网替代。

电脑页移除“同账号设备连接”按钮，继续由后台自动连接。顶部以 SVG 路径电脑图标替代“我的电脑”文字，连接状态在图标右侧同一行显示；图标保留“我的电脑”无障碍描述。底部电脑入口使用相同电脑图标。

验证：Dev APK 构建通过，覆盖安装真机 Dev 并启动；UIAutomator 确认连接按钮为 0、顶部状态为“在线 · 云端 · 已同步”，截图确认上下均为电脑图标。生产包版本及 APK 路径保持不变。图标使用 `mdi:desktop-classic` 显式引用，避免普通名称推断为方格图标。

## 2026-10-02 Dev 登录地址残留修复

用户登录提示无法连接 8811，检查真机 Dev 的 baseUrl 仍为本地测试地址 `http://127.0.0.1:8811`，测试 Worker 已关闭。已恢复 `https://sync.luoluoluo.cc.cd` 并重启 Dev，线上 health 返回正常。未清除应用数据或改动生产包。桥接测试恢复偏好前后清理测试遗留的 SharedPreferences `.bak`，并在重新启动后核对服务器地址与测试前一致，防止 `.bak` 覆盖恢复后的 XML。

## 2026-10-02 多设备基础协议补齐（当前状态）

Windows 与 Android 的当前连接方式为同账号自动连接：登录后后台从账号服务取得设备连接信息并完成加密握手，无需设备 ID、配对码或复制密钥。手机电脑页直接显示连接状态，电脑 `/pair` 显示状态、提供刷新。UDP 不公开 Token；登录其他账号无法取得连接信息；受限网页凭据不能取得全权设备连接。加密、账号隔离与系统密钥保护在后台保留。桌面新安装默认启用局域网，已有配置中明确关闭的设置仍保持。


照片、截图、普通文件先保存私有 Outbox 快照，再优先局域网发送；成功后仍云端补投账号消息，使其他在线或离线设备能够同步。≥2 MiB 分块续传，只补未确认的 1 MiB 块。远程文件列表、读取、写入和终端同样优先已配对的目标电脑；命令固定目标、短期限、原子认领及持久去重，结果未知不会自动重做。

多设备目标、按设备 ACK、稳定 ID/trace、分页恢复、受限网页凭据、接收空间预检查与清理、浏览器/Node 通用 SDK 已实现。隔离 Worker 73 项协议检查（两电脑两手机及新设备）、22 项 Worker/SDK Node 检查和真机云端完整回归通过。真实硬件是一台 Windows 加一台 OnePlus GM1900 Dev；生产包保持不变。云端变更尚未部署，新版 APK 自动建立同账号 LAN 连接（需云端支持 `/v1/me/devices/lan-links`）。

实现和最终测试记录见 `docs/yanzi-device-network-audit-2026-10-02.md`、`docs/yanzi-device-message-protocol-v1.md`、`docs/yanzi-lan-transfer-progress-2026-10-02.md`。以下原有章节保留历史阶段记录；新协议状态以本节为准。

---

## 1. 当前结论

燕子 Android 已从“仅发送文本的 MVP”推进到可用于真实账号验证的跨端客户端。

当前已经真实验证：

- Android 可登录现有燕子账号并注册手机设备。
- 手机可读取账号下的 Windows 小程序目录。
- 手机可读取燕幕组件。
- Windows 与 Android 已共享同一套 Object Sync 协议。
- 同一小程序可在 Windows / Android 使用相同 `extensionId`。
- Android `mobile-js` 已具备账号级小程序存储：
  `context.storage.readText / writeText / deleteText`。
- 小程序业务数据统一使用：
  `extensionData.v1.<sha256(extensionId + "\0" + key)>`。
- 已在真实账号、真实手机上完成：
  **Windows 写 → 手机读 → 手机改 → Windows 回读 → tombstone 删除**。
- 真机 Dev 与生产/历史版燕子可以并存，测试不会覆盖生产包。
- 手机开启 Clash VPN 时，燕子云请求可以绕过异常 Fake-IP 链路，改走底层 Wi-Fi/蜂窝网络。

截至本次验收，真实账号缓存中确认：

- Windows 小程序目录：27 个。
- 燕幕组件：11 个。
- 正式 `mobileExtension.v1.*`：当前账号尚未产生。
- 正式 `extensionData.v1.*`：测试对象均已 tombstone 清理，无活动测试垃圾。

---

## 2. Android 包与开发隔离

### 生产/历史包

```text
applicationId: cc.luoluoluo.yanzi.mobile
```

真实手机上的既有生产/历史版本不得因开发测试而：

- 卸载；
- `pm clear`；
- 覆盖安装；
- 修改其账号数据。

### Dev 包

```text
applicationId: cc.luoluoluo.yanzi.mobile.dev
label: 燕子 Dev
versionName: 0.2.24-dev
```

Dev 包与生产包可同时安装。

Dev build type 使用稳定 Android debug 签名，避免不同开发进程使用不同 `debug.keystore` 时无法原地覆盖，从而丢失 Dev 登录态。

构建：

```powershell
.\scripts\build-android-mvp.ps1 -Configuration dev
```

输出：

```text
mobile\android\app\build\manual-dev\yanzi-mobile-dev.apk
```

真机安装/启动 smoke：

```powershell
.\scripts\dev-real-phone.ps1
```

该脚本必须在安装前后核对生产包版本与 APK 路径，确保生产包未变化。

---

## 3. 当前跨平台架构

### 3.1 小程序身份

同一个小程序跨平台使用同一个 `extensionId`。

Windows 包和 Android `mobile-js` 是同一小程序的不同 runtime：

- 手机存在本地 runtime：优先本机执行。
- 手机无本地 runtime、账号存在 Windows 小程序：转发到电脑执行。
- 同时存在多个 runtime：允许显式选择执行端。

### 3.2 手机小程序定义

账号权威对象：

```text
mobileExtensions.index.v1
mobileExtension.v1.<sha256(extensionId)>
```

设备本地：

```text
SharedPreferences.mobileExtensions
```

只作为缓存/兼容状态，不作为账号权威来源。

Android 客户端实现：

```text
MobileExtensionDefinitionSyncClient.java
```

当前真实账号还没有正式写入过 `mobileExtension.v1.*`。因此要区分：

- 定义同步协议已实现并通过临时测试账号验证；
- 真实账号尚无正式手机小程序定义。

### 3.3 小程序业务数据

统一对象：

```text
extensionData.v1.<sha256(extensionId + "\0" + key)>
```

Windows：

```text
AccountExtensionDataStore
ExtensionStorageService
```

Android：

```text
MobileExtensionStorageClient
context.storage.readText(...)
context.storage.writeText(...)
context.storage.deleteText(...)
```

Windows 与 Android 共享：

- objectId；
- 全局 revision；
- expectedRevision；
- 409 conflict；
- tombstone 删除语义。

> Object Sync revision 是账号级全局递增号，不是每个对象从 1 开始。测试只能断言 revision 单调递增且各端看到同一 revision，不能假定 1 → 2 → 3。

---

## 4. 真实账号双向写入验收

2026-09-30 已在真实账号 + 真实 Android 手机完成完整闭环。

最终成功的一轮：

```text
Windows 初始写入       revision 811
手机读取               revision 811
手机修改               revision 812
Windows 回读           revision 812
Windows tombstone      revision 813
```

真机日志明确出现：

```text
Account storage diagnostic PASSED
```

该测试使用随机临时 `extensionId/key`，结束后执行 tombstone。

随后扫描本轮所有测试前缀：

```text
diag-readback-*
diag-primary-read-*
real-account-roundtrip-*
real-account-storage-diag-*
```

最终确认：

```text
ACTIVE_TEST_OBJECTS = 0
```

即真实账号没有遗留活动测试对象。

---

## 5. Cloudflare Worker / Object Sync 关键修复

### 5.1 直接根因：线上缺失单对象 GET 路由

本轮真机联调发现：

```text
PUT  /v1/sync/objects/{objectId}    成功
GET  /v1/sync/objects              列表可看到对象
GET  /v1/sync/objects/{objectId}   404
```

最终确认：

**单对象 GET 路由只存在于本地未提交代码，线上 Worker 当时没有部署该处理器。**

修复提交：

```text
8fc8acf  fix: make sync object reads primary-consistent
```

Cloudflare 自动部署后已在线验证：

- 新对象 PUT 成功；
- 同 objectId 立即 GET 成功；
- 手机 `context.storage` 可以读到 Windows 写入值；
- 双向 revision 正常推进。

### 5.2 D1 一致性保护

单对象读取增加兼容性保护：

```js
env.DB.withSession("first-primary")
```

运行时支持 D1 Sessions API 时使用 `first-primary`，否则退回普通 `env.DB`。

本轮已经证明的**直接故障根因是线上缺少单对象 GET 路由**；不要把它误记成“已经证明是 D1 副本延迟”。

### 5.3 回归测试补强

`scripts/test-cloud-object-sync.ps1` 已补上：

> PUT 创建对象后，必须立刻执行单对象 GET，并核对 objectId、revision、payload。

两种模式已通过：

```text
objectsAuthoritative=false   PASS
objectsAuthoritative=true    PASS
```

---

## 6. Clash / VPN 网络兼容

真实手机开启 Clash VPN 后，发现：

```text
sync.luoluoluo.cc.cd
→ Clash Fake-IP
→ HTTPS 请求约 10 秒后 Connection reset
```

这不是账号密码错误，也不是 Worker 返回 401/403。

当前 Android 已增加：

```text
MobileNetworkRouting.java
```

策略：

1. 正常网络可用时保持默认连接。
2. 活动网络为 VPN 时，查找底层 Wi-Fi/蜂窝：
   - `NET_CAPABILITY_INTERNET`
   - `NET_CAPABILITY_NOT_VPN`
3. Cloud 请求可通过底层物理网络建立 HTTPS。

已真实验证：

```text
默认 Clash 路径：Connection reset
底层 Wi-Fi 回退：成功
/health：ok=true
```

Manifest 已加入：

```text
android.permission.ACCESS_NETWORK_STATE
```

---

## 7. 设备在线状态

2026-10-01 真机联调发现：手机系统时钟相对 PC / 云端快约 4 分钟，而旧 Android 逻辑使用：

```text
手机本机时间 - desktop.lastSeenAt <= 120 秒
```

判断电脑是否在线，因此 PC 虽然持续每 30 秒正常心跳，手机仍会误判为“未上线”。

已修改为服务端权威时间：

1. Worker `GET /v1/me/devices` 新合同返回 `serverNow`。
2. 每个设备返回由服务端根据 `last_seen_at` 计算的 `online`。
3. Android 优先信任服务端 `online`。
4. 兼容尚未升级的旧 Worker：
   - 无 `online` 时优先使用 `serverNow - lastSeenAt`；
   - 若连 `serverNow` 也没有，最后才退回手机本机时间。

Worker 提交：

```text
9295a2d  fix: make device presence server-authoritative
```

新增回归：

```text
scripts/test-device-presence.ps1
```

真机验证时，即使手机时钟偏差已经超过旧版 120 秒在线窗口，燕子 Dev 仍正确显示：

```text
(云端在线)
```

因此“PC 实际在线但手机显示未上线”的问题已在 Android 兼容路径上实测修复。

## 8. 登录与账号状态

登录 UI 已改进：

- 点击后按钮显示“登录中…”；
- 弹窗内直接显示登录进度；
- 邮箱/密码为空立即反馈；
- 网络/API 失败直接显示原因；
- 日志只记录状态，不记录密码、Token。

登录成功后：

- Token 持久化；
- 注册手机设备；
- 自动刷新小程序目录；
- 自动刷新燕幕；
- 更新个人页状态。

真实账号登录已验证。

---

## 9. Android 稳定性修复

### Headless mobile-js WebView

后台运行 `mobile-js` 时：

- WebView 保持强引用直到 `done/fail`；
- 执行结束后主动 destroy；
- 避免执行过程中被回收。

### Headless UI 空指针

曾出现：

> mobile-js 业务已完成，但结束回调 `setStatus()` 访问尚未创建的 TextView，导致 App 随后崩溃。

已修复：

- `setStatus()` 允许 UI View 尚未创建；
- UI 更新切回 UI 线程；
- 集成测试增加 crash buffer 断言。

修复后：

```text
emulator crash buffer empty
full development smoke test PASSED
```

### Dev / Production 隔离

以下运行时标识不得写死生产 applicationId：

- Broadcast Action；
- Widget Action；
- taskAffinity；
- FileProvider authority。

统一基于：

```text
BuildConfig.APPLICATION_ID
${applicationId}
```

防止 Dev 与生产包串扰。

---

## 10. 当前主要文件

Android：

```text
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MainActivity.java
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MobileExtensionStorageClient.java
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MobileExtensionDefinitionSyncClient.java
mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/MobileNetworkRouting.java
```

Windows：

```text
src/OpenQuickHost/ExtensionStorageService.cs
src/OpenQuickHost/Sync/AccountExtensionDataStore.cs
src/OpenQuickHost/Sync/CloudSyncClient.cs
```

验证：

```text
src/Yanzi.SyncVerification/AccountExtensionBridgeVerification.cs
scripts/test-cloud-object-sync.ps1
scripts/test-device-presence.ps1
scripts/test-mobile-extension-storage.ps1
scripts/test-cross-platform-extension-storage.ps1
scripts/test-mobile-extension-definition-sync.ps1
scripts/test-real-phone-dev-object-sync.ps1
scripts/test-unified-extension-catalog.ps1
```

开发闭环：

```text
scripts/dev-check.ps1
scripts/dev-emulator.ps1
scripts/dev-android-loop.ps1
scripts/dev-desktop-loop.ps1
scripts/dev-real-phone.ps1
scripts/dev-smoke.ps1
```

---

## 11. 常用开发命令

```powershell
# 环境检查
.\scripts\dev-check.ps1

# Android Dev 构建
.\scripts\build-android-mvp.ps1 -Configuration dev

# 真机 Dev smoke
.\scripts\dev-real-phone.ps1

# 完整回归
.\scripts\dev-smoke.ps1

# Cloud Object Sync
.\scripts\test-cloud-object-sync.ps1 -AuthorityMode false
.\scripts\test-cloud-object-sync.ps1 -AuthorityMode true

# 设备在线状态
.\scripts\test-device-presence.ps1 -RequireOnlineDesktop

# 真机 Dev Object Sync（临时测试账号）
.\scripts\test-real-phone-dev-object-sync.ps1
```

---

## 12. 当前未完成项

### P0：整理并提交当前 Android / Sync 工作区

Worker 修复 `8fc8acf` 与设备在线修复 `9295a2d` 已提交并上线；本轮大量 Android、Windows 验证桥和开发脚本改动仍在工作区。

正式提交前至少执行：

1. review 当前 diff；
2. `git diff --check`；
3. Android Dev 构建；
4. Windows build；
5. Cloud Object Sync 两种模式；
6. 真机 Dev smoke；
7. full smoke；
8. 确认生产包未变化。

### P1：正式手机小程序管理能力

真实账号目前还没有正式 `mobileExtension.v1.*`。

下一步需要做用户可操作的：

- 手机小程序创建；
- 编辑；
- 删除；
- runtime 展示；
- 同 ID 的 Windows / Android runtime 管理；
- 冲突状态与 revision 展示。

完成后应在真实账号创建第一条正式手机小程序定义，验证：

```text
手机创建
→ 云端 Object Sync
→ Windows 目录可见
→ Windows 修改
→ 手机刷新
```

### P1：小程序云数据管理 UI

底层 `extensionData` 已通过真实账号双向写入验收，但目前主要由 runtime API 使用。

后续可增加：

- 按小程序查看 key；
- revision；
- 更新时间；
- 内容预览；
- tombstone；
- 冲突信息。

### P2：Object Sync Sessions 扩展

当前 `first-primary` 主要用于单对象 GET。

后续可评估把 changes/history/PUT 冲突判定等关键路径统一到 D1 Sessions API。

---

## 13. 安全边界

任何 AI / 自动开发流程都必须遵守：

- 不把密码、Token、Cookie 写进 Git。
- 不记录手机序列号、IMEI、Android ID 等唯一设备标识。
- 不为了签名冲突卸载或清空生产包。
- 正式账号测试只使用随机、明确前缀的临时对象。
- 测试完成必须 tombstone 清理。
- 真机破坏性测试只允许隔离 Dev 包。
- Worker 只通过 GitHub → Cloudflare 自动构建部署，不执行本地 `wrangler deploy`。
- Cloudflare Secrets 不写进仓库。
- 读取账号状态时，只输出非敏感结论，例如“Token 存在”，禁止打印 Token 本体。

---

## 14. AI 冷启动顺序

以后继续手机端开发时：

1. 阅读本文件。
2. 阅读 `.agents/AGENTS.md` 第 6 节。
3. 执行：
   ```powershell
   git status --short
   git diff --check
   ```
4. 检查 ADB：
   ```powershell
   adb devices
   ```
5. 确认真机生产包和 Dev 包并存。
6. 优先在模拟器做破坏性回归。
7. 真机只通过 `cc.luoluoluo.yanzi.mobile.dev` 验证。
8. 跨端数据问题先跑 Cloud Object Sync 单对象 GET 回归，再排查 Android。
9. 完成测试后确认活动测试对象为 0。

这样可以避免以后再次从 UI 现象重新反推整条同步链路。

---

## 15. 2026-10-01 消息、通知与双端状态闭环

Android 新增实际云端消息前台服务：5 秒串行收件、30 秒心跳、系统通知、持久化收据与 ACK、断网退避、通知禁用时保留 pending。服务从 Activity `onResume` 启动；通知权限请求码 9101 与相机 9001 分离。

Windows 启动消息轮询兜底，与 SSE 共用串行处理及进程内执行结果缓存。LAN 通知增加 Bearer 鉴权、UTF-8 字节读取和通知失败返回；Dev 使用 42982，生产使用 42981。

专项完整真机回归已通过：前台/后台/熄屏通知、通知权限恢复、断网补收、手机发往 Windows 的文本/命令及结果读取、LAN 鉴权与中文、进程重启补收、双端 120 秒离线过期和重新上线。测试使用隔离 Dev 包与临时本地 Worker，生产包保持原状并恢复 Dev 原配置。

入口：`scripts/test-real-phone-message-bridge.ps1`；详见 `docs/mobile-message-bridge-verification-2026-10-01.md`。这轮覆盖消息通信专项，Android 13+/14+ 真机仍需额外验证。未接入 FCM；PC 去重仅在当前进程有效。

本机 PC 的旧 API 端口 53919 位于 Windows 保留范围，已改为 42980 并验证 `/health` 返回成功。PC 主界面已由用户确认恢复。

最终 Dev APK 的完整专项回归产物：`%TEMP%\YanziDev\message-bridge\b0b341c429694c3082f399e45f6f77e3`。恢复账号后公网双向文本/通知也已通过；一次通知遇到网络超时后自动补收，实际 ACK 耗时 47 秒，不能保证固定 5 秒送达。

### 聊天窗口入口补齐

用户实际从 PC 的手机聊天窗口回复时，暴露出这个入口仍只支持 LAN；上一轮公网验证使用云 API，未覆盖这个窗口的发送逻辑。

已修改 `MobileMessageToastWindow`：从消息历史保留接收手机的真实 deviceId，没有 IP 或直连失败时，将文字投递到该手机的云队列；发送失败保留输入，云提交成功显示“已交云端，等待手机接收”。照片/文件目前仍要求直连，错误提示明确区分。

Android 将 `YanziChat` 云文本写入 `desktop_chat_history`，按 messageId 防止重复记录，并更新当前聊天界面。真机回归新增实际 WPF 聊天窗口发送入口测试：无 IP、失效 IP 两种路径均验证手机聊天记录；公网入口为 `scripts/test-real-phone-public-chat.ps1`，使用已有账号，仅发送明确测试前缀的聊天文字，不修改手机配置。

完整专项回归通过：`%TEMP%\YanziDev\message-bridge\8c7341c4a7a04f6a970431e8088451f4`。公网窗口无直连、直连失败转云端两种场景均通过：`%TEMP%\YanziDev\public-chat\9a22095dc7da43b590cfdf7acda5ea01`。PC 已构建并自动重启，手机更新仅作用于 Dev 包。

## 16. 2026-10-01 实时消息与双向附件补齐

在第 15 节基础上增加账号隔离的 Durable Object WebSocket、连接恢复补收、30 秒查漏兜底、断线 5 秒兼容轮询。Windows 保留旧 SSE，并在网络错误后继续重试心跳。Android 心跳与附件接收分别调度。服务端先写 D1 再发事件，接收 ACK 反馈到发送端。

PC 聊天窗口的文字、照片、文件均可转云端；Android 照片和文件发送不再依赖账号 WebDAV。云附件使用鉴权私有 R2 存储、30 MiB 限制、7 天清理、SHA256/大小校验、Range 续传。手机展示图片并可通过 FileProvider 打开文件；PC 历史保留本地附件路径。发送 clientMessageId 支持幂等和内容冲突检查。PC 命令收据持久化，重复处理与清空内存后读取收据均验证通过；中途退出不会自动重复执行，结果未知时提示人工确认。

用户确认尚未开通推送服务：实际 FCM SDK 可选构建和 HTTP v1 发送适配器已接好，HTTPS webhook 为厂商服务桥接入口。签名/失败分支使用模拟传输测试通过，带临时假配置的 FCM 构建通过；最终真机安装的是无 FCM 配置 APK。尚未验证真实系统推送唤醒，也未接入具体国产厂商 SDK。

最终不熄屏完整真机回归通过：`%TEMP%\YanziDev\message-bridge\b86dfc8f65d24fadae5731e740239e11`。真实 WPF 窗口发文字/文件/图片到实体手机、手机上传文件/图片到 PC 均通过内容哈希核对；WebSocket 连接、前后台通知、权限禁用后恢复、断网补收、手机命令/电脑结果、LAN 鉴权、进程恢复、双端 120 秒离线与恢复均通过。生产包保持 0.2.8，Dev 偏好已恢复。10 条本地消息提交到 ACK：中位数 68 ms，样本最慢/p95 87 ms，仅代表本机 Worker + adb reverse；不代表公网时延。

补充后端回归通过：`%TEMP%\YanziDev\mobile-backend\2a89db97512348a5ace6e413ce36cf72`，覆盖分段内容、越界 Range 416、错误哈希、跨账号不可读、删除、消息幂等/409、公开列表不暴露私有附件。PC 已构建并自动重启；最终 Dev APK 安装/启动 smoke 通过，无崩溃。

**尚未部署新后端**：线上旧版暂无新附件接口/实时服务。部署必须经过 Git main 自动构建，先应用 D1 0018 迁移并创建 DEVICE_RELAY。公网不同网络、真实推送以及 Android 13/14+ 后台限制仍待上线/服务开通后验收。接入及发布说明见 `docs/mobile-realtime-attachments-2026-10-01.md`。

用户操作偏好：日常回归不主动熄屏；`test-real-phone-message-bridge.ps1` 默认禁用主动熄屏，确需最终验收时显式 `-IncludeFinalScreenOff`，仅在最后执行一次。不要把日常无熄屏回归写成已重新验证熄屏；本轮早先的熄屏通知已通过，最终轮没有重复操作。

## 17. 2026-10-01 发布与公网真机验收

用户明确授权先 push 最新源码，再发布并验证公网。最新开发代码以 3bb3e0f 推送 main，Cloudflare Git 构建成功；线上应用 D1 0018 迁移，DEVICE_RELAY 绑定及 */30 清理 Cron 生效。发布修正 61acb66 也已推送且构建成功：按仓库要求移除已生效的首次 DO migration；pre-push 不再强制重新部署历史版本，保留 Secret 同步；PC 旧 SSE 兼容模式每两分钟重试实时连接。

最终公网验收通过：%TEMP%\YanziDev\public-chat\59948230f72141d68bc291e5fa7c21d4。使用已有账号和实体 Android Dev 包，没有 adb reverse、未主动熄屏：真实 PC 聊天窗口无直连/失效直连转云文字、双向公网文件和图片下载后哈希一致、两端 WebSocket 当前状态、手机强制停止期间 pending 保留及重新打开补收均通过。PC 构建后自动重启，/health 正常，生产手机包保持原状。临时云附件清理后再次核对数量为 0。

10 条最终公网通知提交到 ACK 的中位耗时 2934 ms，样本最慢/p95 4371 ms；前两组中位 2562/3253 ms，最慢 6563 ms，观察到 HTTP 超时自动恢复。不能把 ACK 当作通知显示延迟，也不能把本地 68 ms 当作公网指标；当前仍未达到稳定微信级时延。系统推送尚未开通，国产厂商 SDK、Android 13/14+ 真机及不同运营商覆盖没有在本轮验证。

公网验证脚本曾误读下载中的 .part 文件、依赖已轮转的旧连接日志，已改为校验最终文件并检查当前设备 realtime 能力状态。详见 docs/mobile-realtime-attachments-2026-10-01.md。

## 18. 2026-10-01 多设备聊天、旧正式版与公网更新

现场 hi/101 原来定向 K70 且一直 pending；K70 只有发送能力，在线不代表可收件。按用户要求本轮不操作 K70。一加在线实例此前是 Dev，正式包仍为 0.2.8、登录态过期且没有应用内更新入口。

新增 D1 0019 account_chat_receipts：聊天按账号同步、设备独立 ACK、离线/新设备补收，命令仍定向。HTTP/SSE/WS 共用收件规则；PC 登录后使用共享云队列并显示账号聊天，手机各注册入口报告版本及收件能力。原 pending YanziChat 保留 ID 转共享队列，hi/101/102 已在一加正式版可见。正式版和 Dev 的独立回执均已核对。

正式包签名一致后从 0.2.8 保留数据覆盖到 0.2.25，自动恢复凭据及实时收件。0.2.25 → 0.2.26 公网自动检查、90.8 MB 后台下载、APK 校验和系统安装器拉起已通过；Google Play Protect 正常扫描后提示“已屏蔽有害应用”，没有给出具体命中类别。未点击“仍要安装”、未关闭扫描、未以 ADB 绕过该次安装，正式版仍是 0.2.25，等待用户选择。不能写成完整自动升级已通过，也不能直接认定误报。

0.2.26 另包含旧内联 screenshot 兼容、更新包签名/版本/SHA256 和安装权限返回续接，构建通过但最终安装/新兼容图片仍需验收。公网更新清单暂保持 0.2.25。发布脚本自动更新 R2 清单并保留 Windows latest，未来 APK 发布必须使用 scripts/upload-release-installer.ps1 / scripts/publish-mobile-update.ps1，不能只发 GitHub 而遗忘公网清单。

后端回归通过：57759b6029a647c6b6f2e08dac416df8；真实 WPF 公网文字回归通过：198f4e814a9241eeaaaf58b77cf3f17a。一台实体一加上的正式/Dev 两实例独立 ACK，不等于两部实体手机验收。无主动熄屏、无数据清空；真实系统推送仍未开通。详情见 docs/mobile-account-chat-update-2026-10-01.md。

## 19. 内置便签与独立日历（2026-10-01）

本轮按用户选择实现两个模式：`quick-notes` 是燕子内可见 `mobile-js / mobile-view` 小程序；`cc.luoluoluo.yanzi.calendar` 是独立原生 Android 日历，Dev 包后缀 `.dev`。宿主升级到 0.2.27 增加通用签名权限 + extensionScopes 的存储 Provider，令牌不传给独立 APP。便签源文件和定义位于 `extensions/quick-notes`，Gradle 自动生成内置目录；存储调用改为异步，网络请求不阻塞便签编辑。

电脑日历 1.1.0 保留旧事项 ID、原 JSON 与首次备份，通过账号 ObjectSync 的 `taskbar-calendar / calendar.v1.json` 同步。两端均有持久化待上传修改、记录版本、整体 expectedRevision 和删除墓碑。同事项冲突保留本地版本，提供显式处理；账号切换不交叉上传。Windows 宿主增加通用 `/v1/account-storage/{id}` 版本化读写接口，其他小程序也可复用。

验证：Windows 构建/自动启动与日历动态编译重载通过；宿主与独立日历 Dev/Release 构建通过；模拟器集成测试验证 Windows↔Android 读写、CAS、记录冲突、离线草稿持久化及重连、删除、范围及签名权限拒绝。原 Windows↔Android 共享存储回归通过（含新异步桥接）。一加 Dev 真机公网完成日历手机新增→电脑、电脑改名→手机、电脑删除→手机，测试事项清理后原两条事项逐字段保持一致。便签草稿在结束进程后恢复，公网保存和删除可由 Windows API 核对。

本轮开始设备检查时，一加正式包已是 0.2.26（此前停留 0.2.25 的记录属于历史状态）；本轮没有执行该正式版升级。仅覆盖 Dev 到 0.2.27-dev，正式版 0.2.26 路径和版本保持一致。正式 0.2.27 APK 已构建，尚未安装或发布，公网更新清单未改。

边界：日历前台每 10 秒同步及保存后同步；独立日历关闭后的后台自动刷新、手机原生闹钟通知和系统日历写入尚未实现，闹钟由电脑端响铃。未做息屏测试、未操作 K70、未开通推送服务。操作、协议及完整验证见 `docs/mobile-notes-calendar-2026-10-01.md`；回归脚本为 `scripts/test-calendar-companion.ps1`，只能清理模拟器测试包。


## 20. 2026-10-01 应用中心与统一数据平台

- Android 0.2.28 新增通用应用中心：公共目录、账号应用库、内嵌定义获取、独立 APK 下载校验与系统安装确认。
- 云端新增 /v1/applications 与 /v1/extension-data 接口，兼容现有 extensionData.v1 对象；专用授权默认只读、有效期 1 小时，可撤销且不能访问账号级接口。
- JavaScript SDK 提供账号隔离的持久化草稿、CAS、断网恢复与显式冲突处理。授权暂为用户手动复制令牌，后续网页自动登录需要 OAuth/PKCE。
- Node 4 项测试及本地真实 Worker/D1 集成测试通过；Dev/Release 构建通过，OnePlus Dev 安装验证通过，正式手机版本 0.2.26 保持不变。公网发布与应用中心下载验证继续执行。
- 详见 docs/application-platform.md。

### 20.1 最终接入与验证

- 最终主应用 0.2.29 在开发页提供应用中心，获取后立即刷新；敏感授权令牌不进入剪贴板同步。独立日历 0.1.1 接入共用 Android data-sdk。
- Android SDK 提供 HostStorage、ScopedCloudStorage、YanziDocument；跨平台 JavaScript SDK 同时可用于网页与 Node。
- 日历和 Android SDK 共 3 项 instrumentation 测试通过，Windows/Android 往返与冲突验证通过；真实公网授权、撤销、两项下载哈希校验通过。
- 无本地应用缓存的第二客户端（模拟器）已获取账号中的便签定义和应用库选择，验证跨设备分发。OnePlus 真机仅更新 Dev，正式燕子仍为 0.2.26，全程未息屏。

### 20.2 公网发布闭环

- 代码已推送 main，Cloudflare Git 构建 564f5c2 已成功部署；没有本地部署 Worker。
- 公网更新清单为 android-v0.2.29，主 APK 实际下载及 SHA-256 校验通过。应用目录含便签 0.1.0 和独立日历 0.1.1。
- GitHub platform-sdk-v0.1.0 已发布 Android AAR 与浏览器/Node ES module。
- 模拟器通过应用中心下载日历 0.1.1，经过系统安装确认成功安装，首次打开显示已同步电脑日历；真机 Dev 日历 0.1.1 同样显示已同步。
- 已清理模拟器中的测试账号登录与日历缓存。物理手机正式燕子仍为 0.2.26，需要用户正常确认主应用升级；未绕过系统安装流程，未息屏。

## 21. 外部应用线上授权接口（2026-10-01）

- 主应用 0.2.30 新增应用中心 AI 接入地址，按小程序 ID + 数据 key + 最大权限创建七天邀请地址。GET 仅发现说明，POST 才产生五分钟待确认申请。
- 手机前台显示原生授权确认，后台消息服务运行时显示高优先级授权通知；每五秒独立检查，不占用聊天接收队列。电脑托盘提供接入窗口和原生确认弹窗。
- 用户确认后外部调用方凭私人轮询秘密自动领取一小时限定令牌，不需要主账号密码或人工复制令牌。只读、跨 key、撤销和重复决定均受服务端约束。
- 日历声明 records-v1 schema，通用服务提供逐条 GET/POST/PATCH/DELETE，与原共享存储保持同一对象和版本语义；修改删除必须携带记录 expectedVersion。
- 当前 Node/SQLite 授权与记录测试 5 项通过，真实本地 Worker/D1 集成通过。桌面构建并重启成功，Android Dev/release 构建成功，OnePlus Dev 0.2.30 安装 smoke 通过，正式 0.2.26 未变。公网和两端 UI 验证待发布后记录。
- 接入协议与例子见 docs/external-data-api.md。尚未配置系统推送，强制停止手机应用后不保证授权通知到达；不主动息屏。

### 21.1 公网验证完成

- main 的 28437e2 已由 Cloudflare Git 自动构建成功部署；数据库 0020 已应用。目录公开 records-v1 schema；Android 0.2.30 正式 Release 和公网更新清单已发布。
- 真实公网匿名申请同时触发电脑 WPF 弹窗与 OnePlus Dev 原生弹窗，核对码一致；手机点击允许后 AI 自动领取令牌，电脑弹窗自动关闭，待确认列表归零。
- 公网逐条新增、修改、读取、删除临时日程成功；Windows 本地日历文件和真实手机 Dev 日历缓存均收到创建、修改、删除。测试前后三条原始日程完全一致；手机测试记录为 tombstone 且无待上传项。
- 手机退到桌面后后台授权通知包含正确申请方与核对码。测试申请、令牌及邀请地址均已撤销；全程未息屏，未动 K70，正式 OnePlus 燕子仍为 0.2.26。
- 公网实际下载主 APK 90,810,436 字节，SHA-256 与正式发布 APK 相同；GitHub Release 已发布并通过 UTF-8 JSON 更新中文说明。
- computer-use 可读取电脑授权弹窗的全部字段，电脑点击工具遇到捕获几何/FrameArrived 限制，因此此次授权实际点击在手机完成；电脑主动批准路径由真实本地 Worker/D1 决策接口覆盖，未宣称电脑按钮真机点击通过。

## 22. 燕窝入口与多数据授权

- 燕窝顶部新增 AI 数据接入卡片：获取数据目录、选择地址的最大范围、生成提示词、复制并打开 AI、撤销地址、列出并撤销有效授权。
- 后端 0021 为邀请与申请保存明确的 scopes 快照。AI 能申请部分或全部目录，设备确认还可进一步缩小范围。全部不包含未来新增资源，不开放账号密码、本地文件或其他账号数据。
- 手机主应用 0.2.31 提供相同的数据清单与提示词；原生确认逐项展示并提交明确 scopes。旧客户端不能批准多资源申请，单资源旧地址保持兼容。
- 燕窝移除旧 UUID 推送卡片，改成实际账号公网状态；手机在线状态来自公网设备心跳，局域网开关仅标为发现开关；端口使用实际配置，个人 WebDAV 仓库与账号同步分开说明。移除无依据的「上次刚刚」与实际只保存配置的「立即同步」文案。
- Node/SQLite 共 6 项验证通过，涵盖多资源目录、全部快照、申请/批准缩小范围、只读、未选资源拒绝、撤销，以及原日历版本和删除语义。本地 Worker/D1 旧接入回归通过。


### 22.1 公网与真机验收完成（2026-10-01）

- `f205ab1` 已在 main，公网 `/v1/applications/access-resources` 和多资源邀请/确认接口可用；公网 Android 更新清单为 `android-v0.2.31`，APK 为 `yanzi-mobile-0.2.31.apk`。
- 新增 `scripts/test-public-external-access.ps1`。公网真实账号回归已验证：资源清单 → 两资源邀请快照 → AI `scopes=all` 申请 → 账号侧缩小为仅便签只读 → 便签可读、未选日历 403 → 撤销授权后便签立即 403 → 邀请撤销，无遗留待确认申请。
- 一加实体机 `cc.luoluoluo.yanzi.mobile.dev` 0.2.31-dev 已完成原生多资源确认：弹窗同时展示“便签 / notes.v1.json”和“日历 / calendar.v1.json”，核对码与公网申请一致；真机取消日历后点击“允许所选”，公网最终只发放便签只读 scope。此轮是实际手机 UI 点击确认，不是仅用 API 代替。
- 真机应用中心“AI 数据清单 / 接入提示词”已验证只显示当前可授权的便签与日历，并能生成包含 resourceList、按任务选择 scopes、核对码确认、poll 领取限时令牌和修改前读最新版约束的提示词。
- 验收时发现真实账号遗留一条 `real-account-storage-headless-* / roundtrip/state.json` 测试对象；先核对 objectId 与 extensionId/key 哈希一致后精确 tombstone。清理后公网授权清单从 3 项恢复为 2 项，未采用会误伤真实小程序的通用前缀过滤。
- `cloudflare npm test` 6 项通过；`scripts/test-application-platform.ps1` 本地真实 Worker/D1 集成通过；`scripts/test-public-application-platform.ps1` 公网应用平台回归通过；新增公网多资源脚本的 API 自动确认模式与真机确认模式均通过。
- 实体一加生产包仍为 0.2.26，未覆盖、未清数据；只操作 Dev 0.2.31-dev。未操作 K70，未绕过 Android 系统授权/安装流程。系统级厂商推送与强制停止后的唤醒能力仍不属于本轮验收范围。

### 22.2 发布物复现验收（2026-10-01）

- `dotnet build OpenQuickHost.sln -c Release --no-restore` 成功，0 errors；现有 19 个 nullable/未使用字段警告与本轮多资源授权无关。
- Android `assembleDebug assembleRelease testDebugUnitTest testReleaseUnitTest` 成功；Gradle 223 tasks 完成，无构建错误。
- 本地 `app-release.apk` SHA256 为 `7B665FDD3CD16172A59C25018FD99358B6D0E6789EE3C0B27FEA1191C457BB0F`，与公网 `yanzi-mobile-0.2.31.apk` 完全一致；公网包解析为 `cc.luoluoluo.yanzi.mobile` / versionCode 31 / versionName 0.2.31。
- 刚构建的 `app-dev.apk` 已通过 `adb install -r` 覆盖到一加实体机，仅更新 `cc.luoluoluo.yanzi.mobile.dev`，保留原登录数据；重启后仍为 0.2.31-dev，账号会话存在，应用中心 AI 数据清单仍只显示便签与日历。
- 生产包 `cc.luoluoluo.yanzi.mobile` 仍保持 0.2.26，未覆盖、未清数据。

## 2026-10-03 工程基础整改继续执行

- API 已移出 MainActivity，新增 MobileSessionStore / MobileObjectRepository / MobileYanmController / MobileChatController / MobileWebViewRuntime；后台 Context 由 YanziApplication 初始化。
- 对象快照超过 1000 项继续分页；会话失效错误不触发路由重试；云请求与加密 LAN 显式区分。
- 新的工程 CI 和独立 Worker state 已接入；五项模拟器集成与清理后的干净 smoke 通过。
- 真机脚本支持显式 Serial，聊天验收读取 SQLite；保留正式包与原有 Dev 偏好。
- 执行及最终部署验收记录：`docs/engineering-foundation-implementation-2026-10-03.md`。
### 2026-10-03 云端与一加 Dev 最终验收

- main 的工程整改已由 Cloudflare Builds 自动部署，生产健康接口 foundationRevision 为 `2026-10-03-domains-v1`；GitHub Linux/Windows 工程 CI 均通过。
- 一加安装当前 `0.2.42-dev / versionCode 42`，正式 `0.2.42` 安装路径与版本保持；不清正式或 Dev 用户数据。最终 UI 渲染、无 Fatal/ANR 检查通过。
- 完整真机消息桥、公网账号对象（UTF-8、CAS、历史、tombstone）、公网双向聊天/图片/文件 SHA256 与进程重启补收、局域网分块 ACK 丢失恢复与重复发送持久化去重全部通过。
- 测试结束已恢复 Dev 的原公网账号/配对设置，删除临时公网附件和临时凭据副本，桌面开发程序恢复运行；精确验收证据见整改执行记录。

### 2026-10-03 Release 升级与回滚准备

- `build-android-mvp.ps1` 支持独立 ArtifactRoot / VersionCode / VersionName；候选 Release 为 43 / 0.2.43-rc.1，默认源码版本仍是 42 / 0.2.42。默认 Dev 独立构建验证仍为 0.2.42-dev。
- 新增 emulator-only 的 `scripts/test-android-release-upgrade.py`：历史 0.2.36 → 候选覆盖升级 → 同版重装 → 降级旧版启动 → 再升级；75 条中文多行历史精确迁入 SQLite，无重复，离线测试会话、设备 ID、偏好和 1 MiB 本地文件哈希保留。
- root/userdebug 模拟器允许 `-d`，不代表生产手机可直接降级；正式回滚宜发布更高 versionCode 的修复版。脚本 finally 清理临时账号并启动干净 Release。
- 候选仍沿用历史 Android Debug 证书以保持覆盖兼容；Release signing 环境未显式配置。实体一加正式和 Dev 的 0.2.42 安装均未改变，本轮不发布正式 APK。
- 详细产物、哈希、Windows 更新/数据兼容和 Cloudflare Builds 平台故障状态：`docs/release-readiness-2026-10-03.md`。
