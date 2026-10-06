# 燕子 Yanzi v1.0.7 发布说明

发布日期：2026-10-06

## 核心升级

- 引入 Shared Runtime：稳定后台能力与前台开发 Shell 分离。前台重启、调试和热更新时，截图、剪贴板、日历等常驻能力可以继续运行。
- 完善 Capability Network：能力由小程序/Provider 动态注册，上层 AI 通过统一语义能力调用，不再把应用逻辑写死在规划器里。
- 增加应用专有 Provider：微信、QQ、WPS、VS Code、Visual Studio、Git、浏览器、Blender、Unity/团结引擎、剪映、网易云音乐、百度网盘、夸克网盘、Bandizip、Everything、迅雷等。
- 浏览器能力提升为通用 Web Runtime：支持声明式 wait / fill / click / scroll / scrape，并复用现有浏览器扩展 WebSocket 通道。
- 应用发现增强：开始菜单、AppsFolder、App Paths、卸载注册表等多来源聚合；同名不同可执行文件不再互相覆盖，微信新版/旧版可正确区分。

## 跨设备与移动端

- Android 主应用推进至 0.2.54，完善电脑/手机任务路由、通知、消息恢复、应用中心和设备能力。
- 改进熄屏/后台消息唤醒、任务回执与恢复链路。
- 相册小程序 0.2.1：仅一台活跃电脑时自动选择发送目标。
- 强化跨设备对象、消息、附件的幂等、重试和权限边界。

## 开发与可靠性

- Git Provider 默认禁止 force push / reset --hard / rebase，pull 固定为 --ff-only。
- Visual Studio Provider 直接使用 MSBuild，Build/Rebuild 自动 restore，并返回结构化编译错误。
- Blender 后台 Python 调用使用 --python-exit-code，避免脚本异常却误报成功。
- WPS 直接使用 COM 创建 Writer/表格/演示并导出 PDF，避免脆弱的 UI 坐标自动化。
- Everything 搜索继续统一暴露为 files.search，Everything 专有能力只负责运行时和索引管理。
- Shared Runtime 安装/切换流程增加精确进程边界，避免多 Runtime / 多 Shell 残留。

## 发布前验证

本版本已通过：

- OpenQuickHost.sln Release 构建
- CapabilityVerification 65/65
- Shared Runtime 生命周期 17 项验证
- Sync / 权限 / 本地 API 边界验证
- Cloudflare / SDK / 设备协议 Node 测试 45/45
- ChatGPT Bridge 40/40
- Android Debug / AndroidTest / Release 构建
- Windows 1.0.7 Velopack 安装包候选构建
- 浏览器脚本语法、世界模型原型生产构建、相册发布输入校验
