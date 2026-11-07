# 任务进度浮窗：方案一验收（2026-10-07）

- Source visual truth: `C:/Users/Administrator/.codex/generated_images/01a11616-8be0-70f0-b7e2-42e827908fc2/exec-82a14bed-3910-4e50-83e7-03b14dd024bc.png`
- Implementation screenshot: `C:/Users/Administrator/AppData/Local/Temp/YanziFloatUiVerification/live-running.png`（安装后的 Runtime 真窗口，PrintWindow 截取）。
- Full-view comparison: `C:/Users/Administrator/AppData/Local/Temp/YanziFloatUiVerification/comparison.png`，已将选中方案的组件裁切与原生 WPF 渲染放在同一张图中比较。
- Viewport: 460 × 164 DIP；真实窗口截图 460 × 164 px（100% DPI）；五种状态的 WPF RenderTargetBitmap 为 920 × 328 px（200% DPI）。方案图是展示画布，比较时只裁切浮窗区域，保持比例缩放，排除标题与状态色说明。
- State: 深色、微信文件发送前。原图“核对发送回执”仍显示中断快捷键存在业务语义冲突；实装提交前显示 Esc，提交后切换琥珀色和“已提交，无法撤回”。真实动作文案随任务变化。

## Findings / comparison history

- 第一轮 [P2] 图标库的 `chat` 别名使用了问号资源。已改为库中明确的 MDI `wechat`，手机使用 `cellphone`；后续五状态截图、真实窗口截图均已确认。
- 第二轮 [P2] 底部阶段区域缺少分隔线。已增加细分隔线，最新 comparison.png 与真实窗口截图已复核。
- 最终未发现未解决的 P0/P1/P2 视觉问题。标准库矢量图标、微软雅黑 UI、深灰底、左侧图标、三层文字、右侧状态胶囊及底部阶段/快捷键均已实现。

## Required fidelity surfaces

- Fonts / typography：微软雅黑 UI，身份 12、动作 20 半粗、对象 13、阶段 12、快捷提示 11 DIP。动作和对象单行省略；阶段保留独立区域，不挤掉快捷提示。
- Spacing / layout：460 × 164 DIP 原生浮窗，外围 4、内容左右 20 DIP，圆角 36 DIP；保留图标/文字/状态三列和进度/阶段两行。较概念图增加少量高度以适配实际桌面字体。原图是概念展示画布，不直接用作窗口背景图片。
- Colors / tokens：背景 #191E23；执行 #66B7FF、完成 #51D998、等待 #FFBF5C、失败 #FF6B75、取消 #A7B2BE。每种颜色同时配图标及状态文字。
- Image / assets：使用项目已有 MDI 矢量库，WeChat 标记、手机、加载、确认、警告和文档图标清晰。没有新增位图依赖。原图的重辉光/渐变简化为原生平面表面及细边框，是桌面组件的实现取舍。
- Copy / content：真实业务阶段；不显示虚构百分比。发送前可中断，提交后不得暗示撤回。查询联系人、打开会话、历史读取等非发送任务有各自标题，完成时使用“任务完成”。
- Focused comparison：组件本身已经完整放大到 920 px；图标、状态胶囊、阶段、快捷键均可直接阅读，另检查了五状态单独截图。

## Interaction evidence / limits

- 五种原生状态渲染及图标、状态文字、快捷键可见性检查通过。
- 隔离回调测试通过：单次 Esc 与按住键的重复 keydown 不取消；两次独立按键取消，且不发送消息。
- 任务记录回归 17 项通过。
- 安装后真实浮窗可见，标注小程序保持隐藏；真实任务结束后的自动关闭已观察。
- 真实键盘注入验收未通过：OpenInputDesktop 返回 Win32 错误 5（拒绝访问）。两次手机取消测试实际完成了测试消息发送，不能记录为取消成功。已增加提交前输入桌面检查；纯任务预览的 task.submit 被 `task_interrupt_service_unavailable` 拒绝，未执行业务发送。用户解锁后仍需复验全局 Esc。

## Follow-up polish

- [P3] 可后续增加更柔和的辉光，当前保留简洁原生渲染。

final result: passed

此结果仅指视觉及已列出的隔离交互检查；全局按键端到端验收仍受当前 Windows 输入桌面限制。
