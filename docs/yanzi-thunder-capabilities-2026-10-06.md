# 燕子：迅雷专有能力（2026-10-06）

## 已实现
- thunder.status
- thunder.open
- thunder.addDownload

## 本机确认
- 主程序：C:\Program Files (x86)\Thunder Network\Thunder\Program\Thunder.exe
- 启动器：ThunderStart.exe
- 版本：12.4.12.4000
- Windows 已注册 thunder:// 与 thunderx:// 协议

## 设计
不依赖未公开的 ThunderCmd 私有参数；下载任务直接交给迅雷主程序及其系统协议入口。
支持：
- http
- https
- ftp
- magnet
- thunder
- thunderx

URL 做绝对 URI、scheme、长度和控制字符校验；参数通过 ProcessStartInfo.ArgumentList 传入。

## 验收
- 编译成功
- CapabilityVerification 65/65
- Shared Runtime 切换成功并加载全部 thunder.* 能力
- thunder.status 实际返回 installed=true、版本 12.4.12.4000
- 本轮未用真实下载 URL 调用 addDownload，避免在关机前产生无意义下载任务
