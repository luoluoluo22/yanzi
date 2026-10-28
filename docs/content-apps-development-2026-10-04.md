# 三个应用中心 APP 首版（2026-10-04）

按“文件管理、抖音替代方向、小红书替代方向”拆成三个独立 Android APK，暂定名文件、燕流、燕记，版本 0.1.0 / code 1。内容应用是第一阶段的个人图文原型，不宣称已经具备公共社区、视频、跨账号分享、关注评论或云端内容同步。

源码仅位于用户目录：
- %LOCALAPPDATA%/OpenQuickHost/Extensions/yanzi-files/android
- %LOCALAPPDATA%/OpenQuickHost/Extensions/yanzi-stream/android、main.cs
- %LOCALAPPDATA%/OpenQuickHost/Extensions/yanzi-cards/android、main.cs

仓库仅保存应用中心发布 APK、元数据和验收文档，没有写入小程序源码，没有修改宿主或 Worker 的业务代码。

## 功能

文件：系统 SAF 授权目录、浏览/搜索当前目录、子目录返回、多选/全选、文件分享、永久删除、新建文件夹和重命名。仅操作用户授权目录，删除前确认；失败项保留。目录授权可跨重启保留。

燕流：单列大卡片图文浏览。
燕记：双列图文卡片浏览。

两者均有六篇标明“示例内容”的文字笔记，支持图文编辑、最多九张图片、草稿、发布到自己的内容列表、收藏、搜索、多选删除。图片通过系统选择器导入为 APP 私有 JPEG 副本，最长边 1600 像素，原图保留；取消编辑与删除内容清理无引用的私有图片，共享引用保留。内容当前只保存在各自 APP 本地。

## AI 协议

手机保持同签名 companion 权限，账号凭据由燕子主应用持有，手机不读取电脑的 ChatGPT Token。需电脑安装相应内容扩展，并运行 ChatGPT 后台工作台、连接已登录的浏览器助手。

桌面能力分别为 stream.content.generate / cards.content.generate。复用 files.workflow.run，手机提供只读、逐 URI 授权的 JSON 请求文件。第一次调用快速提交 ChatGPT 任务，返回 bridgeJobId；后续调用只查询这个任务，不再次提交生成。任务 ID 在扩展专属 ExtensionStorage/ai-jobs 下登记，不能查询其他应用创建的任务。查询和生成均返回小型 JSON 文件，经原有 SHA256 校验通道接收。

手机记录初始工作流 ID、Bridge ID、账号与目标电脑；每阶段是独立短请求，后续查询锁定原电脑，并检查原账号。APP 重开后继续查询。终态异常停止自动查询，保留原任务供人工核对；“结束等待”只结束 APP 等待，不取消或重发 ChatGPT 任务。成功以初始工作流 ID 幂等保存为 AI 草稿，必须由用户确认后发布。

开发初版将生成全程放在一次调用中，真机排队较久触发等待超时与网络回退，现已改为分阶段协议。开发早期还发生浏览器助手重载断连、页面忙、安装升级中断测试；这些均未伪装为成功。最终验证以分阶段版本为准。

## 验证

- 三个模块 Dev / Release 构建与 lintDev 成功。内容模块 AndroidTest 构建成功。
- API30 模拟器 FilesTest 通过：隔离真实 DocumentsProvider 文件，浏览、搜索、多选、取消、实际删除及剩余文件保留。测试目录提供器仅在 Dev，正式 APK 不包含它。
- 两个内容 APP 的 ContentTest 最终各 2 项通过：示例只初始化一次、草稿过滤、发布、收藏、取消/删除、重启保存、AI 导入幂等、图片最后一个引用移除后实际删除。破坏性内容测试强制 emulator-only。
- 一加 81f7e66d 只安装新的 .dev 应用，未清理已有主应用、笔记、相册数据。AiBridgeTest 保留原内容，只新增测试 AI 草稿。分阶段燕记 24.96 秒通过，燕流关闭电脑服务后的按需启动与完整回传 27.172 秒通过。
- 后续目标电脑锁定、账号复核及私有图片清理版本重新构建与 lint 验证；最终燕记真机完整回传 28.109 秒通过。
- Dev 使用现有 Dev 证书 ad4e...ddde；Release 使用主应用历史证书 8a0e...1ee3。正式 APK 包名、版本、签名经现有 application-release-ci.py 验证，不能把 Dev 签名当作正式发布签名。
- 使用独立 managed worktree 只提交本次三个 APK/JSON 与文档；通过 main 的现有应用发布 CI 上架，保留其他目录项，不进行本地 Worker deploy。

## 已知首版边界

当前是图文与本地文件功能起点。AI 接入验证了文字结构化生成；没有声称图片生成、视频、跨账号公共内容分发或完整 Android/OEM 兼容验证已完成。电脑启动与手机当前账号必须可正常通信；初次生成在多个在线电脑时需明确目标的交互尚未扩展，现有主应用会返回 TARGET_REQUIRED。
