# ChatGPT 标签页生命周期

浏览器助手版本 0.2.1；本地工作台已更新，用户已重新加载浏览器助手。

## 行为

1. `chatgpt_status` 查询已打开的 ChatGPT 页面，返回 `exists`、URL、`tabId`、归属 `managed` 及初步关闭条件 `canClose`。
2. 默认发送新聊天时，复用工作台创建的空闲后台页并导航到首页，没有合适的页才新建。通过 `tabId` 可以继续现有聊天，`newChat:false` 也允许继续唯一的 ChatGPT 页面。多个用户页面不猜测选择。
3. `closeAfter:true` 在任务成功、结果收集完成后关闭空闲工作台页。默认保留以便复用。错误任务保留页面供检查。
4. `chatgpt_close` 关闭指定工作台页，`chatgpt_cleanup` 批量清理空闲工作台页。不会关闭用户原有页、前台页、固定页、草稿页或生成中的页。

归属保存在扩展 session storage，worker 挂起后可以恢复，浏览器或扩展重启后旧页面不再自动归属。人工改变 URL 时撤销归属。旧版本留下的测试标签页需手动清理。

任务返回 `lifecycle`，说明创建、复用和关闭是否发生。`closed:true` 后不能再通过该 `tabId` 读取页面，服务仍保存任务结果，关闭标签页不删除 ChatGPT 聊天记录。

## 验证

30 项自动化测试全部通过，包括连续默认复用、指定现有聊天、独立新标签策略、成功关闭、失败保留、用户页面保护、前台/固定/草稿保护、过期归属清理。

真实 Edge 后台测试：

- 测试页 `399745223`，两次聊天均返回 `visibility:hidden`。
- 两个会话 ID 分别为 `6ac0ec72-9774-83e9-aeaa-53fd515e0b89`、`6ac0ec80-587c-83e9-ae3f-a2523de7df88`，复用页时确实开始了独立聊天。
- 两次测试回复 `LIFE_A_831`、`LIFE_B_833` 均已返回，第二次成功后关闭测试页。
- 测试前后的原有 ChatGPT 标签页均为 9 个，逐一核对 ID，无原有页面被关闭。

证据：`.artifacts/chatgpt-lifecycle-test/report.json`。复测脚本：`tools/chatgpt-bridge/lifecycle-live-test.mjs`。

默认自动选择复用及 `chatgpt_cleanup` 的附加测试保存在 `.artifacts/chatgpt-lifecycle-test/pool.json`。实际关闭前重新检查归属、前后台、草稿和生成状态；批量清理返回关闭与跳过的标签页 ID。

默认自动复用测试两次均选择 `399745227`，只创建了一个页。第一次清理时该页转为前台，接口将其放入 `skippedTabIds`，没有关闭。后续空闲后台清理成功关闭 `399745227` 和新建的 `399745230`，详见 `.artifacts/chatgpt-lifecycle-test/cleanup.json`。原有用户页仍保留。

本次没有覆盖浏览器长期休眠、全部网页模型及所有浏览器版本。默认复用跳过无法确认空闲的页面；若所有工作台页面均被用户占用，会另建页面。周期任务可开启 `closeAfter`，避免保留每次任务的页面。
