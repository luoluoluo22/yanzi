# 云同步设计方案

> 实施状态审计：2026-07-10。本文同时记录目标架构与当前代码状态；“计划”不代表已经完成。

## 背景

燕子的同步数据目前同时存在两条路径：

- 账号云端：`yanzi-quickpanel-settings`、`yanzi-ai-settings`、`yanzi-webdav-settings` 等用户配置。
- 个人同步仓库：GitHub/WebDAV/S3/Gitee 等后端中的 `state/launcher-config.json`、`state/yanm-state.json`、扩展包和扩展数据。

此前的问题集中在主配置快照使用整包覆盖：新机器初始化出默认配置后，因为本地时间戳更新，可能把空槽位、默认燕环、默认燕幕反推到云端，继而覆盖已有数据。

## 成熟同步产品的共同设计

Notion、聊天软件、协作文档和浏览器书签类产品通常有几个共同点：

- 服务端是账号数据的权威状态，客户端是缓存和离线编辑队列。
- 新设备首次登录先拉取云端，不主动推送本地初始化默认值。
- 数据按对象同步，不用一份大 JSON 覆盖所有设置。
- 删除是明确操作，不能用空数组或默认对象隐式表示删除。
- 每个对象有独立版本、来源设备和更新时间。
- 冲突按对象类型处理，例如消息追加、设置最后写入、空值不覆盖非空密钥。

## 目标模型

长期目标是把同步拆成三层：

1. 账号云端
   - 权威保存账号级设置、布局、快捷键、AI 配置、同步配置。
   - 新设备登录后优先拉取这里的数据。

2. 个人同步仓库
   - 保存个人扩展包、扩展私有数据、可审计备份。
   - 可作为跨平台/可迁移备份，但不应和账号云端抢主配置权威。

3. 本地缓存
   - 保存当前设备缓存和待上传变更。
   - 有设备身份、首次同步状态和最后同步版本。

## 当前完成度结论

| 阶段 | 状态 | 当前实现 |
| --- | --- | --- |
| 第一阶段：保护现有快照 | 已完成 | 快照元数据、默认配置识别、上传前远端保护、旧 JSON 兼容、遗漏设置补齐和燕幕单一写入权威均已实现。 |
| 第二阶段：对象级同步 | 代码完成，待线上切换 | D1 revision/对象协议、能力协商、桌面缓存、离线 pending、显式 tombstone、分组/页面动态对象和 409 冲突副本均已实现；兼容快照仍保留为迁移回退。 |
| 第三阶段：增量、诊断、恢复 | 配置链已完成 | 账号对象支持增量拉取、不可变历史、来源设备、冲突选择和逐对象恢复；个人仓库除 manifest/change set/Git 差异外，新增跨后端不可变完整恢复点、SHA-256 校验、30 点保留和设置页恢复。 |
| 第四阶段：扩展资产一致性 | 主要链路完成 | 登录账号时扩展包以账号私有库为权威，个人仓库仅上传备份；索引具备逻辑 revision、设备来源、tombstone/purge 和并发冲突副本。扩展私有数据已按 key 建立 revision、不可变版本、SHA-256、pending、tombstone 和冲突选择。 |

因此，原计划的配置同步主链和扩展私有数据主链已经进入可部署验证阶段，但整个云同步方案仍**没有全部开发完成**：线上迁移、真实多设备灰度，以及账号私有扩展包归档的条件 revision/历史 API 仍是收尾项。账号端燕幕已接入统一 revision 与历史协议，个人仓库已具备跨后端时间点恢复；AI API Key、个人仓库 Token/密码均改为本机 DPAPI 保存，不再进入同步 JSON。

## 数据对象拆分方向

后续不再只维护一个 `launcher-config.json`，而是逐步拆成：

- `settings.general`
- `settings.ai`
- `settings.personalSync`
- `settings.hotkeys`
- `settings.mouseTriggers`
- `quickPanel.globalGroups`
- `quickPanel.contextGroups`
- `quickPanel.favorites`
- `radialMenu.pages`
- `yanm.layout`
- `yanm.componentStateIndex`
- `yanm.componentState.*`（每个状态 key 一个稳定对象）
- `yanyu.rules`
- `extensions.index`
- `extensionData.*`

账号对象协议和个人仓库当前共同使用的固定对象是：

- `settings.general`
- `settings.runtime`
- `settings.ai`
- `settings.hotkeys`
- `settings.mouseTriggers`
- `quickPanel.groups`
- `quickPanel.favorites`
- `radialMenu.pages`
- `yanyu.rules`
- `window.controls`

与目标模型相比有以下差异：

