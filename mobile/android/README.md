# 燕子移动端

> 当前真实开发状态请先阅读：[DEVELOPMENT_STATUS.md](./DEVELOPMENT_STATUS.md)。
>
> 该状态文档记录 Android Dev/生产包隔离、真实手机验证、Object Sync、跨端小程序数据、Cloudflare Worker 修复、测试入口、已知问题与下一步。继续手机端开发时以它作为冷启动入口。

最初 MVP 目标是：安卓端登录燕子账号，把文本或系统分享内容发送到同账号下的 Windows 燕子客户端。当前实现已经在此基础上扩展到账号级小程序目录、燕幕、手机小程序 runtime 与跨端 Object Sync。

## 当前能力

- 登录现有燕子账号。
- 自动注册当前 Android 设备。
- 支持系统分享文本到燕子移动端。
- 发送文本消息到 `desktop` 设备。
- 桌面端在线时会轮询云端消息队列并弹出通知。
- 手机登录后运行跨端消息前台服务，持续接收电脑云消息并上报设备在线状态。
- 电脑优先通过局域网推送通知；直连失败则转为云队列，手机显示通知后 ACK。
- 通知被禁用时云消息保持 pending，恢复通知权限后补收。

## 运行方式

用 Android Studio 打开 `mobile/android`，同步 Gradle 后运行 `app`。

如果当前机器无法从 Maven/Google 拉 Gradle 依赖，可在仓库根目录运行兜底构建脚本：

```powershell
.\scripts\build-android-mvp.ps1
```

输出 APK：

```text
mobile\android\app\build\manual-debug\yanzi-mobile-debug.apk
```

默认云端地址：

```text
https://sync.luoluoluo.cc.cd
```

## 约束

- 当前已包含文本、截图与文件相关链路；手机云接收服务当前消费 `notify` / `text`。
- 桌面通过 SSE 接收消息（Worker 内部约 2 秒查队列），5 秒 HTTP 轮询兜底；手机后台约 5 秒轮询。
- 心跳每 30 秒上报，在线状态采用服务端 120 秒窗口；网络失败会退避重试。
- 消息服务运行时显示常驻通知；强制停止 App 后不会自行唤醒，重新打开后补收。未接入厂商/FCM 系统推送。
- Dev 局域网接收端口为 `42982`，生产端口为 `42981`；通知接收需要发现时交换的 Bearer Token。
- 当前版本只支持登录已有账号，注册验证码流程复用网页版/桌面端。

消息真机回归：`scripts/test-real-phone-message-bridge.ps1`，使用临时账号和独立本地 Worker，备份并恢复 Dev 偏好设置，检查生产包不变。
