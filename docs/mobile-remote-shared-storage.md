# 燕子 Android 共享文件远程访问（0.2.73）

## 验证事实与安装方式

- 2026-10-17 在 K70 0.2.72 上已实测 `mobile.files.list` 成功，但其范围仅是燕子专用的 `yanzi-documents`，返回 0 条；这个结果不能推断手机文件管理器或相册为空。
- 0.2.73 新增共享存储与照片 API；**必须更新到 0.2.73**，在「燕子 → 我的」分别开启「远程文件读取」和（如需新建）「远程新建文件」，并在 Android 系统「所有文件访问权限」授予燕子「允许管理所有文件」，才能读取公共存储。无障碍授权不能替代此权限。
- 不开放 ADB、任意 Shell 或绕过 Android 沙箱；只可访问 `/storage/emulated/0` 共享区，明确排除 `Android/`（含其他 App 私有目录）。
- 设备锁定时拒绝远程文件操作。读取和写入开关可随时关闭，默认均为关闭。
- 所有写入仅允许**新建不存在的文件**，不能覆盖、编辑、删除或创建目录；写入最大 32 KiB。手机端执行 `CREATE_NEW`，可在竞态下拒绝覆盖。
- 原 `mobile.files.list/read` 继续提供燕子自己的沙盒文件读取，行为不变。

## Capability.invoke

| 名称 | 参数 | 结果 |
| --- | --- | --- |
| `mobile.storage.status` | 无 | `androidAllFilesGranted, readEnabledByUser, writeEnabledByUser, readReady, writeReady` |
| `mobile.storage.list` | `path`（相对于共享存储根的路径；默认空字符串） | 子文件与子文件夹（每次最多 100 项） |
| `mobile.storage.read` | `path, offset, length` | 对应文件的 Base64 分块（每次至多 48 KiB） |
| `mobile.storage.write` | `path, content`（Base64） | 原子新建文件，返回长度和 SHA-256 |
| `mobile.photos.list` | 无 | DCIM / Pictures 中最多 100 张照片的路径、大小与时间 |
| `mobile.photos.preview` | `path` | 压缩后的 JPEG 预览图（Base64），不传原始照片大文件 |

调用均需现有经过身份验证的云端 `capability.invoke` 通道。设备凭证需要对应单项 `capability.invoke:<name>` scope；电脑端账户会话可发起，云服务器现有 device token 不应未经授权自行扩大权限。

## 验收步骤

1. 安装 0.2.73，保留现有账户登录和 0.2.72 无障碍/远控设置。
2. 先调 `mobile.storage.status`；未授权时应给出明确的系统和燕子开关提示，不应读取文件。
3. 在手机授权后，先列共享根文件夹，再列 `Download`、`Pictures` 与 `DCIM`，验证目录结构。
4. 启用独立写入授权，新建 `Download/yanzi-remote-readwrite-test-<日期>.txt`，用 `mobile.storage.read` 读回、对比字节与 SHA-256；再次写同一路径必须拒绝，不覆盖原文件。
5. `mobile.photos.list` 后选取一张非敏感照片调用 `mobile.photos.preview`，人工核对截图内容。不可未获授权就读取照片。
6. 关闭其中一个手机授权开关，再次调用对应读/写，应失败；验证锁屏保护。
7. `DeviceHeartbeatService` 在 ACK 后移除本地缓存的文件名和图片 Base64；云端 ACK 结果仍受现有保留规则约束，不应在日志中打印文件内容。待服务端支持私密结果 TTL / 直传附件时再扩充大文件。

## 已知限制

此版本未实现随意覆盖现有文件、删除文件、读取 `Android/data`、原图大文件直接传输或 0.2.73 安装前使用这些新 API。Android 11+ 用户需要手动在系统设置同意管理所有文件权限；0.2.72 的屏幕控制许可不包含文件读写许可。
