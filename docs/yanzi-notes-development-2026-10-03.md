# 燕子笔记手机 APP 与电脑同步

2026-10-03：沿用相册的独立 APK / 同签名燕子宿主桥接 / 应用中心分发方式。业务源码位于 `%LOCALAPPDATA%/OpenQuickHost/Extensions/yanzi-notes/`；Android 在 android/，桌面保留现有 NoteGen，增加 extension 内同步适配脚本与目录读取修复。仓库只包含通用框架桥接、构建/分发/验证入口。

身份 `cc.luoluoluo.yanzi.notes` / `.dev`；应用名燕子笔记；版本 0.1.0。桌面扩展 manifest 升至 0.2.0。最低手机宿主 versionCode 27，与宿主同证书，extensionScopes 仅 yanzi-notes，凭据不离开主应用。正式目录应用项为 yanzi-notes / 笔记。

Windows WebView 通用存储新增 storage.get(key,{scope:'local'})、storage.accountRead(key) 与 storage.accountWrite(key,content,expectedRevision,accountId)。账号访问固定当前扩展 ID 与 namespace，拒绝缺失/负 revision、账号变化和 >256 KiB 内容；CAS 返回 conflict。LocalAgentApi 的已认证 /v1/extensions/{id}/run 也支持 webview app 打开/单实例激活；opened 只表示窗口创建，不宣称同步完成。

