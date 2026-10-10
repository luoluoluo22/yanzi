# 燕子公共隐藏桌面测试框架

用于多个燕子小程序的**低资源、按需启动、进程隔离** WPF 界面测试。验证于 Windows 10 Pro / .NET 9，不会修改已安装的燕子版本。

## 项目结构

- `src/Yanzi.UiTesting/`：公用测试 SDK（`IUiTestScenario`、`UiTestContext`、断言、按钮事件、两种截图）。
- `tools/yanzi-ui-test-runner/`：命令行控制器，创建 Win32 隐藏桌面，并使用 `CreateProcessW` 的 `STARTUPINFO.lpDesktop` **在启动进程时**指定桌面。采集工作集/私有内存、CPU、超时和异常，测试完销毁进程。
- `tools/yanzi-ui-test-samples/`：真实示例。已经实测公共 `Yanzi.UI.Wpf` 控件和燕子截图 `OcrFeedback` 悬浮窗，另有故意失败/超时的回归用例。
- `scripts/verify-yanzi-ui-hidden.ps1`：日常一键验收，默认顺序执行两个正常场景。

## 运行

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-hidden.ps1
```

若只测燕子截图悬浮窗：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-hidden.ps1 -Scenario capture-ocr-toast
```

不重新编译：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-yanzi-ui-hidden.ps1 -NoBuild
```

直接运行自定义项目：先编译自己的测试类库，再将 DLL 和测试类完整类型名传给运行器：

```powershell
dotnet run --project tools/yanzi-ui-test-runner/Yanzi.UiTestRunner.csproj -c Release -- --assembly C:\Path\MyMiniapp.UiTests.dll --type MyMiniapp.UiTests.SettingsScenario --mode hidden --out .tmp/ui-test-runs --timeout-ms 15000 --max-memory-mb 350
```

支持可选 `--mode offscreen`，但该模式下窗口虽然处于屏幕外，仍属**当前输入桌面**，透明层或 WPF 的 `PrintWindow` 可能得到空图。默认优先 hidden。

每次运行产生独立时间戳目录，内含 `worker.json`、`summary.json` 和截图 PNG。测试失败的退出码非零。超时及内存超过上限将强制回收子进程，释放隐藏桌面；不存在后台常驻服务。报告记录 `foregroundWindowUnchanged`、`cursorPositionUnchanged`、`clipboardSequenceUnchanged`，注意这三个指标可能受用户同时使用电脑影响，所以仅作为观测，不作为全局输入安全性的证明。

## 为其他小程序编写测试

测试项目引用：

```xml
<ProjectReference Include="../../src/Yanzi.UiTesting/Yanzi.UiTesting.csproj" />
```

参考 `tools/yanzi-ui-test-samples/SampleScenarios.cs`：

```csharp
public sealed class SettingsScenario : IUiTestScenario
{
    public string Name => "Settings UI";
    public Task RunAsync(UiTestContext test, CancellationToken cancellationToken)
    {
        var window = new SettingsWindow(); // 必须是可独立创建的测试实例
        try
        {
            test.ShowWindow(window);
            test.Check(window.IsVisible, "window created");
            test.Click(window.SaveButton); // WPF 路由点击，不是物理鼠标
            test.Check(window.IsSaved, "saved state updated");
            var capture = test.CaptureWindow(window, "settings-after-save");
            test.Check(File.Exists(capture.VisualTreePng), "visual screenshot exists");
            return Task.CompletedTask;
        }
        finally { window.Close(); }
    }
}
```

生产代码若创建时会访问真实数据，应依赖注入**测试用的存储/服务/凭证**，避免测试真实用户配置。SDK 只负责 UI 框架隔离，并不是安全沙箱。加载的测试 DLL 与当前用户具有相同文件/网络权限，**不要运行不可信的第三方测试 DLL**。

## 隔离范围、真实限制

- 隐藏桌面是在相同 Windows 登录会话下的另一个 `HDESK`；Windows 仍共用全局剪贴板和物理键鼠。因此测试用例**禁止使用** `SendInput`、`keybd_event`、`mouse_event`、`SetCursorPos`、`SwitchDesktop` 或操作系统剪贴板。
- 自动化操作使用 WPF 路由事件/控件逻辑/视觉树，不声称模拟了真实物理点击的完整路径。
- `PrintWindow` 可能对透明 WPF 窗口返回黑图，即使 API 返回 true。公共 SDK 同时提供 `RenderTargetBitmap` 视觉树捕获；这可以验证 WPF 渲染，但不能替代屏幕级画面。
- `BitmapSource`/WPF Dispatcher 必须保持 STA 线程关联。隐藏桌面在**子进程创建时**绑定，避免在已存在窗口/COM 对象的线程调用 `SetThreadDesktop` 导致 Win32 错误 170。
- 目前不能把燕子 F3 截图的全屏 `BitBlt`/真实鼠标拉框端到端测试原样搬到这里；需要额外设计屏幕内容注入及输入隔离方案。完整物理输入测试仍需隔离虚拟机或其他独立显示/输入环境。
- `PrintWindow` 的可用性受 GPU、DWM、透明窗口等影响，应始终人工查看第一次的 PNG 结果。
- 这里的性能监控只覆盖**测试子进程**，不包含可能被测试场景启动的其他进程，也不代表完整燕子主程序的内存。

## 2026-10-09 实测

| 用例 | 结果 |
|---|---|
| WPF 共享主题与按钮、状态、窗口截图 | 7/7 通过，约 0.7 秒，峰值工作集约 92 MB |
| 燕子截图 OCR 提示窗：识别中、3 秒仍显示、完成后自动关闭、两次截图 | 7/7 通过，约 6.9 秒，峰值工作集约 93 MB |
| 故意失败断言 | 按预期返回非零退出码并写入失败报告 |
| 60 秒挂起场景 + 1.5 秒超时限制 | 按预期终止子进程，原因标记为 `timeout` |
| 限制工作集峰值为 50 MB | 按预期停止，原因标记为 `memory_limit` |
| WPF 屏幕外模式 | 7/7 通过，视觉树截图可用，窗口系统截屏不可靠 |
| 主桌面鼠标、焦点、剪贴板 | 独立实测前后未变化 |

此前的原型和 Windows API 限制详见 `tools/hidden-desktop-probe/README.md`。
