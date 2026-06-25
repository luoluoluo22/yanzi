# 云同步设计方案

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
- `yanm.componentState`
- `yanyu.rules`
- `extensions.index`
- `extensionData.*`

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

## 第三阶段计划

- 引入增量变更日志。
- 支持离线编辑队列和对象级冲突提示。
- 支持用户选择恢复某个对象或某个时间点的配置。

## 第三阶段实施记录

当前先在个人同步仓库落地可审计增量记录，暂不改变账号云端 API：

- `state/config-manifest.json`
  - 记录当前配置对象索引、对象 hash、大小、更新时间、来源设备和 revision。
- `state/config-changes/*.json`
  - 每次主配置同步都会追加一条 change set。
  - change set 包含 revision、来源设备、同步原因、对象 id、操作类型、路径和 hash。
- `state/config-objects/*.json`
  - 继续作为对象化配置的实际数据源。
- `state/launcher-config.json`
  - 继续写入，作为旧版本客户端兼容 fallback。

这一阶段的价值是先让同步具备“可诊断、可审计、可回滚基础数据”。后续设置页可以读取 manifest 和 changes，展示最后同步来源、对象版本、历史恢复点；再进一步接入本地 pending queue 和 Cloudflare 对象级 API。
