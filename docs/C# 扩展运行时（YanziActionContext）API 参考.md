# C# 扩展运行时（YanziActionContext）API 参考

本文档详细说明了燕子（Yanzi）C# 小程序的底层执行机制、自动编译环境、类入口契约以及 `YanziActionContext` 提供的所有核心 API 与属性。

---

## 1. C# 执行引擎与编译架构

燕子宿主内置了基于 **Microsoft.CodeAnalysis (Roslyn)** 的高性能内存编译管线，并在独立隔离的 `AssemblyLoadContext` 中加载执行：

1. **零外部编译依赖**：无需用户或系统预装完整 Visual Studio 或 .NET SDK 开发环境，宿主程序自包含编译依赖（`Basic.Reference.Assemblies.Net90`）；
2. **多级动态构建与增量缓存**：
   - 源码变更时，宿主会基于 `[扩展ID + 源码Hash + 全局Usings + 运行时源码]` 计算复合指纹；
   - 编译输出缓存在 `.yanzi-csharp-cache/<fingerprint>/bin/Release/net9.0/YanziExtension.dll`；
   - 指纹未变更时直接秒级重用程序集，极大提升快捷触发性能；
3. **故障排查诊断机制**：
   - 若脚本出现语法或类型错误，Roslyn 的全部诊断告警与错误会**自动输出至扩展目录下的 `debug.log`**。开发者或 AI 排错时可直接检查该文件。

---

## 2. 预置依赖与全局命名空间

编译引擎默认引用了完整的 .NET 9.0 运行时核心库以及 Windows 桌面桌面子系统，因此您可以在脚本中直接调用：

* **核心 BCL**：`System`、`System.IO`、`System.Collections.Generic`、`System.Linq`、`System.Diagnostics`、`System.Text.Json`、`System.Text.RegularExpressions` 等；
* **桌面 UI 与系统组件**：WPF 组件（`System.Windows`、`System.Windows.Media`）、WinForms（`System.Windows.Forms` 用于系统底层交互）；
* **原生互操作**：`System.Runtime.InteropServices`（支持完整 P/Invoke 调用 `user32.dll`、`shell32.dll`、`kernel32.dll` 等 Windows 原生 API）；
* **宿主命名空间**：`OpenQuickHost.CSharpRuntime`（自动注入上下文类型定义）。

---

## 3. 入口契约（Entry Contract）

每个 C# 扩展的业务源码必须声明一个包含 `RunAsync` 静态方法的静态类 `YanziAction`：

```csharp
using System;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    /// <summary>
    /// 扩展唯一执行入口
    /// </summary>
    /// <param name="context">宿主注入的运行时上下文</param>
    /// <returns>返回执行结果摘要字符串（供控制台或日志消费）</returns>
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        // 业务处理逻辑
        return "执行完成";
    }
}
```

---

## 4. `YanziActionContext` 核心属性与方法

### 4.1 核心上下文属性

| 属性名 | 类型 | 说明 |
| :--- | :--- | :--- |
| `ExtensionId` | `string` | 当前正在运行的扩展唯一标识（如 `"smart-action"`） |
| `Title` | `string` | 扩展显示名称（如 `"智能识别"`） |
| `ExtensionDirectory` | `string` | 当前扩展所在的绝对物理路径，用于读取同目录资源 |
| `ExtensionDataDirectory` | `string` | 当前扩展专属的持久化数据目录路径（位于宿主 Data 目录下） |
| `InputText` | `string?` | 宿主自动截获的前台选中文本，或通过 API 传递的入参字符串 |
| `LaunchSource` | `string` | 触发来源：`"hotkey"`、`"tray"`、`"search"`、`"api"` 等 |
| `Now` | `DateTimeOffset` | 本次动作被触发时的高精度系统时间戳 |
| `Permissions` | `IReadOnlyList<string>` | 该扩展在 manifest.json 中声明的权限列表 |
| `State` | `IReadOnlyDictionary<string, string>` | 当前扩展保存的持久化状态键值对字典 |
| `AgentApiBaseUrl` | `string` | 宿主暴露的本地 Agent API 基础地址（如 `http://127.0.0.1:53919`） |
| `AgentApiToken` | `string` | 调用本地 Agent API 所需的临时 Bearer 令牌 |

### 4.2 核心上下文方法

#### `void ShowDesktopNotification(string title, string message)`
* **描述**：通过反射主程序安全地向当前 Windows 桌面托盘区域弹出标准的气泡（BalloonTip）或通知。
* **参数**：
  * `title`: 通知标题（建议格式：`[小程序名] - 业务事件`）
  * `message`: 通知正文内容
* **示例**：
  ```csharp
  context.ShowDesktopNotification("智能识别 - 路径不存在", "未在本地磁盘找到该文件。");
  ```

#### `Task ShowNotificationAsync(string title, string message)`
* **描述**：双通道异步通知。除拉起桌面气泡外，还会向 LocalAgentApi 发送通知广播。
* **示例**：
  ```csharp
  await context.ShowNotificationAsync("同步完成", "所有项目数据已成功同步至云端。");
  ```

#### `void Log(string message)`
* **描述**：将诊断信息以 `[yyyy-MM-dd HH:mm:ss] message` 格式安全追加写入当前扩展目录下的 `debug.log` 文件中，线程安全且异常静默。
* **示例**：
  ```csharp
  context.Log($"捕获到前台选中文本: length={context.InputText?.Length ?? 0}");
  ```

#### `Task SetStateAsync(object values)` / `Task SetStateAsync(IReadOnlyDictionary<string, string> values)`
* **描述**：将扩展的运行时状态持久化保存，宿主会自动写回状态存储，下次启动该扩展时可通过 `context.State` 读取。
* **示例**：
  ```csharp
  await context.SetStateAsync(new { LastSearchEngine = "Google", LastRunTime = DateTime.UtcNow });
  ```

---

## 5. 子服务客户端

### 5.1 键值存储客户端 (`context.Storage`)

当扩展需要保存用户配置或业务数据（支持本地或多端云同步）时使用：

```csharp
// 写入数据（支持 scope: "local" | "cloud" | "both"）
await context.Storage.WriteTextAsync("search_engine", "google", scope: "both");

// 读取数据
string? engine = await context.Storage.ReadTextAsync("search_engine", scope: "both");
```

---

## 6. 经典代码范式（Best Practices）

### 6.1 前台剪贴板安全操作（STA 线程安全模式）
WPF 剪贴板操作若在非 UI/STA 线程执行会引发异常，建议封装如下模式：

```csharp
private static string GetClipboardTextSafe()
{
    string result = string.Empty;
    var thread = new Thread(() =>
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    result = System.Windows.Clipboard.GetText();
                    break;
                }
            }
            catch
            {
                Thread.Sleep(30); // 剪贴板锁重试
            }
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(1000);
    return result;
}
```

### 6.2 智能拉起系统外部进程
打开文件、调用浏览器或启动独立终端时，必须设置 `UseShellExecute = true`：

```csharp
// 1. 调用默认浏览器打开 URL
Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

// 2. 调用资源管理器定位目录
Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"") { UseShellExecute = true });

// 3. 拉起保持窗口的 PowerShell 终端执行指令
Process.Start(new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"{cmd}\"",
    UseShellExecute = true
});
```
