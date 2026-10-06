# 燕子：百度网盘专有能力（2026-10-05）

## 已实现能力

- `baiduNetdisk.status`
- `baiduNetdisk.open`
- `baiduNetdisk.upload`

## 技术路线

本机百度网盘：
`C:\Users\Administrator\AppData\Roaming\baidu\BaiduNetdisk\BaiduNetdisk.exe`

版本：8.8.3.101。

百度网盘没有发现稳定公开的 CLI 上传参数，但 Windows Shell 扩展真实注册了：

`上传到百度网盘`

燕子通过 `Shell.Application` 获取目标文件的 Verb 集合并调用该官方 Verb，而不是模拟鼠标点击客户端。

## 上传语义

`baiduNetdisk.upload` 当前只支持本地文件。

能力返回分两层：

- `triggered=true`：官方 Shell 上传入口已经接管文件。
- `confirmed=false`：当前客户端没有暴露可靠的“云端上传已完成”机器回执。

因此状态明确为：

`triggered_unconfirmed`

不会把“交给客户端”误报成“已经上传到云端”。

## 真实验收

2026-10-05 正式 Runtime API：

- installed=true
- version=8.8.3.101
- clientRunning=true
- 测试文件：`artifacts/baidu-provider-test.txt`
- Shell Verb：`上传到百度网盘`
- triggered=true
- confirmed=false

客户端进程由 BaiduNetdisk.exe 与 BrowserEngine/BaiduNetdiskUnite 子进程组成；未发现可靠传统主窗口句柄，因此本轮没有加入脆弱坐标 UI 自动化。

桌面、Runtime、Runtime Verifier 构建通过；Yanzi.CapabilityVerification 65/65。
