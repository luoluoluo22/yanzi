# 延时关机 0.3.5：暂停/继续、日落后开灯、开灯失败仍关机

## 已实现

- 图标背景 `accentHex=#DC3545`（红色），保留深色倒计时窗口。
- 保存 manifest 后，宿主文件监听器 650 毫秒防抖刷新小程序目录、燕环轮盘和快捷面板；轮盘执行时按 ID 重新读取磁盘上的最新定义。
- `POST /v1/extensions/shutdown-timer/reload` 会编译并刷新菜单；**尚未运行时绝不启动倒计时**。正在倒计时的延时关机拒绝自动重载，必须先手动终止。
- 关机倒计时结束时先查询 Home Assistant `sun.sun`：`above_horizon` 为日出至日落，直接关机；`below_horizon` 为日落至次日日出，尝试调用 `light/switch.turn_on` 打开卧室灯并验证设备状态。日落计算使用 Home Assistant 配置的**家庭位置**，请检查该位置是否准确。
- **开灯不再是关机前置条件**：查询太阳状态失败、Home Assistant 离线、开灯失败或总计六秒超时，都跳过后续开灯并继续执行关机；日志记录跳过原因（不含令牌）。无法判断日落时不主动开灯。只有用户主动暂停/关闭窗口，或系统关机命令本身执行失败，才阻止正常完成。
- “终止关机”改为暂停且窗口保留；点击“继续关机”恢复倒计时，暂停时允许进入设置测试卧室灯；右上角 × 彻底退出。
- `--preview` 无倒计时、无真实开灯或关机；`--settings` 同样安全，并自动打开时长设置。

## 开通米家联动（需要用户完成账号授权）

已在本机 WSL2 Docker 中部署 Home Assistant，使用者已完成 Xiaomi Home 授权，并确认卧室灯由 `switch` 实体控制。智能家居访问令牌已通过 Windows DPAPI 加密保存在本机。

1. 准备 Home Assistant，并按小米官方说明安装/登录 [Xiaomi Home 集成](https://github.com/XiaoMi/ha_xiaomi_home/blob/main/doc/README_zh.md)。需要在 Home Assistant 界面使用小米账号执行 OAuth，并导入包含目标灯的家庭。
2. 在 Home Assistant 的“开发者工具 → 状态”里找到目标灯的 `light.xxx` 或 `switch.xxx` 实体 ID；在用户资料页生成长期访问令牌。
3. 打开延时关机设置（小程序运行参数 `--settings`；不会执行关机）→“米家开灯联动…”；填写 Home Assistant 地址、目标灯实体 ID 和令牌，并勾选“日落后关机前开卧室灯（失败仍关机）”。
4. 设置中的“设备测试”提供三个独立按钮：**读取状态**（只读）、**测试开灯**和**测试关灯**（分别发出真实设备控制指令并确认状态）。它们与系统关机流程独立，按钮仅在用户明确点击时控制设备；测试不会保存未保存的设置。
5. 点击“保存配置”保存联动开关、地址、设备实体及加密令牌；重新打开时令牌显示已保存占位，留空不会清除旧令牌。
6. 正常延时关机从不调用 `turn_off`：白天无需开灯，夜间先尝试开灯，但成功与否均继续关机；主动暂停或退出时不会发出后续关机指令。

### 凭据与网络

- 长期令牌使用 Windows DPAPI CurrentUser 在本机加密，以 local 范围写入小程序存储，绝不写入 manifest 或日志。
- 支持 Home Assistant HTTP/HTTPS，建议 HTTPS（普通 HTTP 会在局域网明文传输 Bearer Token）。
- 禁止自动跟随跨站重定向；根据实体前缀选择 `/api/services/{light|switch}/turn_on` 或仅供手动测试的 `/turn_off`，通过 `/api/states/{entity_id}` 核实结果。
- 小米官方集成可能通过云端或本地网关控制设备；状态 `on` 是 Home Assistant 的报告，不能代替物理光照传感器验证。
- 本地测试使用虚拟 Home Assistant 服务和安全预览，不会对真实设备执行操作。

## 项目备份与测试

修改前的 `manifest.json` 备份在：
`F:\Desktop\kaifa\OpenQuickHost\.artifacts\shutdown-timer-mi-home\manifest.before.json`。

编译检查：`POST /v1/extensions/shutdown-timer/build`。
安全预览：`POST /v1/extensions/shutdown-timer/run`，请求体 `{"input":"--settings"}`。关闭预览使用 `/stop`。
