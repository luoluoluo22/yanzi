# 小红书增强：最小开发背景

## 身份

- 名称：小红书增强
- 稳定 ID：`xiaohongshu.filter`
- 目录版本：`0.7.24`
- 站点匹配：`https://www.xiaohongshu.com/*`
- 页面入口：`browser-extension/webapps/xiaohongshu-filter/content.js`
- 目录声明：`browser-extension/webapps/catalog.json` 中对应 ID 的条目。
- 该条目的同步键：`rules`、`settings`、`noteStats`、`customCards`。

## 代码导航

- 规则匹配、瀑布流过滤和作者/笔记屏蔽主要位于 `content.js`；重点定位 `applyRules`、`addRule` 与页面消息处理函数。
- 兴趣数据、AI 笔记、本地历史及兴趣面板也集中在同一个页面脚本；可用 `localFeedState`、`localFeedHistoryItems`、`ensureInterestPanel`、`renderInterestPanel` 搜索对应实现。
- AI 笔记生成、取消、评价和打开评价对话的消息由页面脚本发出，并在 `browser-extension/chatgpt-background.js` 中处理。当前消息类型包括 `yanzi_xhs_generate_local_notes`、`yanzi_xhs_cancel_local_notes`、`yanzi_xhs_evaluate_note`、`yanzi_xhs_open_evaluation_chat`。
- 安装注入与小程序数据同步由 `browser-extension/webapps/runtime-background.js` 管理；修改数据格式时检查页面脚本、运行时和目录 `syncKeys` 是否一致。

## 已确认的维护约束

- “探索空间”网络画布已从兴趣界面移除；不要重新添加该入口，也不要重新引入生成流程对探索节点前沿数据的依赖，除非任务明确要求重新设计这项功能。
- 历史笔记采用按需渲染：首屏 20 条，通过“加载更多”继续追加。改动历史列表时，检查按钮仍位于滚动列表内，并确认离开历史页后不保留整批卡片 DOM。
- 兴趣开关关闭后，不应继续从 ChatGPT 网页请求 AI 笔记；如修改相关开关，必须同时验证实际消息触发条件。
- 内容脚本体量较大。优先做小范围、可验证的修改，不要用整文件重写替代局部修复。

## 验证

```powershell
node --check browser-extension/webapps/xiaohongshu-filter/content.js
node --check browser-extension/chatgpt-background.js
node --check browser-extension/webapps/runtime-background.js
node -e "const c=JSON.parse(require('fs').readFileSync('browser-extension/webapps/catalog.json','utf8')); if(!c.apps.some(a=>a.id==='xiaohongshu.filter')) process.exit(1); console.log('catalog entry valid')"
git diff --check
```

涉及浏览器行为的修改，还应在真实小红书页面重载后验证：规则过滤、兴趣开关、AI 笔记请求、历史分页和数据同步。本文档描述的是代码导航与已知约束，遇到代码版本差异时以当前源码为准。