共享对象 `extensionId=yanzi-notes,key=notes/sync.v1.json`。schemaVersion:1、records（article/*.md 路径为键）；record 为 version/deleted/item，item 的 content 是完整 Markdown，updatedAt 为 ISO UTC。整体对象 CAS，按记录 version 合并；正在编辑的旧版本与云端新版本冲突时保留本地。删除保留墓碑。手机离线修改/草稿持久化并按账号隔离；电脑账号切换暂停原文件集，避免导入到新账号。电脑更新/删除文件前备份到 ExtensionStorage/yanzi-notes/notes/sync-backups，路径拒绝 .. 等越界段。电脑需打开笔记窗口同步，手机前台每 10 秒同步，保存立即尝试。

验证：Android Dev/Release 与 lintDev 通过；桌面 Debug 0 错误（现有警告保留）；Node 2 项同步测试通过；隔离 Worker 的 Windows→手机→Windows 3 项 instrumentation 通过（真实 Provider、中文往返、CAS、跨 scope 拒绝、模拟离线持久化、同篇冲突、墓碑、账号隔离、编辑草稿恢复）。最终产物 `%TEMP%/YanziNotesTest-25d4197f7d994ee6a07a6d976ea2b28f`，notes-editor-qa.png 已查看，未覆盖用户正式包。真实一加只安装笔记 Dev，并核对正式燕子 APK 路径/versionCode 不变。

分发脚本增加 -NotesApkPath / -NotesOnly，检查 package/signature/hash，只替换 notes 应用项，保留其他线上项。0.1.0 已上传 R2 并发布目录；正式下载端 APK SHA-256 与目录匹配，线上相册仍为 0.2.0、日历仍 0.1.1。未部署 Worker，未推送仓库中其他在途更改。

当前边界：Markdown 正文和路径同步；附件、图片渲染、富文本预览、后台同步尚未实现。集合序列化后 256 KiB 上限，超限明确报错并保留本地；不得宣称无限笔记或附件完整同步。当前电脑数据首次导入以打开笔记并成功同步为准，不能将测试账号往返等同于真实账号导入。
真实电脑 WebView 已通过认证 API 打开，当前账号首次导入 9 篇 Markdown，账号对象记录数 9 已验证（不输出正文）。
真实手机 Dev 同账号 9 篇、待上传 0；逐篇版本、墓碑与正文一致性检查通过，未输出笔记正文。

## 后台同步与三版 UI 方案

手机 0.1.1（versionCode 2）、桌面小程序 0.2.1：手机用持久化 JobScheduler，联网时一次任务与 15 分钟周期任务、指数退避，开机/升级恢复。Activity 与 JobService 共享单例 store，通过 syncGate 串行网络合并，后台继续使用原账号隔离、CAS、记录冲突和持久 outbox。系统强行停止应用后需再次打开，省电/Doze 下执行时间由 Android 决定。草稿仅保存本地，点击保存后进入同步队列。

电脑宿主新增通用 app.runInBackground 可选声明；关闭窗口隐藏并保留 WebView，宿主退出才释放。startup.mode=on_app_launch 时最小化静默初始化并隐藏，重开复用窗口。后台宿主 30 秒 tick 派发 yanzi:background-tick，避免依赖隐藏 WebView 的 JS 定时器；业务同步处理仍在用户扩展目录。电脑燕子必须保持运行。

Android Dev/Release/lintDev 通过；Node 3 项通过（含无 JS 轮询的原生后台 tick）。隔离往返和原 UI 草稿 3 项 instrumentation 通过；再杀进程后强制运行系统任务，验证后台下载桌面新记录和上传持久 outbox（准备 1 项 instrumentation），无 Activity。产物 `%TEMP%/YanziNotesTest-bc4c2088943f4e978485cae9b991ba5e`。正式应用中心 notes 0.1.1 已更新，下载 SHA 匹配，其他应用版本保留。真机 Dev 覆盖安装被自动审批 blocked by policy 拒绝，未更新真机安装包。桌面构建后已重启。

UI 已分别生成 3 版设计图并按展示顺序编号。用户目录 yanzi-notes/design-options/option-{1,2,3}.png 保存参考图，等待选择或修改意见；本次只改后台功能，尚未应用这些 UI。
## 当前状态：第一版深色 UI 与事件驱动同步

以此章节覆盖此前 10 秒/15 分钟/30 秒 tick 描述：notes 手机 0.1.2（code 3），桌面笔记 0.2.2，手机燕子 0.2.34（code 34），保存上传账号对象后通过现有 durable device messages 发送 extension-storage.changed（仅 extensionId/key/revision，不发送正文）。手机 Provider/电脑 AccountExtensionDataStore 通用桥接发消息；手机 DeviceHeartbeatService 静默派发到已注册、签名与声明 scope 匹配的 companion；receiver 受宿主签名权限保护，检查账号/key。电脑静默派发 yanzi:storage-changed，不写聊天或弹通知。原后台 WebView 保活保留，30 秒原生 tick 移除。复用现有 WebSocket/SSE/持久消息，不新增独立笔记长连接；燕子通道本身仍有心跳/断线兼容补偿。

手机移除 10 秒轮询和 15 分钟周期 Job，升级时 cancel 旧 periodic ID；保存/变更事件触发一次 Job，合并队列，异常由系统退避重试；无账号或签名错误不循环重试。打开/重连时读取补偿，未保存草稿只存本机。Activity 和 JobService 共用单例+syncGate，UI 监听本机 sync-status 改变刷新。电脑移除 setInterval，保存后 sync、通知后 sync、重连补偿；网络/CAS 错误最多 5 次退避，新事件可恢复。对象 CAS、记录版本、冲突、账号隔离和备份继续使用。

深色 UI：本轮第一张 reference-dark/option-1.png，标题/搜索/横向真实目录分类/动态双列卡片/暖黄加号/笔记和同步导航。无对应附件时仅显示真实文本，未把设计照片变成虚假笔记。Google Material Icons 3.0.1 原版资源，license 随源码。纯 native Android，不新建网页。QA 对照图与 design-qa.md 在用户扩展目录，修复旧图标、导航文字对齐、异步截图旧帧和状态文案，final result passed。

验证：Dev/Release/lintDev、桌面 Debug、Node 3 项通过；隔离 UI 搜索/分类/新建、草稿、冲突/CAS/删除共 4 项 instrumentation，通过真实主应用消息唤醒已关闭笔记进程，闲置 12 秒无定时同步、第二次新事件、未签名合法形状广播不能唤醒、后台 outbox 上传和反向 metadata 消息均通过。最终隔离产物 %TEMP%/YanziNotesTest-1fe268afea9b45008990ed3af734f515。一加仅 Dev 主应用和 Dev 笔记已覆盖安装（保留数据），生产燕子仍 0.2.26；未向真机注入测试账号或清空数据。

本次正式版 APK 构建完成供后续发布；正式应用中心依旧 notes 0.1.1，未把未发布主应用桥接依赖伪装成线上生效。发布脚本新 notes minHostVersionCode=34。本次未部署 Worker/未 push Git/未创建公共主应用 Release。

## 当前状态：0.1.3 全屏详情与已删除

手机详情参考用户 2149.jpg，保留炭黑暖黄。全屏正文编辑取代 AlertDialog，顶部返回/分享/保存/更多，标题/时间/字数/长正文，草稿落地与配置恢复。已有文件名只读，正文可编辑；返回保存，稍后继续保留草稿。首页标题与目录设置同一行，底部同步改为已删除，已删除详情只读、确认恢复后同步；隐藏回收页新建入口并更新导航选中状态。桌面适配 0.2.3 在删除墓碑保留正文，历史空内容无法追溯恢复。

NotesStore 新增按 deleted 状态查询，恢复仍走既有记录版本 CAS 与持久 outbox，未新增定时轮询或宿主专属业务。业务全部位于用户 yanzi-notes 目录。Android Dev/Release/lintDev 通过；Node 4 项（新增电脑删除保留正文及手机恢复后文件重建）通过；隔离 instrumentation 4 项与系统后台 Job/消息唤醒/无周期同步回归通过，产物 `%TEMP%/YanziNotesTest-bded8cec8bf54f9f8b75d78df614ac47`。官方 Material 图标，首次误用 reply 的 P2 已修正，再捕获截图、并排 QA passed。正式应用中心仍为旧发布，当前版本仅本地测试交付。


## 当前状态：指定电脑接续（手机 0.1.4 / 宿主 0.2.35 / 桌面适配 0.2.4）

手机详情更多与电脑笔记左下增加“在电脑上打开”。保存并先完成账号同步，选择同账号 desktop，发指定设备 extension.handoff（仅 extensionId/accountId/路径/动作，不发正文）；目标运行新版燕子后拉取最新 Markdown、打开笔记小程序与对应 tab/editor。24 小时过期，目标离线时留在云端；没有远程开机功能，也没有额外周期笔记同步。手机提供查看结果，电脑有查看结果入口；消息发出不冒充已打开。

仓库仅增通用 CompanionTransferProvider handoff/handoff-status、WebView handoff.devices/open/status 与 generic OpenResourceAsync：same-signature、extensionScope、companion yanzi.handoff opt-in；桌面 app.bridge.apis opt-in，限制发送自身 extensionId；同账号、指定目标、参数长度、过期、稳定 ID 及持久回执检查。业务接收路径校验、同步、冲突/删除拒绝和 NoteGen store 激活全部在用户扩展目录。NoteGen async webpack module 69242 是实际文件 store；捕获官方 runtime 获取并调用 addTab/setActiveTabId/setActiveFilePath/readArticle，不是 DOM 模拟点击。嵌套中文路径测试通过。通用 handler 可返回 extension entry 根内的 HTML navigate，宿主待加载后重新派发；实际桌面 NoteGen 页面为 core/main.html。

线上旧消息不包含 authorization，初次真实云端请求被严格检查拒绝，已修正为经当前账号 authenticated /me message lookup 校验源/指定目标/账号/原输入；有 grant 时仍限定 account-owner。未手动部署 Worker，不需要后端变更。重试真机真实 cloud handoff 1 项通过（14.662 秒，完成回执）；Dev 两包已安装打开，生产包 code26 未变。初次 USB 中断后重连并以 push install 完成。

验证：桌面 0 errors；Android app/notes Dev、Release、notes lintDev 通过；Node 7 项；隔离模拟器 UI/Provider/同步 5 项加后台事件/outbox 回归，产物 `%TEMP%/YanziNotesTest-c5b0e162cdd94b608685573d6dee6b27`。`scripts/test-notes-handoff.ps1` 验证真实编辑器/path、重复请求复用持久回执、错误账号、不存在笔记、过期拒绝。PhysicalHandoffTest 只显式调用真实 Provider，不清数据、不创建笔记、不写 fixture，云端 completed 回执已通过。正式应用中心尚未发布此迭代。
