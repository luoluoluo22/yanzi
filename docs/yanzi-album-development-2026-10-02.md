# 燕子相册开发与交付

## 形态与源码

与独立日历一致：手机是独立 Android 应用 `cc.luoluoluo.yanzi.album`，Dev 为 `.dev`；手机主应用需要 0.2.33 或更新版。账号凭据仅保留在主应用中，通过同签名、限定 extensionScopes 的 ContentProvider 调用通用文件工作流接口。

相册业务源码按仓库规范放在 `%LOCALAPPDATA%/OpenQuickHost/Extensions/yanzi-album/`：`main.cs`、`manifest.json` 是 Windows 小程序，`android/` 是 Android 独立应用与 instrumentation 测试。桌面主程序仅新增通用文件票据/工作流能力，手机主应用仅新增通用伴随应用传输 Provider。仓库不保存相册业务源码。

## 已实现的闭环

手机图库/系统选图/系统分享 → 选择同账号电脑与处理方式 → 燕子持久任务与原图快照 → 加密 LAN 优先，云端设备消息兜底 → Windows `album.process` 真正处理图片 → 校验回传文件 → 自动写入系统图库 `Pictures/燕子作品`。

用户提交任务时启动相册前台服务，相册退到后台仍轮询并将已校验结果写入系统图库；完成后停止服务。再次打开相册会恢复未结束任务的保存。系统强行停止应用或设备重启后的持续后台执行不作保证。

手机支持授权范围内的系统图库、按日期与来源相册展示、多选（每批最多 32 张）、长按预览、分批加载更多、电脑选择、任务状态、作品预览与分享。系统图片 VIEW/SEND/SEND_MULTIPLE 可选择燕子相册。原图不覆盖、不删除，电脑结果是新作品。

Windows 相册小程序启动后注册 `album.process`，支持缩放至最长边 1920、JPEG 质量 70 压缩、黑白效果和 JPEG 导出；显示已处理作品并可打开文件。小程序窗口关闭后处理能力注销，手机无法把未运行的处理器当作成功。其他电脑小程序可使用同一能力契约接入工作流。

## 深色界面改版

按用户选定的第二版设计实现 Android 原生界面：深灰背景、青柠操作色、错落图片布局、同账号电脑入口、相册筛选、底部图库/任务/作品导航，选择后显示固定发送栏。授权/系统选图/刷新/加载更多收进菜单，发送时选择处理方式。任务、作品、预览及弹窗统一深色。图片只在可见区域解码，缓存 24 MiB，离屏释放显示引用。

视觉 QA 在用户目录 `yanzi-album/design-qa.md`，比较图在 `design-assets/dark-ui-comparison.png`。最终真机截图在 `%TEMP%/YanziDev/album-dark-ui-final/`。先完成跨端回归（`6ebe7fdb58d24656852a3b6d0ed1a4eb`，图片 2 项 + UI 1 项及既有协议/同步通过），随后对最终视觉/可见缩略图版本重跑 UI 测试，1 项通过；Dev/Release 与 lintDev 通过。Android 11 实机验收，不宣称所有 Android 版本已验证。

UI 测试图片仅为本机 Windows 壁纸的本地副本，不进入正式 APK/源码 ZIP；源码包附 `prepare-ui-test-assets.ps1` 用于本地准备测试素材。Tabler 图标 MIT 许可保存在 Android assets 中。

## 基础接口

- 主应用 Provider：`content://<hostPackage>.companion-transfer`。
- `devices`：同账号设备与不含凭据的账号标识。
- `submit`：extensionId、jobId、已授权的图片 content URI、targetDeviceId、capability、parameters；可用 `transport=cloud` 指定云端，默认自动优先 LAN。调用方签名、extensionScopes、workflowCapabilities 均校验。
- `jobs/status`：账号和服务器隔离的持久化任务，不暴露宿主文件路径。完成结果通过只读 `result/<extensionId>/<jobId>` URI 读取。
- Windows `PUT/GET /v1/companion/files/{ticket}`：受现有令牌或账号所有者加密直连授权保护，校验长度及 SHA-256；GET 支持 `offset` 续传。
- ≥2 MiB 上传复用通用 1 MiB 分块、缺失块重传与提交校验，使用 `/v1/companion/transfer-sessions/{id}`；会话按账号隔离。
- Windows `files.workflow.run`：指定 jobId、输入 transferId/attachmentId、处理 capability 和 parameters。将输入文件交给已绑定的小程序，并验证结果只能来自该任务输出目录，拒绝路径穿越和符号链接越界。

任务完成结果与请求指纹持久化，同 jobId 的重复请求返回原结果，改变内容/工作流/参数会冲突。执行后尚无完成记录的任务报告结果未知，不自动重复处理。云端沿用既有 capability.invoke 的目标设备、有效期、claim、回执与结果恢复。单文件现有上限 30 MiB，云端指令有效期 30 分钟，已发送指令的过期状态由回执展示。图库修改和广泛访问仍受 Android 系统授权约束，不宣称替换所有厂商相册入口。

## 构建与应用中心

```powershell
scripts/build-android-mvp.ps1 -Configuration dev
scripts/build-android-mvp.ps1 -Module album -Configuration dev
scripts/build-android-mvp.ps1 -Module album -Configuration release
```

构建器识别用户目录中的相册工程。`publish-application-catalog.ps1` 新增 `-AlbumApkPath`，检查正式包身份、APK 哈希与签名证书，与日历同证书才准备/发布应用中心记录；保留其他已发布应用。目录注明手机主应用最低 versionCode 33，电脑需要相册小程序。`-PrepareOnly` 只产出本地目录和 APK，不发布线上数据。

发布前需要提供相册 APK、桌面小程序安装包和含通用接口的手机主应用更新；更新正式应用仍由用户正常确认，不覆盖现有生产包做测试。

## 真机验证

```powershell
scripts/test-real-phone-message-bridge.ps1 -SkipBuild -VerifyAccountLan -CapabilitiesOnly -VerifyAlbumWorkflow
```

使用 OnePlus GM1900 Dev、隔离 Worker、独立 Windows 数据根。创建和清理的只有相册测试自身的 MediaStore 图片，不访问或删除用户照片。检查真图片缩放尺寸、局域网与云端回传、原图 SHA 不变、系统图库写入、重复请求、跨应用授权拒绝；检查大图分块、文件 offset 与错误哈希拒绝，并复用 83 项设备协议及手机增量同步检查。结束恢复主应用登录/正式服务器地址，核对生产包不变并重启桌面。

首轮 4000×2000 → 1920×960 的 LAN/云端双链路、自动 MediaStore 保存、原图保留与去重已通过，产物 `%TEMP%/YanziDev/message-bridge/7e0e47ba5192472ea6b121893eaf320a`，截图 `album-pipeline.png` 已人工查看。最终一轮产物为 `%TEMP%/YanziDev/message-bridge/3a28e1a98e764178a3c9b091c2385db0`：2 项 instrumentation 通过，包含五组大图处理、EXIF 方向修正、相册退到后台后自动写入 MediaStore；截图已人工查看。Dev/Release 构建及 lintDev 通过。最终发布状态以 Android DEVELOPMENT_STATUS 为准。

## 当前图像边界

工作流导出 JPEG，不保留原图 EXIF/动画，输入原件完整保留。桌面使用 Windows 图像解码器，设备未安装的 HEIC/RAW 解码器会明确失败；不声称支持全部格式。压缩参数是 JPEG 质量控制，不保证任意 PNG 经转换后都变小。非缩放操作最长边限制 8192，超过 5000 万像素拒绝，避免任意大图耗尽内存。
