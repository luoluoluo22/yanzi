param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$a = [System.Windows.Automation.AutomationElement]
$type = [System.Windows.Automation.ControlType]
if ($ProcessId -eq 0) { throw 'An explicit isolated dev process ID is required' }
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId"
if (-not $process -or $process.CommandLine -notmatch '--dev' -or
    $process.CommandLine -notmatch '--settings-preview') {
    throw 'Refusing to operate non-preview application'
}
$window = $a::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($a::NameProperty,
        '燕子设置 · 统一 UI 试用（开发版）')))
if (-not $window -or $window.Current.ProcessId -ne $ProcessId) {
    throw 'Preview settings window not ready'
}
$nav = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $type::ListItem)))
$keys = @('general', 'ai', 'environment', 'sync', 'extensions', 'quickpanel',
          'mousegestures', 'radial', 'yarnselect', 'yanm', 'yanwo', 'about')
$passed = 0
foreach ($key in $keys) {
    $item = @($nav | Where-Object { $_.Current.Name -match "Key = $key," }) | Select-Object -First 1
    if (-not $item) { throw "Sidebar page missing: $key" }
    $pattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    Start-Sleep -Milliseconds 180
    if (-not $pattern.Current.IsSelected) { throw "Navigation failed: $key" }
    $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $type::Text)))
    $right = $window.Current.BoundingRectangle.Left + 250
    $visible = 0
    foreach ($text in $texts) {
        if (!$text.Current.IsOffscreen -and $text.Current.BoundingRectangle.Left -ge $right -and $text.Current.BoundingRectangle.Width -gt 0) { $visible++ }
    }
    if ($visible -lt 1) { throw "No visible settings content: $key" }
    Write-Host "PASS page=$key visible_text=$visible"
    $passed++
}
Write-Host "PASS: $passed/$($keys.Count) settings pages navigate with live content"
# Return to general for human review and screenshot.
$general = @($nav | Where-Object { $_.Current.Name -match 'Key = general,' }) | Select-Object -First 1
$general.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
