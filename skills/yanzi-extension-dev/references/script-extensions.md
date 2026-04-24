# Script Extensions

Current supported runtime:

- `powershell`

Entry modes:

- `entry`: use a script file such as `main.ps1`
- `entryMode = inline`: put the PowerShell source directly in `manifest.json` under `script.source`

Basic pattern:

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
