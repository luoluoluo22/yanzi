# ChatGPT 后台接口实测记录

日期：2026-10-03（用户时区 Asia/Shanghai）。环境：用户已登录的 Microsoft Edge，燕子浏览器助手 0.2.0，本地小程序 `127.0.0.1:53921`。

## 结果

| 功能 | 实测结果 |
| --- | --- |
| 扩展连接 | `connected: true`，`extensionVersion: 0.2.0` |
| 新建聊天 | 创建 inactive 标签页，输入框就绪，`visibility: hidden` |
| 发送并等待回复 | `status: success`，收到 `YANZI_BACKGROUND_OK` |
| 同一聊天继续发送 | 相同 tabId / conversationId，收到 `YANZI_CONTINUE_OK`，仍为 hidden |
| 独立读取消息 | 一次性定时任务读取测试聊天成功 |
| 命名事件触发 | `yanzi.bridge.smoke` 触发标签页状态查询成功；随后暂停计划 |
| 一次性定时任务 | 到时进入串行队列并执行成功，执行后计划自动停用 |
| 最终版消息提取与完成判断 | 收到精确纯文本 `YANZI_FINAL_OK`，无界面标签，返回单一 messageId，hidden |
| 自动化测试 | `npm test`：17 项通过 |

最终版请求 ID：`981b9c81-b4e9-46b5-87fb-7809459135f9`。运行记录保存在本机 ExtensionStorage/chatgpt-bridge/state.json。

## 实测发现并修复

1. 重复标签关闭插件会关闭自动创建的新页；用户停用后恢复。新页加载失败会返回错误。
2. 当前输入框没有 `prompt-textarea` ID，改为同时支持 main 中 contenteditable 的 textbox。
3. 当前发送按钮只有中文 aria-label“发送”，补充该选择器。
4. 当前消息使用 `data-chatgpt-search-unit-key`，补充用户/助手消息提取，并清除屏幕阅读器标题和重复 ID。
5. ChatGPT 会在新标签页恢复草稿；只允许提交与本次请求完全一致的草稿，保留其他草稿。
6. 当前回复除稳定文本和停止按钮判断外，还要求对应回复的复制/评价控件出现，降低返回未完成内容的风险。

早期适配过程中产生的超时/error 任务保留用于排查；已开始的请求没有自动重发。最终发送和同一会话继续发送均重新实测通过。

## 验证边界

本次已验证标签页处于后台，未验证电脑休眠期间执行（休眠时本来就无法执行）、浏览器退出后执行、长期冻结恢复、长文/工具调用/附件或所有语言页面。服务使用网页登录态和 DOM，不依赖非官方逆向 API，也不导出 Cookie。周期计划及断线/重启行为由自动化测试覆盖，长时间常驻仍需后续观察。
