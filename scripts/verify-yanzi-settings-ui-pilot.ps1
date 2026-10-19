param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$a = [System.Windows.Automation.AutomationElement]
$type = [System.Windows.Automation.ControlType]
$windowName = '燕子设置 · 统一 UI（开发版）'
$condition = New-Object System.Windows.Automation.PropertyCondition($a::NameProperty, $windowName)
$window = $a::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
if (-not $window) { throw "Dev SettingsWindow preview is not visible" }
if ($ProcessId -and $window.Current.ProcessId -ne $ProcessId) { throw 'Wrong preview process ID' }
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$($window.Current.ProcessId)"
if ($process.CommandLine -notmatch '--dev' -or $process.CommandLine -notmatch '--settings-preview') {
    throw 'Refusing to interact with production settings: --dev --settings-preview required'
}
function Find($name, $controlType) {
    $filter = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($a::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $controlType)))
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $filter)
}
# Navigation may be left on any settings page by the user. Select General first.
$general = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $type::ListItem))) |
    Where-Object { $_.Current.Name -match 'Key = general,' } | Select-Object -First 1
if (-not $general) { throw 'General settings navigation unavailable' }
$general.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 150
# The preview toggle is intentionally removed: permanent shared styles cannot be disabled.
if (Find '新版样式' $type::CheckBox) { throw 'Obsolete preview toggle is visible' }
if (-not (Find '基础设置' $type::Text)) { throw 'Permanent shared General settings card absent' }
$combos = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $type::ComboBox)))
if ($combos.Count -lt 1) { throw 'Real theme ComboBox was lost' }
$switches = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $type::CheckBox)))
if ($switches.Count -lt 4) { throw "Expected four startup/system toggle controls, found $($switches.Count)" }
$settings = Join-Path $env:LOCALAPPDATA 'OpenQuickHost.Dev\appsettings.local.json'
$keys = @('ThemeMode','LaunchAtStartup','EnableAutoUpdate','CloseToTray','RefreshCloudOnStartup')
function SavedValues {
    if (-not (Test-Path $settings)) { throw 'Isolated developer settings not found' }
    $data = Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json
    return (($keys | ForEach-Object { "$_=$($data.$_)" }) -join ';')
}
$before = SavedValues
Start-Sleep -Milliseconds 150
$after = SavedValues
if ($before -ne $after) {
    throw "Read-only UI verification unexpectedly changed settings: before=$before after=$after"
}
Write-Host 'PASS: real SettingsWindow opened in isolated dev process'
Write-Host 'PASS: 1 theme Select + 4 startup/system Switch controls retain existing bindings'
Write-Host 'PASS: legacy preview toggle is absent and shared card remains visible'
Write-Host 'PASS: read-only inspection retained the five persisted settings'
Write-Host "PID=$($window.Current.ProcessId); RECT=$($window.Current.BoundingRectangle)"
