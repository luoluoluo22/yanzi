# 燕子移动端 MVP

第一版目标：安卓端登录燕子账号，把文本或系统分享内容发送到同账号下的 Windows 燕子客户端。

## 当前能力

- 登录现有燕子账号。
- 自动注册当前 Android 设备。
- 支持系统分享文本到燕子移动端。
- 发送文本消息到 `desktop` 设备。
- 桌面端在线时会轮询云端消息队列并弹出通知。

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

- 当前版本只做文本链路，不处理图片/文件。
- 当前版本使用 HTTP 消息队列，桌面端约 5 秒内收到；后续再接 Durable Objects WebSocket。
- 当前版本只支持登录已有账号，注册验证码流程复用网页版/桌面端。
