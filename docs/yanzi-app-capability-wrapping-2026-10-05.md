# 燕子：电脑软件与手机 App 通用能力封装（2026-10-05）

## 目标

将“某个软件/某个 App”从一次性硬编码调用，提升为燕子能力网络中的通用 Provider。原则是：

- 新安装软件/App 自动进入能力网络，不逐个重复开发。
- 上层 AI 面向稳定能力名，不依赖具体快捷方式、包名或 UI。
- 电脑与手机统一通过 Capability Registry 暴露。
- 手机优先局域网，无法使用局域网时走现有云端 `capability.invoke`。
- 执行动作与读取动作分开，便于权限与风险控制。

## 已落地能力

### Windows 应用

- `app.list`：列出可启动应用。
- `app.search`：按名称、别名、关键词、启动路径搜索。
- `app.status`：判断应用是否存在并返回启动信息。
- `app.open`：打开或激活应用。
- `app.openFile`：用指定应用打开文件/目录。
- `app.ensure`：对 KnownApplicationCatalog 中的常用软件自动确保安装，优先 winget。

执行解析规则：应用名/ID/路径精确命中优先于关键词，避免“微信”被其他带微信关键词的软件抢占。

### Android App

桌面侧能力：

- `phone.app.list`
- `phone.app.info`
- `phone.app.open`
- `phone.app.openUri`
- `phone.app.shareText`
- `phone.app.settings`

手机原生执行能力：

- `mobile.apps.list`
- `mobile.apps.info`
- `mobile.apps.open`
- `mobile.apps.openUri`
- `mobile.apps.shareText`
- `mobile.apps.settings`

手机枚举使用 Android Launcher Activity，不依赖 ADB，因此正式使用时数据线断开也能工作。

## 当前实际覆盖

2026-10-05 真机/本机验收：

- Windows：`app.list` 返回 235 个可启动应用入口。
- Redmi K70：`phone.app.list` 返回 103 个可启动 App。
- 微信桌面端可由“微信”精确解析为已安装。
- K70 微信可解析为 `com.tencent.mm`，版本 `8.0.78`。
- K70 Termux 已通过 `phone.app.open` 做真实打开验证。
- 手机 App 能力真机 instrumentation 验收通过，测试中识别微信与 Termux。
- 新增 12 个上层能力在当前 Runtime 中 12/12 注册。

因此当前策略不是维护“235 + 103 套独立代码”，而是维护 2 个通用 Provider；以后安装/卸载应用时，能力目录会动态反映。

## 跨端修复

真机验收时发现：K70 在 VPN/多网络环境下能收到云端执行消息，但 execution claim 曾被强制走底层物理网络并出现 TLS connection reset。

已修改 `MobileMessageClient`：

- claim 仍然只发送一次，避免“响应丢失后重试”导致执行状态不确定。
- 单次 claim 改走系统默认网络，与 heartbeat/ACK 网络路径保持一致。
- 真机修复后 `phone.app.list` 可以从桌面经云端到 K70 正常返回。

桌面 `YanziMobileCapabilityClient` 同时统一解包 LAN/cloud 返回，AI 最终拿到结构化结果，不需要再处理 `executionResult.output` JSON 字符串。

## 边界

当前通用 Provider 已覆盖“应用层基础能力”，但不会伪装成每个第三方应用的内部业务 API。

例如以下属于后续专用 Provider，而不是通用启动能力：

- 微信：指定联系人、发送消息、读取会话。
- 百度地图：地点搜索、导航方案。
- 网易云音乐：搜索歌曲、播放/暂停/歌单。
- Termux：执行具体命令。
- Tasker / Auto.js：触发任务与自动化脚本。
- WPS：文档创建、编辑、导出。
- 剪映：项目导入、渲染与导出。
- 网盘：上传、下载、搜索、分享。

这些应继续遵循“官方 API / Intent / Deep Link / 本地接口优先，浏览器或 UI 自动化兜底”的 Provider 分层，而不是给每个应用手写重复的启动逻辑。

## 验收

- OpenQuickHost Release build：通过，0 error。
- Yanzi.CapabilityVerification：65 checks passed。
- Android dev Java compile：通过。
- Android release assemble：通过。
- K70 正式版覆盖安装：成功，保留正式包 `cc.luoluoluo.yanzi.mobile`。
- 临时 dev / androidTest 包已从 K70 清理。
- 当前共享 Runtime 已切换到新快照并通过健康检查。
