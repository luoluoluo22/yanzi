# 燕子 Yanzi 1.0.8（2026-10-06）

## 修复

浏览器助手状态现在区分三种情况：

1. 浏览器助手已连接；
2. Edge / Chrome 等浏览器已启动，但燕子浏览器助手未连接；
3. 浏览器本身未启动。

新增本地接口 `GET /v1/browser/status`，让 AI、设置页和开发工具可以直接判断故障是在“浏览器进程”还是“浏览器扩展连接”。

## 性能

浏览器进程探测增加短 TTL 缓存；浏览器助手已经连接时不再枚举浏览器进程，避免开发窗口每秒刷新状态时反复扫描系统进程。

## 验证

- OpenQuickHost Release build：通过
- Yanzi.Runtime Release build：通过
- Local API domain/auth boundaries：14 checks passed
- git diff --check：通过
- 本机真实接口验证：Edge 运行、助手未连接时返回 browser_extension_not_connected
- Shared Runtime：保持 1 Runtime + 1 Shell，健康检查正常
