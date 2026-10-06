# 燕子浏览器助手开发接口与工作流协议规范 (v1)

> ChatGPT 后台聊天使用独立的小程序服务 `http://127.0.0.1:53921`，不会受下面旧工作流接口 30 秒等待限制影响。安装、定时和事件触发 API 见 [ChatGPT 后台工作台](../tools/chatgpt-bridge/README.md)。浏览器助手 0.2.0 的弹窗可设置燕子实际 API 端口；默认 53919，ChatGPT 专用通道固定 53921。

燕子浏览器助手通过本地 WebSocket 连接（默认端口 `18293`）常驻连接到燕子启动器。燕子启动器的本地 Agent 服务（默认端口 `53919`）对外暴露统一的 HTTP RESTful 接口，使得本地 AI 智能体 (Agent) 或第三方脚本能够远程控制浏览器进行静默数据抓取和交互表单填充。

---

## 0. 浏览器助手开发控制接口

### 0.1 自重载扩展

- **接口地址**：`POST http://127.0.0.1:<AgentPort>/v1/browser/reload`
- **认证 Header**：与 Local Agent API 一致，使用 `X-Yanzi-Token` 或 Bearer Token。
- **用途**：开发期修改燕子浏览器助手后，让当前已连接的 Edge/Chrome 扩展直接执行 `chrome.runtime.reload()`，无需再打开扩展管理页手工点击“重新加载”。
- **成功响应**：HTTP 202，表示重载指令已通过 Browser WebSocket 发送。
- **验证链**：桌面日志应依次看到 `Browser extension reload requested`、`Browser extension reload ACK`，随后旧 WebSocket 断开并由新版扩展重新 `Registered successfully`。
- **首次启用**：由于旧版扩展本身还没有这段处理逻辑，需要在加入此功能后的第一次由用户手工重新加载一次。从此后可全自动重载。
- **页面代码更新**：开发自重载会写入一次性刷新标记；新版 Service Worker 启动后自动刷新当前运行网页小程序的标签页，确保旧 content script 不残留。
- **连接自愈**：浏览器助手每 20 秒心跳；连续 45 秒未收到 pong 会主动判定连接僵死并强制重连，避免桌面端异常退出后 WebSocket 长时间停留在假 OPEN 状态。

## 1. 外部控制接口 (REST API)

外部 Agent（如 Python 脚本、本地 AI 服务）通过向燕子客户端发送 HTTP POST 请求来操控浏览器执行任务。

*   **接口地址**：`POST http://127.0.0.1:53919/v1/browser/execute`
*   **认证 Header**：`X-Yanzi-Token: <token>`
*   **请求 Body**：JSON 格式，定义待访问 of URL、工作流步骤以及执行配置。

### 示例请求 (Python)：
```python
import requests

url = "http://127.0.0.1:53919/v1/browser/execute"
headers = {
    "X-Yanzi-Token": "yanzi-local-dev-token",  # 视实际配置的 Token 而定
    "Content-Type": "application/json"
}

payload = {
    "url": "https://www.xiaohongshu.com/explore",
    "closeOnComplete": True,  # 任务完成后自动销毁/关闭网页标签页
    "steps": [
        { "type": "wait", "selector": ".search-input", "timeout": 5000 },
        { "type": "fill", "selector": ".search-input", "value": "AI提效神器" },
        { "type": "click", "selector": ".search-button" },
        { "type": "wait", "timeout": 2000 },
        { "type": "scroll", "distance": 600 },
        { 
            "type": "scrape", 
            "selectors": {
                "titles": "section.note-item .title|innerText",
                "authors": "section.note-item .author-name|innerText",
                "links": "section.note-item a.cover|href"
            } 
        }
    ]
}

response = requests.post(url, headers=headers, json=payload)
print(response.json())
```

### 1.1 网页小程序数据接口

网页小程序共享同一条 `POST /v1/browser/execute` 控制通道。调用方不直接依赖浏览器 storage。

读取：

```json
{
  "action": "webapp_data_get",
  "appId": "xiaohongshu.filter",
  "key": "noteStats"
}
```

写入：

```json
{
  "action": "webapp_data_set",
  "appId": "xiaohongshu.filter",
  "key": "customCards",
  "value": {
    "version": 1,
    "items": []
  }
}
```

### 1.2 小红书自定义卡片接口

查询：

