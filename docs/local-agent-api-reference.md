# LocalAgentApi 接口清单

本文档汇总了燕子（OpenQuickHost）本地代理服务（LocalAgentApiServer）开放的所有 RESTful API 接口，供本地命令行工具、AI Agent 以及自动化测试脚本调用集成。

---

## 1. 服务概述与鉴权协议

* **默认服务地址**：`http://127.0.0.1:53919`
* **交互控制台**：在浏览器直接访问 `http://127.0.0.1:53919/` 即可打开内置的图形化 API 调试工作台。
* **认证方式（HTTP Bearer）**：
  所有受保护的 API 请求必须在 HTTP Header 中携带身份令牌：
  ```http
  Authorization: Bearer <AGENT_API_TOKEN>
  ```
  *(本地调试环境下默认 Token 为：`yanzi-local-dev-token`)*
* **数据编码**：所有请求体与响应体统一采用 **UTF-8 JSON** 格式。

---

## 2. 小程序与扩展管理接口

### 2.1 获取已安装扩展列表
* **路径**：`GET /v1/extensions`
* **说明**：扫描并返回本地 `%LOCALAPPDATA%\OpenQuickHost\Extensions` 中已安装的所有扩展及其元数据。
* **响应示例**：
  ```json
  {
    "items": [
      {
        "id": "smart-action",
        "title": "智能识别",
        "subtitle": "智能识别选中的内容并自动路由...",
        "category": "效率工具",
        "source": "LocalExtension",
        "version": "1.0.0",
        "runtime": "csharp",
        "entry": "main.cs",
        "permissions": ["clipboard", "context.read"]
      }
    ]
  }
  ```

---

### 2.2 执行指定小程序（核心测试端点）
* **路径**：`POST /v1/extensions/{id}/run`
* **说明**：在宿主内触发指定扩展执行。

> [!IMPORTANT]
> **入参字段命名关键约束**
> 请求体中传递的输入参数字段名为 **`input`**（小写），**切勿误写为 `inputText`**。

* **请求参数（Body）**：
  ```json
  {
    "input": "https://github.com/luoluoluo22",
    "launchSource": "api"
  }
  ```
* **响应结构**：
  ```json
  {
    "ok": true,
    "success": true,
    "output": "已在默认浏览器打开: https://github.com/luoluoluo22",
    "error": "",
    "exitCode": 0
  }
  ```

---

### 2.3 强制终止正在运行的小程序
* **路径**：`POST /v1/extensions/{id}/stop`
* **说明**：主动终止由指定扩展拉起的托管工作线程、原生窗口或后台长周期任务。
* **响应结构**：
  ```json
  {
    "ok": true,
    "stopped": true
  }
  ```

---

### 2.4 移动扩展至回收站（软删除）
* **路径**：`DELETE /v1/extensions/{id}`
* **说明**：将指定 ID 的扩展从活跃扩展目录安全隔离至回收站，可防止用户误删。

---

### 2.5 彻底清除回收站中的扩展（物理删除）
* **路径**：`DELETE /v1/extensions/recycle-bin/{id}`
* **说明**：永久删除回收站中指定 ID 的扩展物理文件。

---

## 3. 扩展键值存储（Storage）接口

每个扩展拥有独立的本地与云端键值数据隔离空间：

### 3.1 读取扩展存储值
* **路径**：`GET /v1/storage/{id}?key={key}`
* **响应示例**：
  ```json
  {
    "key": "default_search_engine",
    "content": "google"
  }
  ```

### 3.2 写入/更新扩展存储值
* **路径**：`PUT /v1/storage/{id}`
* **请求参数（Body）**：
  ```json
  {
    "key": "default_search_engine",
    "content": "google"
  }
  ```

---

## 4. 桌面环境与系统接口

### 4.1 发送桌面系统通知
* **路径**：`POST /v1/app/notify`
* **说明**：无需通过小程序，直接从宿主层面拉起 Windows 桌面右下角气泡通知。
* **请求参数（Body）**：
  ```json
  {
    "title": "系统提醒",
    "message": "后台编译与数据同步已完成。"
  }
  ```

---

### 4.2 执行后台 Shell 脚本
* **路径**：`POST /v1/shell/run`
* **说明**：让燕子宿主作为宿主代理进程拉起独立的后台 PowerShell 指令。
* **请求参数（Body）**：
  ```json
  {
    "command": "Get-Process Yanzi | Select-Object Id, CPU"
  }
  ```

---

## 5. 快速测试脚本（Windows PowerShell 5.1）

在终端中可使用以下标准模板进行扩展调测：

```powershell
$token = "yanzi-local-dev-token"
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type"  = "application/json; charset=utf-8"
}

# 1. 查询所有已加载扩展
Invoke-RestMethod -Uri "http://127.0.0.1:53919/v1/extensions" -Headers $headers

# 2. 模拟测试运行 smart-action (输入网址测试)
$payload = @{
    input = "https://github.com/luoluoluo22"
    launchSource = "powershell_test"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://127.0.0.1:53919/v1/extensions/smart-action/run" `
    -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($payload))

Write-Host "Success: $($response.success), Output: $($response.output)"
```
