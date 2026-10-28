# 燕子：浏览器通用能力（2026-10-05）

## 已实现能力

- `browser.status`
- `browser.scrape`
- `browser.autofill`
- `browser.workflow`

## 复用现有架构

没有再造第二套浏览器自动化。

现有链路：

`Capability API -> LocalAgentApiServer -> Browser WebSocket -> 燕子浏览器助手 -> content.js workflow engine -> 页面 DOM -> task_response`

浏览器扩展当前版本：0.5.32。

## 正式能力边界

### browser.status

返回：

- 浏览器助手是否启用
- 本地 API 是否监听
- 浏览器是否连接
- 当前连接浏览器
- 支持的声明式原子步骤

### browser.scrape

打开 http/https 网页，按 CSS Selector 抓取：

- innerText
- innerHTML
- textContent
- 指定 attribute

不支持 file/javascript/chrome/edge 等 URL。

### browser.autofill

按 CSS Selector 填写字段，并可选择点击一个按钮。

适合：

- 搜索框
- 普通表单
- 查询页
- 登录后站内操作

### browser.workflow

只允许五类声明式步骤：

1. wait
2. fill
3. click
4. scroll
5. scrape

不允许通过该能力注入任意 JavaScript。

最多 50 步；URL 仅允许 http/https；Selector、输入长度、等待时间、滚动距离均做边界校验。

## LocalAgentApiServer 改进

新增内部统一任务入口：

`RunBrowserTaskAsync(...)`

它负责：

- 判断浏览器 WebSocket 是否在线
- 生成 taskId
- 串行化 WebSocket SendAsync，避免并发发送冲突
- 等待 task_response
- 超时处理
- 清理 pending task

以后浏览器能力 Provider 不需要自行实现 WebSocket 协议。

## 真实验收

### 公共网页 scrape

对 `https://example.com/` 执行真实 `browser.scrape`：

- title 成功读取 `Example Domain`
- paragraph 成功读取页面正文
- Edge WebSocket 往返正常

### 本地确定性测试页

创建：

`artifacts/browser-capability-test/`

包含：

- input#name
- button#submit
- div#status
- 本地 Python HTTP 测试服务

#### workflow

真实执行：

`wait #heading -> fill #name -> click #submit -> wait -> scrape`

结果：

- heading：`燕子浏览器能力测试`
- status：`submitted:燕子工作流验收`
- title：`Yanzi Browser Capability Test`

#### autofill

真实执行：

- 填写：`燕子自动填写验收`
- 点击：`#submit`

服务器落盘结果：

`燕子自动填写验收`

因此可以确认填写和点击均实际发生，而不是只返回形式上的 success。

## 意义

浏览器不再只是一个“应用”，而成为燕子的互联网执行 Runtime。

后续网站能力可以遵循：

`第一次 AI 探索网页 -> 得到稳定 selectors/workflow -> 封装成语义能力 -> 后续直接调用`

即：

**第一次靠智能，第二次开始靠程序。**
