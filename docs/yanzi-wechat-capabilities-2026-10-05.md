# 燕子：微信专有能力（2026-10-05）

## 能力

当前新增：

- `wechat.status`
- `wechat.fileTransfer.sendText`

默认控制新版微信：

`C:\Program Files\Tencent\Weixin\Weixin.exe`

旧版仍保留为独立应用：

`C:\Program Files (x86)\Tencent\WeChat\WeChat.exe`

## 同时修复的应用发现问题

本轮先修复了底层应用目录，否则微信专有能力会出现“搜索到一个版本、执行另一个版本”的身份错误。

应用发现现在增加：

- 开始菜单 / 公共开始菜单
- 桌面 / 公共桌面
- shell:AppsFolder
- Registry App Paths
- HKLM/HKCU 32/64 位 Uninstall 注册表

去重规则从“同名应用只能保留一个”改为：

- 同启动命令去重。
- 同标题 + 同物理 exe 去重。
- 同标题但不同物理 exe 必须共存。
- KnownApplicationCatalog 的 ExecutableNames 顺序作为默认版本优先级。

微信定义为：

1. `Weixin.exe` —— 默认/新版。
2. `WeChat.exe` —— 旧版。

真实验收后：

- `微信` -> `C:\Program Files\Tencent\Weixin\Weixin.exe`
- `微信 (旧版)` -> `C:\Program Files (x86)\Tencent\WeChat\WeChat.exe`
- `app.open("微信")` 实际启动新版。
- Windows 应用目录从 235 个入口增加到 382 个。

## 微信状态机

### 1. 进程与窗口

优先查找 `Weixin.exe`。

没有可见窗口：

`app.open("微信")`

启动后等待真实窗口出现。

### 2. 登录态判定

新版微信当前是 Qt 客户端，主窗口类为：

`Qt51514QWindowIcon`

实际验证中 Windows UI Automation 几乎没有暴露可用的子控件，因此不能依靠 “Name=登录” 之类的 UIA Selector。

使用窗口状态作为第一层判据：

- 登录页实测约 `296 × 388`
- 登录后主窗口实测约 `690 × 707`

Provider 使用尺寸范围而非单一固定像素值：

- 小窗口 -> login
- 大窗口 -> main
- 其他 -> unknown，不盲目操作

### 3. 登录按钮

登录页先截图当前微信窗口，在窗口下半区识别微信主绿色按钮。

优先用绿色区域中心点点击；视觉检测失败时才使用经过真机验证的窗口相对位置兜底。

点击后观察窗口是否从 login 变成 main，而不是认为“点击成功=登录成功”。

### 4. 需要用户验证

点击“进入微信”后，若仍未进入主窗口：

1. 判断为 `verification_required`。
2. 截取当前微信验证窗口。
3. 通过燕子 `chat.send` 把截图发到用户手机。
4. 提醒用户扫码/按手机提示确认。
5. 最长等待 `loginWaitSeconds`。
6. 期间持续观察微信是否进入 main。
7. 超时则返回 `needsUserAction=true`，不继续乱点。

截图仅保存在燕子本地数据目录的 `WeChatCaptures` 中，并清理一天以前的临时截图。

## 进入文件传输助手

新版 Qt 微信实测：

- `Ctrl+F` 不可靠。
- UIA 无有效搜索框子控件。
- `KEYEVENTF_UNICODE` 中文输入不可靠。
- Qt / 输入法状态下 `Ctrl+A` 清搜索词也不可靠。

因此正式流程为：

1. 使用微信窗口的相对坐标定位搜索框。
2. 使用搜索框自带清除按钮，避免依赖 Ctrl+A。
3. 临时把“文件传输助手”写入剪贴板。
4. Ctrl+V 粘贴。
5. 立即恢复用户原剪贴板内容。
6. 点击顶部精确功能结果。
7. 聚焦消息输入区。

所有位置均相对当前微信窗口计算，不保存屏幕绝对坐标。

如果本次 Runtime 已经通过视觉标题哈希确认仍停留在文件传输助手，会直接复用当前会话，不重复搜索。

## 发送前保护

正式发送之前必须先验证消息确实进入输入框：

1. 临时保存剪贴板。
2. 聚焦输入框。
3. Ctrl+A / Ctrl+C。
4. 读取文本。
5. 与待发送文本做精确规范化比较。
6. 恢复原剪贴板。

如果不一致：

**立即停止，不发送。**

这样即使搜索/点击因为微信升级发生偏移，也不会把消息发给错误联系人。

## 发送

默认先按 Enter。

考虑到用户可能配置“Ctrl+Enter 发送”：

- 若 Enter 后聊天区没有变化；
- 且输入框仍完整保留原文本；

才允许安全尝试一次 Ctrl+Enter。

其他不确定状态不重试，避免重复发送。

## 发送确认

“调用了 Enter”不等于“发送成功”。

正式确认采用两个独立信号：

### A. 输入框清空

发送后聚焦输入框：

- 写入唯一剪贴板哨兵；
- Ctrl+A / Ctrl+C；
- 若剪贴板仍是哨兵，说明输入框没有文本；
- 最后恢复用户剪贴板。

### B. 聊天区发生变化

发送前后分别截图微信窗口，仅比较聊天内容区域，排除输入框光标等无关变化。

采样像素变化超过阈值才认为出现新的聊天内容。

最终：

`confirmed = inputCleared && chatChanged`

若输入框已清空但视觉确认不足：

- 返回 `status=uncertain`
- **不自动重发**

避免“实际上已经发送，但视觉检测失败”造成重复消息。

## 真实验收

2026-10-05 当前机器：

- `wechat.status`
  - running = true
  - state = main
  - loggedIn = true
  - window = 690 × 707

通过正式 Capability API 调用：

`wechat.fileTransfer.sendText({ text: "燕子微信专有能力端到端测试：已通过正式 Provider 发送" })`

返回：

- status = sent
- sent = true
- confirmed = true
- recipient = 文件传输助手
- navigation = search
- loginAction = already_logged_in
- sendShortcut = Enter
- inputPrepared = true
- inputCleared = true
- chatChanged = true
- visualDifferenceRatio ≈ 0.30535

能力注册、主项目、Runtime 构建均通过；Yanzi.CapabilityVerification 65/65 通过。

## 目前边界

当前专有 Provider 只承诺“文件传输助手文本发送”。

后续再扩展时建议分别增加：

- `wechat.contact.search`
- `wechat.chat.open`
- `wechat.message.sendText`
- `wechat.message.sendFile`
- `wechat.message.sendImage`
- `wechat.currentConversation.get`
- `wechat.login.status`

发送给普通联系人时必须继续沿用“发送前确认当前会话身份”的保护，不应该简单复用坐标后直接发送。
