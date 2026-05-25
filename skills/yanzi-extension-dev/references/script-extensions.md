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
- `LaunchSource`
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
