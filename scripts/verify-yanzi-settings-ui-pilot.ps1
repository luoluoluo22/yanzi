param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$a = [System.Windows.Automation.AutomationElement]
$type = [System.Windows.Automation.ControlType]
$windowName = '燕子设置 · 统一 UI 试用（开发版）'
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
$toggle = Find '新版样式' $type::CheckBox
if (-not $toggle) { throw 'Visual-only library toggle was not found' }
$togglePattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($togglePattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
    $togglePattern.Toggle()
}
Start-Sleep -Milliseconds 120
if (-not (Find '基础设置' $type::Text)) { throw 'Live general settings Card absent' }
$combos = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty, $type::ComboBox)))
if ($combos.Count -lt 1) { throw 'Real theme ComboBox was lost' }
$settings = Join-Path $env:LOCALAPPDATA 'OpenQuickHost.Dev\appsettings.local.json'
$keys = @('ThemeMode','LaunchAtStartup','EnableAutoUpdate','CloseToTray','RefreshCloudOnStartup')
function SavedValues {
    if (-not (Test-Path $settings)) { throw 'Dev settings not found' }
    $data = Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json
    return (($keys | ForEach-Object { "$_=$($data.$_)" }) -join ';')
}
$before = SavedValues
$togglePattern.Toggle()
Start-Sleep -Milliseconds 180
if ($togglePattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) {
    throw 'Preview toggle failed to turn off'
}
if (Find '基础设置' $type::Text) { throw 'Original flat settings were not restored' }
$afterOriginal = SavedValues
$togglePattern.Toggle()
Start-Sleep -Milliseconds 180
if ($togglePattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
    throw 'Preview toggle failed to re-enable'
}
if (-not (Find '基础设置' $type::Text)) { throw 'Shared Card was not restored' }
$afterPilot = SavedValues
if ($before -ne $afterOriginal -or $before -ne $afterPilot) {
    throw "The visual-only switch mutated settings values: before=$before original=$afterOriginal pilot=$afterPilot"
}
Write-Host 'PASS: real SettingsWindow opened in isolated dev process'
Write-Host 'PASS: 1 theme Select + 4 startup/system Switch controls retain existing bindings'
Write-Host 'PASS: public UI Card on -> original controls -> public UI Card restores'
Write-Host 'PASS: preview toggle did not mutate five persisted settings'
Write-Host "PID=$($window.Current.ProcessId); RECT=$($window.Current.BoundingRectangle)"
