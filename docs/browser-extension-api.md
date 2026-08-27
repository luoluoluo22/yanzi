# 燕子浏览器助手开发接口与工作流协议规范 (v1)

燕子浏览器助手通过本地 WebSocket 连接（默认端口 `18293`）常驻连接到燕子启动器。燕子启动器的本地 Agent 服务（默认端口 `53919`）对外暴露统一的 HTTP RESTful 接口，使得本地 AI 智能体 (Agent) 或第三方脚本能够远程控制浏览器进行静默数据抓取和交互表单填充。

---

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
