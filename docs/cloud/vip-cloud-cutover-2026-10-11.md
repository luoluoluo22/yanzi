# 燕子云服务 VIP 付费与免费版 1.0 兼容

## 适用边界

- Windows 本地基础功能始终可用；用户已取得的安装包不会因为会员到期而停用。
- 自动升级：0.x 和 1.0.0 免费；1.0.1 以后的发行版，要求终身 VIP 或维护期覆盖对应版本的**真实发布日期**。发布日期未知时，不自动放行付费版升级；维护期内已取得的版本可继续安装。
- 云服务：`CLOUD_VIP_ONLY=true` 才启用统一授权。`/v1/*` 默认付费，只开放注册/登录/验证码/密码找回、会员状态、卡密激活、旧数据导出和版本发现。`/connect/` 外部授权、新版云应用目录及资源下载均检查所属账号 VIP。
- 老客户升级/找回数据：无需升级应用，访问 `https://sync.luoluoluo.cc.cd/upgrade`，登录账号后可购买、输入卡密、查看会员状态、下载本人**历史云同步对象 JSON**。仅存储在页面内存中的 Bearer，不写 localStorage、Cookie，也不写浏览器持久缓存。
- 旧数据导出：每页 100 个对象，按 revision 递增；只读、账号隔离，Cloudflare Rate Limiting 30 页/分钟。当前不含 R2 附件二进制、独立扩展安装包、外部 WebDAV/网盘；页面明确警告。这些未纳入的类型不应宣传为完整备份。
- 公共免费例外：`/health`、`/upgrade`、`/upgrade.js`、`/ai` 说明、Android `releases.json` 等更新引导及已公开的软件发布包。现有 GitHub 公开发布页的文件无法仅靠客户端检测限制第三方下载；若今后要求真正的安装包下载限制，应改为私有对象存储 + 付费认证的临时签名 URL。
- 自用的服务器端新闻定时任务 / 本地 ChatGPT gateway 不是普通用户云 API；消息节点仍受其所用 VIP 云账号权限约束。

## 发布顺序和回滚

1. 先以 **CLOUD_VIP_ONLY 未设置**的状态部署源码及自助升级网站（对旧客户端无行为改变）。检查 `/health`、`/upgrade`、`/upgrade.js`、历史导出权限、生产服务状态。
2. 在已登录用户和已过期用户模拟数据上验证 `vip_required` / `cloud-export` / `licenses/redeem`，以测试 Worker 入口和脚本验证。Windows 本地方案必须有构建和 `--vip-version-policy` 测试，Android Java 需编译。
3. 对外发布包含升级入口的客户端；老用户也可使用网页自助。历史数据保留原状，不做大规模 D1/R2 删除。
4. 在 `cloudflare/wrangler.toml` 设置 `[vars] CLOUD_VIP_ONLY = "true"`，通过正式 GitHub Action 发布，再验收 401/403/VIP200、429 登录限流、非 VIP 导出和卡密兑换；服务器节点应仍在线。**这是单独的生产切换提交**。
5. 回滚只需取消该 vars 并重新部署 Worker（不要回滚数据库、删除用户、清空历史数据）。

## 费用边界

- 无效登录可在进入 D1 前被 Workers Rate Limit 拦截。被限流的请求仍属于 Worker 调用；只有 Cloudflare WAF/规则在执行 Worker 前拦截，才能避免 Worker 执行。切勿依据可伪造的 `X-Yanzi-Client-Version` 字段授予会员或拒绝用户。
- `GET /health` 不再读 D1。VIP 查询采用 Worker 隔离进程内约 15 秒缓存（跨区域并不强一致）；真实会员状态及续期依旧来自 `users` 表。原有 1.0 客户端可能继续周期轮询并收到 HTTP 403，不具备新客户端本地冷却措施。
