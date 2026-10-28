# 燕子：隐藏桌面与屏幕外 UI 测试可行性验证（2026-10-09）

本仓库保留两个**真实运行过**的 Windows GUI 探针：

- `WinForms/`：`CreateDesktopW` + 全新 STA 线程 + `SetThreadDesktop`。通过 `SendMessageW(BM_CLICK)` 操作按钮，使用 `PrintWindow` 截取窗口。
- `Wpf/`：相同桌面隔离方式，真实 WPF 窗口，通过 WPF `ClickEvent` 更新界面；同时分别验证 `PrintWindow` 和 `RenderTargetBitmap`。

两个探针均自动退出，不修改燕子部署版本，**不发送全局键盘或鼠标输入，不切换当前桌面，不操作系统剪贴板**，没有开机启动项或常驻服务。

## 重现方法

在 Windows 交互式登录的用户进程中，从仓库根目录执行：

```powershell
dotnet run --project tools/hidden-desktop-probe/WinForms/HiddenDesktopProbe.csproj -c Release
dotnet run --project tools/hidden-desktop-probe/Wpf/HiddenWpfProbe.csproj -c Release
```

每次运行依次测试 `hidden` 和 `offscreen`；生成报告和 PNG 到项目 `bin/Release/net9.0-windows/results/`。

## 2026-10-09 实测结果（Windows 10 Pro / i5-12400F / ~16GB RAM）

| 指标 | WinForms 隐藏桌面 | WinForms 屏幕外 | WPF 隐藏桌面 | WPF 屏幕外 |
|---|---:|---:|---:|---:|
| 通过创建窗口与触发按钮事件 | 是 | 是 | 是 | 是 |
| `PrintWindow` 可捕获窗口内容 | 是 | 是 | 是 | **否（仅窗框）** |
| WPF `RenderTargetBitmap` 正确渲染 | — | — | 是 | 是 |
| 常规桌面画面抓取（GDI CopyFromScreen） | **失败：无效句柄** | 成功调用但不含窗口 | 未验证 | 未验证 |
| 工作集峰值 | 41.8 MB | 38.9 MB | 88.8 MB | 89.2 MB |
| CPU 时间 | 109 ms | 109 ms | 422 ms | 438 ms |
| 墙钟耗时 | 1.64 秒 | 1.63 秒 | 1.97 秒 | 1.96 秒 |

以上数据仅针对**空白窗口 + 标签 + 按钮**的短生命周期样例，不能外推为完整燕子、PaddleOCR 模型或长期测试的实际内存消耗。

WinForms 探针运行前后，主桌面的前台窗口句柄、剪贴板序列号、鼠标位置均未发生变化。两个测试窗口**不会获得当前桌面输入焦点**。已经分别打开 PNG 检查，隐藏桌面中 WPF `PrintWindow` 的实际图片包含更新后的文字和绿色按钮；屏幕外 WPF `PrintWindow` 图片只有窗框，WPF `RenderTargetBitmap` 图片正常。

## 技术限制与部署建议

1. `CreateDesktop` 并不等价于第二块物理显示器或第二套全局键鼠。不能让现有 `SendInput`/全屏 `BitBlt` 无改造运行在隐藏桌面里。
2. 必须在**未创建任何窗口或安装钩子**的全新 UI 线程上调用 `SetThreadDesktop`。在普通进程主线程上测试返回 Windows 错误码 170（`ERROR_BUSY`），随后改为新 STA 线程通过。
3. `PrintWindow` 在隐藏桌面能够验证简单 WPF UI 的真实窗口画面，但复杂 GPU/DWM 界面、透明 WPF 叠加层和多显示器尚未验证。
4. 真正的 OCR 截图工具如果依赖从整块屏幕读取像素，无法直接用屏幕外窗口替代——应先将**选区坐标计算**、**图片 OCR**、**复制状态机**和**悬浮窗逻辑**拆开进行隔离测试，再用可授权的独立 VM 桌面做完整真实鼠标拖拽验收。
5. 建议**按需启动、测试完退出**，不要后台常驻；同时主桌面忙时不运行全局输入类 E2E。

## 可核对的截图证据

在 `evidence/` 目录保留本机实测文件：

- `wpf-hidden-printwindow.png`：隐藏桌面 WPF 内容已正常绘制（绿色按钮）。
- `wpf-offscreen-printwindow.png`：屏幕外 WPF 的 `PrintWindow` 只显示窗框，内部空白。
- `wpf-offscreen-visualtree.png`：相同屏幕外 WPF 窗口通过 `RenderTargetBitmap` 可绘制内容，但不是桌面抓取。
- `winforms-hidden-printwindow.png`：隐藏桌面 WinForms 的窗口捕获正常。
- `winforms-results.json` / `wpf-results.json`：性能和测试结果。

注意：探针只证明短生命周期组件级测试技术可行，没有运行实际燕子 F3 全流程或全屏鼠标拖拽。程序正常退出后，无额外测试进程常驻。

## 后续公共封装

此目录保留最初的可行性探针。正式可复用的框架已迁移至 `src/Yanzi.UiTesting/`（公共 SDK）和 `tools/yanzi-ui-test-runner/`（隔离进程与资源回收），命令行入口为 `scripts/verify-yanzi-ui-hidden.ps1`。参见 `tools/yanzi-ui-test-runner/README.md`。
