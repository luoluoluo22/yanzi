# ChatGPT 结构化内容实测记录

2026-10-03，用户 Edge 已登录 ChatGPT，浏览器助手 0.2.0 连接本地工作台 `127.0.0.1:53921`。用户已暂停会自动关闭重复标签的插件。六个样例均新建聊天，返回 `visibility: hidden`。

## 结论

六个样例的 API 正文与完成后四秒重新读取的正文、Markdown 一致。独立解析网页 HTML 后，六个代码块的 UTF-8 SHA-256 与 API `codeBlocks[].text` 一致；所有被检查的段落、标题、表格单元格和链接无遗漏。当前最终提取器回放六份快照也与实测返回一致。

这证明本次样例的网页内容传输完整。普通段落 JSON 的网页显示文本本身不合法，因此不能宣称六个样例全部具有可解析的 JSON。网页 DOM 也不能证明模型原始 Markdown 的逐字一致性。

| 样例 | 网页与返回核对 | 结构检查 |
| --- | --- | --- |
| 普通段落 JSON | 正文一致，无遗漏 | 网页与返回均解析失败，位置 93；`jsonError` 如实报告 |
| JSON 代码块 | 原文 235 字节，哈希一致 | 嵌套数组、中文、Emoji、引号和换行转义解析成功 |
| Python | 原文 881 字节，哈希一致 | 缩进、空行、字符串、中文完整；Python AST 解析成功 |
| Python / JSON / JavaScript 混合 | 三块原文分别 129 / 70 / 56 字节，顺序及哈希一致 | 三种语法均解析成功，前后说明文字保留 |
| Markdown | 标题、段落、表格内容及链接保留 | 1 张表格、4 个列表项、Example 链接均核对通过 |
| 80 行长代码块 | 原文 2479 字节，哈希一致 | ROW_001 至 ROW_080 全部存在，末尾标记存在 |

Python 使用 `ast.parse`，JavaScript 使用 `vm.Script` 仅编译，JSON 使用 `JSON.parse`；没有执行生成代码。

## 修复与接口

当前网页代码块采用 `data-markdown-copy="code-block"`，而非旧版 `pre`。原先检测会漏掉代码结构，并将 JSON/Python 语言标签混入正文。现已同时支持两种结构，过滤语言标题和复制按钮，并直接从 `code.textContent` 返回代码原文。

接口提供 `text`、`markdown`、`codeBlocks`、`json`、`jsonError`。机器使用代码时应取 `codeBlocks[].text`，该字段保留末尾换行；`text` 则裁剪正文首尾空白。`markdownSource: reconstructed-from-dom` 明确说明 Markdown 来源。普通段落的转义在网页渲染过程中可能改变，建议提示词要求 JSON 代码块，不自动修补不合法 JSON。

新增测试覆盖复制按钮、Unicode、反斜杠、嵌套 JSON、Python 空白、多个代码块、代码内反引号、旧版 pre、列表、表格、链接及不合法 JSON。全部 23 项自动化测试通过。

## 可复查证据

完整实测 API 返回、网页 HTML、延迟读取结果保存在本机 `.artifacts/chatgpt-structured-test-fixed/`；`report.json` 是实测摘要，`comparison.json` 是独立 HTML 比对结果。该目录不提交到 Git。

脚本：`tools/chatgpt-bridge/live-test.mjs`（实际发送测试消息）、`tools/chatgpt-bridge/verify-live-artifacts.mjs`（读取保存的快照，不发送消息）。实际网页测试六类通过内容核对；普通段落 JSON 的语法失败单独报告，不算作 JSON 解析成功。

本次未覆盖图片、附件、工具调用、公式、引用卡片、所有模型，以及浏览器长期冻结后的恢复。后续网页结构变化仍需复测。
