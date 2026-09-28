# 小爱电脑控制（独立燕环小程序）

> 版本 0.2.2：已验证 L05B 云端能够返回真实对话记录；语音主前缀为「电脑」（兼容旧「燕环」）。新增显式 `--listen` 启动模式，以便在用户授权后同时启动 HA 事件和小爱云端监听，无须在正在交互的面板上执行 UI 自动化；正常启动仍不自动监听。历史口令只提示，不自动重放。

## 安装位置

- 已安装：`%LOCALAPPDATA%\OpenQuickHost\Extensions\xiaoai-pc-control\manifest.json`
- 源码：`F:\Desktop\kaifa\OpenQuickHost\.artifacts\xiaoai-pc-control\main.cs`
- 云端适配器：`F:\Desktop\kaifa\OpenQuickHost\.artifacts\xiaoai-pc-control\mina-adapter.py`
- 本机安全登录：同目录 `login-local.ps1`、`login.py` 和 `secure_store.py`
- 仅存储当前 Windows 用户 DPAPI 加密会话：`%LOCALAPPDATA%\OpenQuickHost\Xiaoai\mina-auth.dpapi`

## A. Home Assistant 事件桥

- 复用延时关机已保存的 Home Assistant 本机 DPAPI 令牌，只在当前 Windows 用户的进程内解密；**不复制明文**，也不向其他进程输出。
- 状态栏检查已接入的 `media_player.xiaomi_cn_504069354_l05b`（小爱音箱 Play）。
- 点击「启动事件监听」：通过 HA WebSocket 订阅 `yanzi_pc_intent` 事件。
- 在面板「发送 HA 模拟事件」会向 `POST /api/events/yanzi_pc_intent` 发送 `{"command":"ping"}`，验证事件总线至 Windows 面板的链路。
- Home Assistant 自动化的「触发事件」动作可发送 `yanzi_pc_intent`，数据 `command: open_notepad` / `open_calculator` / `ping`。真正来自小爱语音的 HA 触发源尚未配置；**小爱音箱的执行文本指令接口是电脑→音箱，不等于音箱→电脑的语音识别回调**。

## B. 小米云端会话兼容桥

- 面板「启动本机接收」只监听 `http://127.0.0.1:55483/voice`。
- 第三方适配器通过 `POST /voice` 传入 `{"text":"open_notepad"}`，请求头带 `X-Xiaoai-Bridge-Key`。使用面板「复制桥接密钥」获取令牌，**不要提交到 Git、聊天、日志或网页**。
- 「模拟云端语音」只发送 `电脑状态`，不打开程序或发起关机。
- `mina-adapter.py` 是基于 XiaoGPT 公共的 Mina 会话查询思路独立编写的按需轮询示例。它不拦截 HTTPS 流量、不修改音箱固件、不记录全部对话，只转发特定「电脑…」前缀（兼容旧「燕环…」）和白名单词语。
- 在新面板点击「重新授权小米账户」，由**本机可见 PowerShell** 启动独立 Python `MiService` 登录流程；仅在本机输入账号、密码及可能要求的短信/邮箱验证码。登录成功后，从 Mina 设备列表筛选 L05B，使用 Windows 用户 DPAPI 加密保存 `micoapi` 会话，绝不保存账号密码。Home Assistant OAuth 与 Mina API 的授权范围不同，不可混用。
- 此前找到旧 `~/.mi.token` 文件，但其云端会话在 2026-09-27 返回 HTTP 401，旧 passToken 刷新亦返回 70016；未复制旧凭据，也未更改原文件。
- 在本机登录成功后，返回面板点击「启动真实语音监听」，程序自动启动仅绑定 `127.0.0.1:55483` 的桥接服务和独立 Python 轮询进程。每五秒请求 Mina 会话记录（最多取 10 条），不保存普通聊天内容；仅处理新出现的白名单口令。网络异常指数退避。离开面板会终止监听进程。需要一次性同时启动两通道时，使用小程序输入 `--listen`；正常启动不会自动监听。
- 语音验收先说：「小爱同学，电脑状态」，观察面板日志是否依次出现「收到新语音，识别为：电脑状态」「云端语音已转发：ping」和「电脑在线，事件链路正常」。随后可尝试「小爱同学，电脑打开记事本」，但只有手动勾选「允许来自已连接通道的指令操作电脑」后才实际打开程序。历史中的旧口令只提示存在，绝不自动重放。
- 每五秒读取至多 10 条近期记录；普通对话只显示未匹配条数，不在本机保存全文或上传聊天。若刚启动监听，必须说一条**新指令**；已存在的历史记录不会触发电脑操作。

## 电脑安全与限制

- 新面板默认**不允许控制电脑**。勾选「允许来自已连接通道的指令操作电脑」才会执行白名单程序；`ping` 查询始终可用。
- 只允许启动记事本、计算器及本机在线测试；没有关机、任意命令执行、浏览文件或远程桌面入口。
- 来自 HA 的事件要求已有有效 HA 用户令牌；本机云端接收必须带随机生成、DPAPI 加密存储的桥接密钥。
- 监听仅在管理面板运行且手动启动后有效，退出面板即停止。不会因安装就自动开机监听。
- **当前验证阶段：** 用户已在本机重新完成 micoapi 授权。Mina API 已返回真实 13 位毫秒时间戳的对话记录，包含“电脑”字样；尚需用户更新小程序后说一条新口令，确认实时转发和可选白名单执行。

参考：[小米官方 Xiaomi Home 集成](https://github.com/XiaoMi/ha_xiaomi_home)、[XiaoGPT](https://github.com/yihong0618/xiaogpt)。