```json
{ "action": "xiaohongshu_custom_card_list" }
```

新增或更新：

```json
{
  "action": "xiaohongshu_custom_card_upsert",
  "card": {
    "id": "daily-ai-note-20261004",
    "title": "今天值得看的 AI 变化",
    "body": "燕子根据你的关注主题生成的摘要。",
    "author": "燕子 AI",
    "badge": "燕子",
    "imageUrl": "https://example.com/cover.jpg",
    "url": "https://example.com/note",
    "priority": 10,
    "enabled": true
  }
}
```

删除：

```json
{
  "action": "xiaohongshu_custom_card_remove",
  "id": "daily-ai-note-20261004"
}
```

清空：

```json
{ "action": "xiaohongshu_custom_card_clear" }
```

`title` 必填；`imageUrl` / `url` 仅接受 HTTP(S)。同 id 的 upsert 会原位更新数据。卡片定义保存到 `xiaohongshu.filter/customCards` 并使用燕子现有账户同步。

### 1.3 小红书兴趣数据

`xiaohongshu.filter/noteStats` 为聚合后的个人兴趣行为数据。内容包括打开次数、停留时长、标题、作者、标签、互动数等。页面侧采用本地高频聚合 + 延迟云同步，避免每次 DOM 更新或每秒停留都产生一次云端写入。

---

## 2. 声明式工作流协议 (Workflow DSL)

工作流由一个 `steps` 数组按序串行执行。每一个步骤代表一个原子浏览器指令：

### 2.1. 等待步骤 (wait)
等待指定时长，或者阻塞直到指定的元素加载出现在 DOM 中。
*   **无元素延时**：
    ```json
    { "type": "wait", "timeout": 3000 }  // 静态延迟 3 秒
    ```
*   **等待元素加载**：
    ```json
    { "type": "wait", "selector": ".submit-btn", "timeout": 5000 }  // 最长等待 5 秒直到按钮出现
    ```

### 2.2. 输入步骤 (fill)
将指定文本填充至目标输入框中，已进行高保真 SPA 框架绑定（Vue/React）适配。
*   **参数**：
    *   `selector`：目标 Input/Textarea 的 CSS 选择器。
    *   `value`：要填充的文本。
*   **示例**：
    ```json
    { "type": "fill", "selector": "#username", "value": "my_account" }
    ```

### 2.3. 点击步骤 (click)
模拟点击网页元素。
*   **参数**：
    *   `selector`：目标按钮/链接的 CSS 选择器。
*   **示例**：
    ```json
    { "type": "click", "selector": "button[type='submit']" }
    ```

### 2.4. 滚动步骤 (scroll)
垂直滚动网页，用以触发图片懒加载或触底加载下一页（无限滚动）。
*   **参数**：
    *   `distance`：垂直滚动的像素数（向下为正，默认 400）。
*   **示例**：
    ```json
    { "type": "scroll", "distance": 800 }
    ```

### 2.5. 抓取数据步骤 (scrape)
提取指定选择器的数据。
*   **参数**：
    *   `selectors`：抓取字段名字典，值支持以 `|` 管道附带抓取属性。
*   **管道属性规范**：
    *   `selector|innerText`：获取元素的纯文本（默认值）。
    *   `selector|innerHTML`：获取元素的 HTML 代码。
    *   `selector|href`：获取超链接目标 URL。
    *   `selector|src`：获取图片或媒体源路径。
    *   `selector|value`：获取表单输入框的当前值。
*   **示例**：
    ```json
    {
      "type": "scrape",
      "selectors": {
        "titles": "a.post-link|innerText",
        "urls": "a.post-link|href",
        "avatar_img": ".user-card img|src"
      }
    }
    ```

---

## 3. 错误处理与响应规范

在执行过程中，任何一步失败都会中断工作流并返回具体的错误位置。

### 成功响应示例 (200 OK)：
```json
{
  "taskId": "task_172938491",
  "status": "success",
  "data": {
    "titles": ["第一篇文章", "第二篇文章"],
    "urls": ["https://.../1.html", "https://.../2.html"]
  }
}
```

### 失败响应示例 (200 OK 带有 error 状态 / 或 500)：
```json
{
  "taskId": "task_172938491",
  "status": "error",
  "message": "步骤 3 (click) 失败: 未找到点击目标元素: button[type='submit']",
  "data": null
}
```
