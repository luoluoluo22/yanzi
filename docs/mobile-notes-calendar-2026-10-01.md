# 手机便签与独立日历

## 两种呈现方式

- **便签**：燕子内的小程序，`quick-notes`，运行时 `mobile-js`，界面模式 `mobile-view`。在账号小程序列表中打开，不安装另一个 APK。支持标题、正文、搜索、新增、编辑、删除、未保存草稿恢复、本地保存与账号同步。
- **燕子日历**：独立 Android APP，正式包 `cc.luoluoluo.yanzi.calendar`，Dev 包后缀 `.dev`。月历、每日事项、待办和闹钟数据编辑使用现有电脑日历 `taskbar-calendar` 的数据。手机界面明确说明闹钟目前由电脑响铃。

便签源码在 `extensions/quick-notes/mobile.js`，元数据在同目录 `mobile.json`。Gradle 构建自动生成内置目录定义。宿主只增加通用内置目录、可见小程序容器和异步存储桥接，没有把便签界面写入主程序。

## 依赖与安装

独立日历需要同一手机上安装 **燕子 0.2.27 或更新版**，并登录与电脑一致的账号；电脑日历升级到 **1.1.0**，电脑宿主需要包含版本化账号存储 API 的本次构建。正式日历连接正式燕子；Dev 日历连接燕子 Dev。两者必须使用同一证书签名。

独立日历没有自己的登录令牌。即使燕子界面没有打开，Android 也能启动其存储 Provider 完成读写。首次成功连接后，日历按账号缓存数据和待上传修改；离线可以编辑，重新联网后同步。前台每 10 秒检查一次，保存后立即尝试同步，离开日历后停止轮询。

```powershell
scripts\build-android-mvp.ps1 -Configuration dev
scripts\build-android-mvp.ps1 -Module calendar -Configuration dev
scripts\build-android-mvp.ps1 -Configuration release
scripts\build-android-mvp.ps1 -Module calendar -Configuration release
```

本次真机验证使用 Dev 隔离包，正式燕子保留 0.2.26，没有覆盖安装 0.2.27 正式包。

## 数据与冲突

共享身份为 `extensionId = taskbar-calendar`、`key = calendar.v1.json`，仍走已有账号 ObjectSync，不依赖局域网直连。对象包含 `schemaVersion:1` 和以事项 ID 为键的 `records`；每个记录有 `version`、`deleted` 和 `item`。`item` 沿用电脑端的 `Id / TargetDate / AlarmTime / Title / IsAlarm / IsTriggered / CreatedTime`，日期保持当地日历时间。

每次写入携带云端 `expectedRevision` 和读取时的账号 ID。整体对象冲突会重读后重试；不同事项可以合并。同一事项同时修改时保留本地修改，提示用户选择版本，不自动覆盖云端。删除保留墓碑，防止旧设备重新上传而复活。账号切换不会把旧账号待上传修改写入新账号。

电脑原文件 `calendar_reminders.json` 保留，初次接入自动备份为 `.before-sync.bak`。新增 `.sync.json` 日志保存待上传修改，原始事项 ID 不变。电脑端遇到账号切换暂停同步，保留旧数据；目前没有自动迁移到另一个账号的操作。

电脑日历输入支持：

- `sync-status`：查询同步状态。
- `sync-use-cloud` / `sync-keep-local`：处理电脑侧冲突，处理前保存 `.conflicts.bak`。
- `add-reminder:YYYY-MM-DD|标题`、`rename-reminder:事项ID|标题`、`delete-reminder:事项ID`：通过同一管理器操作事项，便于自动化与跨端验证。

手机长按事项，可以删除、使用云端版本或保留本地版本。便签按整个便签集合检查版本；冲突时可复制本地内容，或备份本地后使用云端。编辑期间的网络请求不阻塞界面，上传过程中再次编辑会保留新的待上传状态。

## 通用跨 APP 存储接口

宿主 Provider：`content://<hostPackage>.extension-storage`。

- 签名权限：`<hostPackage>.permission.EXTENSION_STORAGE`。
- Companion 清单声明 `yanzi.extensionScopes`，逗号分隔允许访问的扩展 ID；日历只声明 `taskbar-calendar`。
- `ContentResolver.call(uri, "read" / "write", extensionId, args)`。
- `args`：`key`；写入另含 `content`、`expectedRevision`、`accountId`。
- 返回 Bundle 的 `result` 为 JSON；包含 `ok / exists / revision / content / accountId` 或 `conflict / error`。
- 每次调用额外检查签名、调用包、声明范围、账号与版本。不给 Companion 提供密码、令牌或任意网络请求能力。单次写入最大 256 KiB。

Android 文档要求 Provider 的 `call` 自行检查权限，因此接口内显式执行权限及作用域校验：[ContentProvider.call](https://developer.android.com/reference/android/content/ContentProvider.html)。

Windows 通用接口：`GET /v1/account-storage/{extensionId}?key=...`、`PUT /v1/account-storage/{extensionId}`。需要本地 Agent 认证，写入体为 `{key, content, expectedRevision, accountId}`。旧版写入返回 409；缺少版本返回 400。它可供其他跨平台小程序复用。

## 验证记录与边界

- Windows 构建与自动重启通过；电脑日历动态编译、重载通过。
- Android 宿主 Dev / Release、独立日历 Dev / Release、日历 instrumentation APK 构建通过。
- `scripts/test-calendar-companion.ps1` 仅允许模拟器，使用临时本地 Worker 和临时账号。验证 Windows 写入 → Android 读取/修改 → Windows 读取、CAS、同事项冲突、持久化离线草稿及恢复同步、删除墓碑、跨范围与未签名调用拒绝。测试完成清理模拟器账号。
- 一加 GM1900 的 Dev 包公网验证：电脑原日历可读，手机新增测试待办到电脑，电脑改名返回手机，电脑删除返回手机。测试待办已清理；原两条事项逐字段与初次备份一致。
- 一加便签：界面可用，未保存草稿在结束进程后恢复，保存通过公网写入账号，Windows 版本化 API 读到内容。

当前日历是前台同步；没有实现关闭独立日历后的后台自动刷新、手机原生闹钟通知或系统日历写入。未进行息屏测试，未宣称具备系统推送或强制停止后唤醒能力。K70 不在本轮验证范围。正式 APK 已构建，正式包安装与面向用户发布不属于已完成的真机结论。
