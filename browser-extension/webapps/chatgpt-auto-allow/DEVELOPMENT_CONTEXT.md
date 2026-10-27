# ChatGPT 自动允许：最小开发背景

## 身份

- 名称：自动允许
- 稳定 ID：`chatgpt.auto-allow`
- 目录版本：`0.1.0`
- 站点匹配：`https://chatgpt.com/*` 与 `https://chat.openai.com/*`
- 页面入口：`browser-extension/webapps/chatgpt-auto-allow/content.js`
- 目录声明：`browser-extension/webapps/catalog.json` 中对应 ID 的条目。
- 该条目的同步键：`settings`。

## 代码导航

- 页面脚本检测 ChatGPT 对话中出现的 MCP 与外部工具调用授权确认框（卡片/弹窗）。
- 当包含“始终允许”（Always allow）或“允许一次”（Allow once）的确认框出现时，自动启动 15 秒（默认）倒计时。
- 倒计时过程在操作区域直观呈现倒计时徽标与提示条，支持鼠标悬停暂停与一键取消。
- 倒计时归零时，自动触发点击“始终允许”（或次级备选“允许一次”），实现免打扰高效交互。
- 用户手动点击任意按钮（“始终允许”、“允许一次”、“拒绝”）或按下 Esc 时，立即中止倒计时，绝不干扰用户自主决策。
- 设置包含：自动允许总开关 `enabled`、倒计时秒数 `countdownSeconds`（默认 15 秒）、优先点击模式 `preferAlways`。
- 数据通过 `yanzi_webapp_storage_get`、`yanzi_webapp_storage_set` 与扩展运行时及云端同步。

## 已确认的行为约束

- 严格遵循 UNIX 静默哲学：无弹窗确认框时不产生干扰，自动允许完成后仅在卡片轻微呈现微秒级状态反馈。
- 绝不重复触发：同一卡片或已被处理的授权框在会话生命周期内只触发一次自动点击。
- 容错降级：优先点击“始终允许”，若页面仅提供“允许一次”，则降级点击“允许一次”，并做好可见性检测。

## 验证

```powershell
node --check browser-extension/webapps/chatgpt-auto-allow/content.js
node --check browser-extension/webapps/runtime-background.js
node -e "const c=JSON.parse(require('fs').readFileSync('browser-extension/webapps/catalog.json','utf8')); if(!c.apps.some(a=>a.id==='chatgpt.auto-allow')) process.exit(1); console.log('catalog entry valid')"
git diff --check
```
