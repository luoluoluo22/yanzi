# 燕子：夸克网盘专有能力（2026-10-05）

## 已实现能力

- `quark.status`
- `quark.cloudDrive.open`
- `quark.cloudDrive.openPath`
- `quark.cloudDrive.openUri`

## 原生接口

本机夸克：
`C:\Users\Administrator\AppData\Local\Programs\Quark\quark.exe`

版本：7.2.0.992。

Windows 注册了：

- 协议：`qkclouddrive://`
- 网盘启动模式：`quark.exe --brand-clouddrive`
- 文件/路径交付：`quark.exe --brand-clouddrive <path>`

因此燕子直接调用系统已注册的稳定入口，不使用 UI 坐标。

## 安全边界

`quark.cloudDrive.openUri` 只允许 `qkclouddrive://` scheme，拒绝任意 URI。

`quark.cloudDrive.openPath` 要求本地文件或目录真实存在。

## 真实验收

2026-10-05 正式 Runtime API：

- status：installed=true，version=7.2.0.992
- cloudDrive.open：成功，processId=9776
- cloudDrive.openPath：成功把测试文件交给夸克网盘模式，processId=26932
- 测试路径：`artifacts/baidu-provider-test.txt`

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
