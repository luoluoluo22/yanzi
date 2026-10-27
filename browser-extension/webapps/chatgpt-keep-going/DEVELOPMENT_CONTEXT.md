# ChatGPT 不要停（自动续聊）：最小开发背景

## 身份

- 名称：不要停
- 稳定 ID：`chatgpt.keep-going`
- 目录版本：`0.2.0`
- 站点匹配：`https://chatgpt.com/*` 与 `https://chat.openai.com/*`
- 页面入口：`browser-extension/webapps/chatgpt-keep-going/content.js`
- 目录声明：`browser-extension/webapps/catalog.json` 中对应 ID 的条目。
- 该条目的同步键：`settings`。

## 代码导航

- 页面脚本检测 ChatGPT 对话状态，在 AI 回复生成完毕后根据配置启动倒计时并自动在输入框发送“继续”（或自定义续聊文本）。
- **绝对独立的外部悬浮架构（Zero Height Impact）**：
  - 宿主容器直接挂载在 `document.body` 下，与输入框 DOM 树彻底解耦；
  - 绝不插入输入框卡片内部，也绝不在输入框父容器内占用任何文档流高度，**对输入框原本的高度和布局实现真正的 0 像素干扰**；
  - 采用 `position: fixed` 独立浮动，通过 `getBoundingClientRect()` 动态计算，精确定位在**距离输入框正上方外侧 10px 处，右边缘与输入框右侧对齐**；
  - 绑定 `resize`、`scroll` 以及输入框 `input` 事件，当输入框输入多行自适应变高时，外部胶囊丝滑随动保持 10px 独立间隙。
- **状态机**：
  - `idle`（空闲/未开启）：极简半透明暗色微光胶囊，显示“✦ 不要停”，左键开启，右键或齿轮打开设置；
  - `waiting`（等待中）：琥珀金微光，显示“不要停 · 待回复”、“不要停 · 发送中”等；
  - `ready`（倒计时就绪）：鲜绿微光胶囊，显示“不要停 Ns”并实时跳动倒计时，时间归零时自动填入并发送。
- **停止防护模型**：
  - 手动草稿保护：输入框若存在用户手动输入的文本，立即暂停并保护草稿，绝不覆盖；
  - 切换对话保护：检测到 URL 切换或会话重置时立即停止；
  - AI 错误保护：检测到网络中断或生成报错时立即停止；
  - 最多续发限制：可配置续发指定次数（如 5 次）后自动停机；
  - 到指定时间停止：可配置本机具体时间点自动停机；
  - 关键词/正则停止：当 AI 最新回复命中特定关键词（如“完毕”、“可以停了”）或正则时，自动停机。

## 验证

```powershell
node --check browser-extension/webapps/chatgpt-keep-going/content.js
node --check browser-extension/webapps/runtime-background.js
node -e "const c=JSON.parse(require('fs').readFileSync('browser-extension/webapps/catalog.json','utf8')); if(!c.apps.some(a=>a.id==='chatgpt.keep-going')) process.exit(1); console.log('catalog entry valid')"
git diff --check
```
