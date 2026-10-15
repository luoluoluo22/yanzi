# 反重力 · 燕子小程序（Windows CLI 部署 + 手机聊天入口）

## 新电脑一键恢复

1. 在新 Windows 10/11（64 位）电脑安装燕子，登录燕子账号并同步此小程序。
2. 点击「反重力」：如果本机没有 Google Antigravity CLI，自动下载并运行 Google 官方 Windows 安装器；已安装则直接显示状态，不重复下载。
3. 首次使用选择「反重力」，输入 `/登录`。小程序打开本机 PowerShell 里的 `agy`，在浏览器手动选择并授权所需 Google 账号。
4. 授权后继续使用现有手机→电脑反重力聊天与消息回传。

小程序源码在 `%LOCALAPPDATA%\OpenQuickHost\Extensions\antigravity-chat\`，需要参与小程序同步。CLI 本体不进入扩展包，官方默认安装于 `%LOCALAPPDATA%\agy\bin\agy.exe`；同时支持 PATH 中已有的 `agy.exe`。

**环境依赖**：`manifest.requires: ["node>=22"]` 由燕子宿主的公共依赖管理器准备，未安装 Node 时无需小程序自己下载 Node。Antigravity CLI 由本小程序的 `setup.ps1` 在**用户显式点击或发出 /部署**时准备。

## 命令

- 点击小程序（不输入文本）：缺少 CLI 时自动部署；已安装时显示就绪状态。
- `/检查`：执行 `agy --version`，只读检测；缺失或执行失败不算成功。
- `/部署` 或 `/安装`：幂等安装，无需管理员；下载源固定为 `https://antigravity.google/cli/install.ps1`，临时文件执行后清除。
- `/更新`：已安装时运行 Google 官方 `agy update`，缺失时转为安装。
- `/登录`：在独立 PowerShell 终端启动 `agy`，由用户自行完成账号授权；不传输或同步凭据。
- `/状态`、`/新建`、`/取消`、`/结果`、`/恢复`：原有手机会话操作。

安装脚本使用当前用户目录、跨进程互斥锁以及安装后的版本校验，最多等待 5 分钟；网络、权限或账户问题不会被误报为部署成功。不会删除旧会话、重置 Google 凭据或在同步包中保存令牌。官方脚本会被下载并执行，依赖对 Google 官方 HTTPS 下载源的信任；若组织策略阻止脚本执行，应在本机按策略处理，不能绕过公司 IT 审批。

## 手机会话

手机燕子登录同一燕子账号，在「电脑」页刷新小程序目录。输入问题后选择「反重力」，先返回任务编号，完成后把结果回到发起手机的聊天会话。独立 CLI 会话不会覆盖桌面反重力的聊天。

任务命令：`/状态`、`/新建`、`/取消`、`/结果`、`/恢复`。手机反重力会话发送普通问题后，右下角“发送”会在等待 AI 回复期间切换为“停止”；点击停止时，如果任务尚未被电脑领取会直接取消云端排队消息，如果已经开始执行则 `/取消` 会创建取消标记并立即终止正在运行的 Antigravity CLI 进程树。取消后的完整 AI 回复不会继续回传。中断后 `/恢复` 只清锁不重做任务。首版仍固定规划模式，在独立 workspace 内运行，不直接以宿主仓库为工作目录。

数据在 `%LOCALAPPDATA%\OpenQuickHost\ExtensionStorage\antigravity-chat\`，包含会话、任务、回复、workspace；Google 登录与凭据由 CLI 和操作系统凭据库管理，未设置跨设备自动登录。后台桥接的 API token 仅以子进程环境传递，不写入数据文件，也不传给 CLI。

API：`POST /v1/extensions/antigravity-chat/run`，请求体 `{ "input": "问题" }`；复用燕子现有的设备消息机制，无需新增公网端口。手机送达需要实际 ACK，不能将“已提交”当作“已读”。

## 验证

- `powershell -NoProfile -File setup.ps1 -Mode check`
- `node --test tests/worker.test.cjs`
- 真正空白 Windows 电脑的首次下载、授权及完整消息回传仍须独立端到端验收。

仅安装 **Antigravity CLI**，不会额外下载 Antigravity 2.0 桌面版或 IDE。