- 快捷面板组已使用 `quickPanel.globalGroup.*` / `quickPanel.contextGroup.*` 动态对象，燕环页已使用 `radialMenu.page.*` 动态对象；对应 index 对象维护顺序，旧聚合对象会写 tombstone。
- `settings.personalSync` 由账号配置 `yanzi-personal-sync-settings` 单独保存，不在主配置对象目录中。
- 燕幕不进入 launcher 主配置对象：账号端使用 `yanm.layout`、`yanm.componentStateIndex` 和按 key 的动态状态对象；`state/yanm-state.json`/旧账号接口只接收对象权威结果的单向备份镜像。
- 燕幕布局与组件状态具有独立 revision、历史和冲突副本。单 key 修改不重写布局或其他 key；状态删除使用 tombstone。
- 扩展包索引使用独立的逻辑 revision、设备来源和 tombstone 协议；账号私有库是登录状态下的包权威，个人仓库只做上传备份。账号归档接口仍未复用账号配置对象的条件 revision/历史 API。
- 扩展私有数据使用个人仓库中的逐 key 版本对象：mutable head 位于 `state/extension-data/objects`，每次变更先写 `state/extension-data/history` 不可变版本，再更新 head；旧 `appdata` 路径仅作兼容镜像。

每个对象应有：

```json
{
  "objectId": "quickPanel.globalGroups",
  "schemaVersion": 2,
  "revision": 128,
  "updatedAtUtc": "2026-06-23T01:38:56Z",
  "updatedByDeviceId": "desktop-...",
  "deleted": false,
  "payload": {}
}
```

## 冲突策略

- 新设备：云端有内容时必须先拉取，不能上传本地默认配置。
- AI 配置：空值不覆盖非空值。
- 快捷键、鼠标触发、主题等单值设置：对象级最后写入。
- 快捷面板和燕环：按分组/页面拆分后分别合并。
- 燕幕：布局和组件状态分离，组件状态优先按 key 合并。
- 删除：使用 tombstone，不能用空数组隐式删除。

### 当前冲突策略与目标的差距

- 已实现：新设备默认配置保护、串行推送、expectedRevision 条件写入、409 冲突副本、用户选择版本、动态分组/页面对象、显式 tombstone 和恢复即新版本。
- 已实现：燕幕布局、状态索引、每 key 状态对象进入统一条件写入与历史协议；旧端点只做迁移/个人仓库备份镜像，不再反向争夺权威。
- 已实现：AI 服务商元数据参与同步，API Key 从本机旧配置/旧云快照迁入 DPAPI 后不再写回普通 JSON；环境变量值也采用相同的“只同步元数据”边界。
- 已实现：个人同步账户配置只保存后端、仓库、分支和路径等元数据；Token/密码权威收回桌面 DPAPI，Worker 对旧客户端提交也会清空敏感字段。新设备需重新填写专用凭据。
- 已实现：登录账号时扩展包由账号私有库负责拉取，个人仓库退为上传备份；本地删除同步移除账号关联并写仓库 tombstone，回收站/已删除集合会阻止账号补装，消除两条扩展同步链互相复活的问题。
- 已实现：个人仓库扩展包索引具备单调逻辑 revision、来源设备、删除/彻底删除语义；独立设备同时编辑同一扩展时保存本地包或删除意图，设置页由用户选择版本。
- 已实现：扩展私有数据按 extensionId + key 独立 revision。写入基于本机最后观察 revision，远端领先时不覆盖；本地内容、远端内容、不可变版本均保留，设置页可选择“采用本地 / 接受远端”。离线 pending 会在周期或手动同步重试。
- 未实现：账号私有扩展包归档本身尚未提供 expectedRevision、append-only history 和逐包恢复 API；个人仓库扩展数据 head 在不支持条件请求的后端仍依赖“写后复读校验 + 不可变版本 + 本地冲突副本”，需要真实并发压测验证后端一致性窗口。

## 第一阶段实施范围

第一阶段不大改后端协议，先增强当前快照机制：

- 给主配置快照增加 `schemaVersion`、`sourceDeviceId`、`sourceDeviceName`、`isInitialDefaultConfig`、`hasUserContent`。
- 推送账号云端前检查远端：如果本地是默认/无用户内容，而远端有用户内容，则跳过上传。
- 个人同步仓库和 WebDAV 同步使用同一套“新机默认配置不得覆盖远端非空配置”的判断。
- 更严格地区分默认初始化内容和用户内容：默认燕选规则、默认燕幕组件不再单独算作用户内容。
- 保持现有 JSON 结构向后兼容，旧客户端仍能读取主要字段。

## 第二阶段计划

- 将 `launcher-config.json` 拆成多个对象文件。
- 账号云端新增对象级 API，例如 `/v1/sync/objects` 和 `/v1/sync/changes?since=revision`。
- 本地记录 `lastSyncedRevision` 和 pending changes。
- 设置页展示同步诊断：最后拉取、最后推送、远端版本、来源设备。

### 第二阶段当前状态与缺口

