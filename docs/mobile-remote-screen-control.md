# 燕子 Android 远程屏幕控制（0.2.72）

## 范围与授权
- 沿用已经认证的 `capability.invoke` 云端设备消息协议；不开放任何手机 TCP 监听端口。
- “我的” → “远程屏幕控制” 默认关闭。需用户在手机上确认开启，并在系统无障碍设置启用燕子移动端。
- 手机已锁定、无障碍服务未连上或用户关闭开关时，所有远程屏幕读取和交互都拒绝执行。
- 手机上可随时关闭；密码节点在树中不返回文字。Android FLAG_SECURE 区域由系统自行保护。
- 屏幕截图与节点树在收到云端 ACK 后从本机持久化任务结果中清除；云端可能根据既有消息保留规则保存请求结果，需另行制定云端敏感结果保留策略。

## RPC 能力
通过原有设备通道向 K70 发出 `capability.invoke`：
- `mobile.screen.status` → `enabledByUser, accessibilityConnected, ready`
- `mobile.screen.capture` → `image` (base64 JPEG), `mimeType`, `width`, `height`（原始屏幕坐标系）
- `mobile.screen.tree` → 视图节点集合（上限 130；密码字段文本不返回）
- `mobile.screen.tap` → `x,y`（物理屏幕像素）
- `mobile.screen.swipe` → `x,y,x2,y2,durationMs`
- `mobile.screen.action` → `action` 为 `back/home/recents/notifications`
- 已有 `mobile.apps.list/info/open/openUri` 可继续使用，与远程屏幕开关独立。

## 注意
- 不提供远程任意 Shell、绕过手机锁屏、免授权截图、远程静默打开无障碍权限或静默安装 APK。
- 屏幕操作需要手机已经解锁。重启或被系统杀掉时可能需要重新连接无障碍服务。
- 远程触控是否实际落地仍需新版安装后在红米 K70 做端到端验收。
- 不应将截图 base64、屏幕文字、敏感节点明文打印进服务日志。
