# [OPEN] screenshot-403-waf

## 问题
- 手机端截图发送返回 HTTP 403，响应为 Cloudflare 拦截页。

## 现象
- 文本消息可发送。
- 截图流程中，无障碍截图成功。
- 在发送截图消息时收到 403。

## 假设
1. 不是域名问题，而是 `POST /v1/me/mobile/messages` 携带大体积 JSON 请求体时被 Cloudflare/WAF 拦截。
2. 不是单纯体积阈值，而是 payload 中的 `screenshotDataUrl=base64,...` 这种高熵特征触发了 WAF 规则。
3. 接口本身可用，但只要 `kind=screenshot` 且包含截图相关字段就会被规则命中。
4. 并非云端业务代码返回 403，而是请求在到达 Worker 前就被 Cloudflare 边缘层拦截。
5. 若改为较小 base64、无 base64、或仅发送 `webDavPath`，结果会出现可区分差异。

## 计划
1. 用脚本直接请求线上接口，复现并记录不同 payload 的状态码。
2. 对比文本消息、小 base64、大 base64、截图元数据无 base64 这几组结果。
3. 基于证据再决定是否保留直链、分片、改字段、或走 WebDAV 引用。

## 证据
- `GET /v1/auth/me` 返回 200，说明 Bearer token 本身有效，域名和基本鉴权可用。
- `GET /v1/sync/webdav-config` 返回 200，说明并非所有云端 API 都被拦截。
- `GET /v1/me/devices` 返回 403，响应体为 Cloudflare 拦截页。
- `POST /v1/me/devices` 返回 403，响应体为 Cloudflare 拦截页。
- `POST /v1/me/mobile/messages` 在以下请求体下全部返回 403，且均为 Cloudflare 拦截页：
  - 文本消息，约 135 bytes
  - 截图元数据无 base64，约 246 bytes
  - 截图小 base64，约 361 bytes
  - 截图中等 base64，约 5737 bytes
  - 截图大 base64，约 120276 bytes

## 当前结论
- 假设 1“仅大请求体触发拦截”已被否定。
- 假设 2“仅 `screenshotDataUrl/base64` 特征触发拦截”已被否定。
- 假设 4“403 发生在 Worker 前”已被强烈支持，因为返回的是 Cloudflare 拦截页而非业务 JSON。
- 更接近真实原因的是：当前出口 IP/请求特征被 Cloudflare 边缘策略拦截，影响 `/v1/me/devices` 与 `/v1/me/mobile/messages` 路由，而不是截图业务逻辑本身。
