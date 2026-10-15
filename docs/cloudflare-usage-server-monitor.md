# 燕子 Cloudflare 用量监控（远程 Ubuntu）

- 数据来自 Cloudflare GraphQL Analytics API，只读。
- 监控整账户 D1 Rows Read / Rows Written / Workers Requests 与 yanzi-sync 细分情况。
- 免费日额度分别为 5,000,000 行、100,000 行和 100,000 次；UTC 00:00 重置。
- 定时：每小时第 10 分钟抓取当前用量；每天 UTC 00:25（北京时间 08:25）复盘刚结束的完整 UTC 日。
- 机器报告：`/var/lib/yanzi-cloud-monitor/live.json`、`latest.json`、`daily/YYYY-MM-DD.json`。
- 严禁将 API Token 写入仓库、任务配置、日志或报告。部署时应创建单独的 Cloudflare **Analytics Read** 最小权限 Token，并用 root 私有的 `/etc/yanzi-cloud-monitor/credentials.env` 配置：`CLOUDFLARE_API_TOKEN=...`，文件权限 0600、父目录 0700。
- 部署方法：以 root 在服务器上运行 `scripts/cloudflare/install_usage_monitor.sh`。此脚本只安装作业程序与 systemd timers，不改 Worker 或 D1。
- 人工验收：`systemctl list-timers 'yanzi-cloud-*'`；`systemctl start yanzi-cloud-usage-daily.service`；`journalctl -u yanzi-cloud-usage-daily --since today`；`cat /var/lib/yanzi-cloud-monitor/latest.json`。
- 自动化边界：采集和异常识别可无人值守；源代码与 D1 migration 的生产变更必须经过隔离工作树、测试、PR/CI、生产健康检查。不得在服务器上每晚直接覆写 Worker。
- 当凭证未配置时，任务写出 `last_error.json` 并失败，避免误报“0 用量”。
