# 燕子：QQ 专有能力（2026-10-05）

## 已实现

- `qq.status`
- `qq.self.sendText`

目标客户端：
`C:\Program Files\Tencent\QQNT\QQ.exe`

## 实现原则

QQ NT 主窗口当前为 Chromium 窗口 `Chrome_WidgetWin_1`，Windows UI Automation 没有提供可用的内部控件树，因此采用：

进程/窗口状态识别 → 窗口相对坐标 → 精确搜索“我的手机” → 输入前校验 → 发送 → 发送后双重确认。

所有点击位置都按照 QQ 当前窗口尺寸计算，不保存绝对屏幕坐标。

## 自传消息

`qq.self.sendText` 使用 QQ 自带的“我的手机”工具入口：

1. 确保 QQ 已运行。
2. 判断窗口是否已进入主界面。
3. 聚焦左上角搜索框。
4. 清空搜索框。
5. 临时写入剪贴板“我的手机”并粘贴，随后恢复原剪贴板。
6. 点击精确搜索后的顶部“我的手机”工具结果。
7. 聚焦消息输入区。
8. 临时粘贴待发送文本并恢复用户剪贴板。
9. 重新读取输入框内容，确认与待发文本完全一致后才允许发送。
10. 默认 Enter 发送；仅在能确定 Enter 没有发送而只是保留原文本时，才尝试 Ctrl+Enter。
11. 发送后同时检查输入框是否清空、聊天内容区域是否发生变化。
12. 两个判据同时满足才返回 confirmed=true；不确定时绝不自动重发。

## 登录边界

当前机器 QQ 已登录，因此登录页没有做真实点击回归。

Provider 已有保护逻辑：如果 QQ 可见窗口尺寸不符合主界面，会把当前 QQ 窗口截图发送到燕子手机，等待用户完成登录/验证；在进入主窗口前不会继续发消息。

## 真实验收

2026-10-05：

- `qq.status`
  - running = true
  - state = main
  - loggedIn = true
  - window = 1121 × 931

正式 Capability API 调用：

`qq.self.sendText({ text: "燕子QQ正式能力端到端测试：Provider 调用成功" })`

结果：

- status = sent
- sent = true
- confirmed = true
- recipient = 我的手机
- sendShortcut = Enter
- inputPrepared = true
- inputCleared = true
- chatChanged = true
- visualDifferenceRatio = 0.11302

桌面构建、Runtime 构建、Runtime Verifier 均通过；Yanzi.CapabilityVerification 65/65 通过。
