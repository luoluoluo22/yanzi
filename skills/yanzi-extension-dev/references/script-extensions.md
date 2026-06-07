# Script Extensions

Current supported runtimes:

- `csharp`
- `powershell`

Entry modes:

- `entry`: use a script file such as `main.ps1`
- `entryMode = inline`: put C# or PowerShell source directly in `manifest.json` under `script.source`

Choose the runtime by task:

- Use C# for complex logic, JSON/HTTP/file processing, native WPF windows, P/Invoke, and strongly typed .NET APIs.
- Use PowerShell for Windows automation, registry/service/process/scheduled-task/system-command work, clipboard/file automation, and tasks with existing cmdlets.
- If the task is essentially a cmd/bat command sequence, wrap it with PowerShell or use an external script entry instead of forcing C#.

C# inline pattern:

```csharp
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        return Task.FromResult(context.InputText);
    }
}
```

Available context fields:

- `InputText`
- `ExtensionDirectory`
- `LaunchSource`: String. Indicates how the extension was triggered. Common values: `"app-startup"` (launched silently on software startup via the manifest startup configuration), `"agent-api"` (triggered via the Local HTTP API), or UI actions (e.g. `"radial-menu"`, `"search"`).
- `Now`
- `Permissions`

PowerShell basic pattern:

```powershell
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Output "hello from skill"
```

Rules:

- `param(...)` must be first
- keep stdout for successful result text
- use stderr or throw for failures
- if hosted view uses `actionType = "script"`, stdout is rendered into the right-side output panel
- if launched as a normal script extension, Yanzi currently shows the result in a modal dialog

Clipboard example:

```powershell
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$text = Get-Clipboard -Raw
if ([string]::IsNullOrWhiteSpace($text)) {
    Write-Output "当前剪贴板为空。"
}
else {
    Write-Output $text.Trim()
}
```

---

## Advanced C# & PowerShell Examples

### 1. C# Background Hotkey Listener (Resident Service)
Demonstrates how to register a Win32 global hotkey (`RegisterHotKey`) using `static` variables to persist the hotkey listener in the background after `RunAsync` exits.

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

public static class HotkeyListenerManager
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static Window _dummyWindow;
    private static HwndSource _hwndSource;
    private const int HOTKEY_ID = 9000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    public static bool IsRunning => _dummyWindow != null;

    public static void Start()
    {
        if (IsRunning) return;

        // Hotkey registration needs a window handle (HWND) to receive message loops.
        // We run this dummy window in the WPF Main Thread.
        Application.Current.Dispatcher.Invoke(() =>
        {
            _dummyWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            _dummyWindow.SourceInitialized += (s, e) =>
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(_dummyWindow);
                _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
                _hwndSource.AddHook(HwndHook);

                // Register Ctrl + Alt + K (0x4B)
                RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, 0x4B);
            };
            _dummyWindow.Show();
        });
    }

    public static void Stop()
    {
        if (!IsRunning) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(_dummyWindow);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource.Dispose();
            _dummyWindow.Close();
            _dummyWindow = null;
        });
    }

    private static IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            // Hotkey triggered: Execute your action here
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show("Ctrl + Alt + K Pressed! Triggering background task...", "Hotkey Manager");
            });
        }
        return IntPtr.Zero;
    }
}

public class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        // Quietly setup the hotkey listener in background
        HotkeyListenerManager.Start();
        return Task.FromResult("Hotkey listener running in background. Press Ctrl+Alt+K to test.");
    }
}
```

### 2. C# Pure Code WPF UI with Async Web Request
Demonstrates how to build a clean, modern WPF Window with async HTTP calls using pure C# (avoiding XAML template files) and a progress indicator.

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

public class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var tcs = new TaskCompletionSource<string>();
        Application.Current.Dispatcher.Invoke(async () =>
        {
            try
            {
                var win = new Window
                {
                    Title = "每日名言",
                    Width = 400,
                    Height = 250,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 247))
                };

                var sp = new StackPanel { Margin = new Thickness(20) };
                
                var titleText = new TextBlock { Text = "正在从网络获取内容...", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,15) };
                var quoteText = new TextBlock { Text = "加载中...", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Brushes.DimGray, Margin = new Thickness(0,0,0,20) };
                
                var closeBtn = new Button { Content = "确定", Width = 80, Height = 30, Background = Brushes.LightGray, BorderThickness = new Thickness(0) };
                closeBtn.Click += (s, e) => win.Close();

                sp.Children.Add(titleText);
                sp.Children.Add(quoteText);
                sp.Children.Add(closeBtn);
                win.Content = sp;
                
                win.Closed += (s, e) => tcs.TrySetResult("Window Closed");
                win.Show();

                // Async network call
                using (var http = new HttpClient())
                {
                    // Call a public quote API
                    var res = await http.GetStringAsync("https://v1.hitokoto.cn/");
                    using (var doc = JsonDocument.Parse(res))
                    {
                        var hitokoto = doc.RootElement.GetProperty("hitokoto").GetString();
                        var from = doc.RootElement.GetProperty("from").GetString();
                        
                        titleText.Text = "今日一言";
                        quoteText.Text = $"“{hitokoto}” — 《{from}》";
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult("Failed: " + ex.Message);
            }
        });

        return await tcs.Task;
    }
}
```

### 3. PowerShell Process Monitor (Returning JSON Results)
Demonstrates a multi-file PowerShell script that monitors process performance and feeds results back.

```powershell
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Fetch top 5 processes sorting by CPU usage
$processes = Get-Process | 
    Sort-Object CPU -Descending | 
    Select-Object -First 5 | 
    Select-Object Name, Id, @{Name='CPU(s)'; Expression={[Math]::Round($_.CPU, 2)}}

# Format as JSON string and output to stdout
$jsonOutput = $processes | ConvertTo-Json -Compress
Write-Output $jsonOutput
```
