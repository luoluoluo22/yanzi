# 手机聊天对象与反重力接入（2026-10-14）

用户要求在电脑页直接切换聊天对象。此轮新增群聊、设备和 Agent 对话，复用现有消息传输及账号鉴权。

## 行为

聊天图标右侧箭头打开对象列表；原「聊天」标签改为当前对象名，默认「群聊」。列表呈现图标、名称、消息预览和未读数。反重力使用原聊天输入框和消息列表，暂时仅支持文字。

每个账号/对象分别保存历史和草稿。发送期间切换对象不会改变收件人。清理仅影响当前会话。SQLite 增加 conversation_id，旧记录升级归入 group；没有清空或重建正式数据库。

群聊仍用 account-chat。指定设备使用 targetDeviceId 与目标 platform；定向文本禁止误走另一台 LAN 电脑，定向附件经现有持久发件箱投递。Agent 用 run-extension 明确电脑、适配器、发起手机与会话。手机等待启动回执，CLI 长任务在后台独立执行。

## 私有适配器与兼容

antigravity-chat 0.2.0 的 CLI 执行、Google 登录、取消及会话文件仅在用户小程序目录闭环，业务源码未加入宿主仓库。目标电脑需要安装适配器和 CLI。不同发起手机使用独立会话和 workspace，首版保持规划模式。

通用 chat.send 增加可选 conversationId；持久发件箱和重放保留标识。旧宿主参数校验拒绝该字段时可安全去掉字段重试，兼容回复标记限定桌面来源和定向收件。群聊中的同名文字不分类为 Agent，稳定 requestId 保持幂等。

## 原执行失败

真机明确返回「手机请求的扩展不存在：antigravity-chat」。磁盘目录和 Local Agent API 可发现扩展，但手机执行入口只查旧 _localExtensionIndex。源码已改为解析当前磁盘目录，也拒绝已删除的缓存条目。

现有宿主通过当前 Windows 用户受限的 Runtime IPC settings.refresh 刷新目录，未重启或替换安装文件。之后一加 Dev 真正从聊天输入发起并收回 CLI 回复。源码修复和本机缓存恢复是两个不同结果。

## 验证与交付边界

- OpenQuickHost.sln Debug 和完整 SyncVerification 通过，保留既有警告。
- --chat-conversation-metadata：Schema、持久会话/收件人、旧 outbox 兼容、完成清理通过。
- --mobile-extension-catalog：宿主启动后新增小程序可执行；删除后的旧缓存不能执行。
- Dev 与 DevAndroidTest 构建通过；候选用版本覆盖 0.2.59 / 59，未修改仓库正式版本。
- 模拟器 chat-objects：旧数据库迁移、群聊/电脑/手机/Agent 路由、来源、历史/草稿/未读隔离、箭头、附件入口和账号切换通过。
- 模拟器 chat-rich-text、message-delivery：富文本、禁通知仍落库/ACK、失败 ACK 幂等、附件校验/续传/丢失 ACK 恢复通过。禁通知仅在模拟器 Dev，结束已恢复。
- 适配器 Node 7 项：Unicode、接续、CLI 失败、超时、取消、回传失败保留结果、动态手机/会话通过。
- 一加 Dev chat-objects-live：实际输入 → 指定桌面 → CLI → 同手机 Agent 会话通过，未清除登录或用户历史；正式包版本和路径在 Dev 安装前后相同。

K70 尚未安装新 APK；未发布公共 APK、未部署 Worker、未升级或重启正式桌面宿主。K70 正式界面及息屏实时到达仍待安装/实际验收。