- 已新增 D1 `user_sync_revisions`、`user_sync_objects` 表，以及 `/v1/sync/objects`、`/v1/sync/changes`、`PUT /v1/sync/objects/:id` API。
- 条件写入要求 `expectedRevision`，冲突返回 HTTP 409；被拒绝的冲突不会再消耗 revision。
- 桌面端已保存账号级对象缓存、`lastSyncedRevision` 和 pending object id，并在对象 API 不可用时继续使用兼容快照。
- 老账号首次拉取时，如果服务端尚无对象记录，客户端会以已拉取的兼容快照安全回填对象，不要求用户额外修改一次设置。
- pending 状态记录创建时间、尝试次数、期望/观察到的 revision 和最后错误；409 后远端先落入缓存，本地版本保存为可选择的冲突副本，不再按不可靠的设备时钟自动覆盖。
- Worker 通过 `/v1/sync/capabilities` 声明协议能力。默认保持整包与对象双写；只有显式启用 `SYNC_OBJECTS_AUTHORITATIVE=true` 后，新客户端才停止整包写入。
- 尚未移除整包主配置写入；当前处于双写迁移期，待线上迁移和多设备验证稳定后再切换权威。
- pending 队列仍以对象为粒度；设置页已显示状态、revision、来源设备、pending/错误，并提供“采用本地 / 接受远端”。
- 个人仓库虽然按对象存储，但冲突逻辑尚未完全复用账号端 revision 协议。
- 设置页已显示每个账号对象的远端 revision、来源设备、最后时间、冲突和不可变历史；历史版本可逐对象恢复。

## 第三阶段计划

- 引入增量变更日志。
- 支持离线编辑队列和对象级冲突提示。
- 支持用户选择恢复某个对象或某个时间点的配置。

## 第三阶段实施记录

个人同步仓库继续保存以下可审计记录：

- `state/config-manifest.json`
  - 记录当前配置对象索引、对象 hash、大小、更新时间、来源设备和 revision。
- `state/config-changes/*.json`
  - 每次主配置同步都会追加一条 change set。
  - change set 包含 revision、来源设备、同步原因、对象 id、操作类型、路径和 hash。
- `state/config-history/index.json` 与 `state/config-history/points/*.json`
  - 每次实际配置变化保存完整不可变对象集合，不依赖后端自身版本功能。
  - 索引记录 SHA-256、来源设备、大小和变更摘要，最多保留 30 个恢复点并清理更早文件。
  - 恢复前校验路径、恢复点 ID 和 SHA-256；恢复结果作为新版本同步，旧恢复点保持不变。
- `state/config-objects/*.json`
  - 继续作为对象化配置的实际数据源。
- `state/launcher-config.json`
  - 继续写入，作为旧版本客户端兼容 fallback。

这一阶段让同步具备“可诊断、可审计、可回滚”的完整配置链：账号端由 pending queue、对象 revision 和历史 API 承担；个人仓库由 manifest、change set 和不可变恢复点承担。

设置页的“配置恢复点（全部后端）”直接读取统一索引，因此 WebDAV、S3 和非 Git 后端也能产品化恢复，不再依赖 Git 提交历史。change set 继续用于轻量审计，完整恢复由不可变 restore point 承担。

## 2026-07-10 同步范围审计补充

本轮已纳入主配置同步：

- 应用程序鼠标手势绑定。
- 搜索范围的排序、显示和固定配置。
- 自动更新、浏览器助手、通知关闭、手动扩展编辑等用户偏好。
- 环境变量名称与说明；变量值继续使用本机 DPAPI 保存，不进入普通云端 JSON。
- AI 服务商名称、地址、模型和启用状态；AI API Key 迁入本机 DPAPI，不进入新账号对象、兼容快照或个人仓库配置。
- `SyncCoverageCatalog` 通过反射检查全部 `AppSettings` 字段；新增字段没有声明同步归属时，`Yanzi.SyncVerification` 会失败。目前 67 个字段均已分类。

继续保留为设备本地数据：

- Agent API Token、广域网推送 UUID、局域网开关等设备身份或入口配置。
- 设置窗口位置、备份目录、上次备份时间。
- 最近新增、未读标记、移动端缓存和测试参数等临时状态。
- AI API Key、Git/Gitee/GitLab/Gitea Token、WebDAV 密码和 S3 Secret Access Key；这些值使用当前 Windows 用户 DPAPI 加密，新设备重新填写。

## 后续实施顺序建议

1. 先部署 D1 `0010`～`0013` 迁移，再部署 Worker；保持 `SYNC_OBJECTS_AUTHORITATIVE` 未设置，执行真实账号多设备灰度。`0012` 清理旧 AI 明文密钥，`0013` 清理个人仓库 Token/密码。
2. 灰度稳定后设置 `SYNC_OBJECTS_AUTHORITATIVE=true`，停止新客户端整包写入；旧快照只读兼容继续保留一段版本周期。
3. 评估是否在未来增加可选的用户口令保险箱；当前默认采用“本机 DPAPI + 新设备重新填写”，不让服务器持有可直接访问个人仓库的凭据。
4. 为账号私有扩展包归档增加 expectedRevision、append-only history 和逐包恢复，并把设置页现有扩展冲突入口接入归档历史。
5. 将个人仓库恢复点索引升级为带条件写入/合并的并发索引，进一步强化两台设备同时备份时的索引完整性。
