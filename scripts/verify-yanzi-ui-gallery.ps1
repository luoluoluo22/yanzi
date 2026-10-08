param([int]$ProcessId = 0)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if ($ProcessId -le 0) {
    $proc = Get-Process -Name 'Yanzi.UI.Gallery' -ErrorAction Stop |
        Sort-Object StartTime -Descending | Select-Object -First 1
} else {
    $proc = Get-Process -Id $ProcessId -ErrorAction Stop
}
if ($proc.HasExited -or $proc.MainWindowHandle -eq 0) {
    throw 'Yanzi UI evaluation window is not running.'
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$A = [System.Windows.Automation.AutomationElement]
$C = [System.Windows.Automation.ControlType]
$T = [System.Windows.Automation.TreeScope]
$testCount = 0

function Find-Control([string]$name, $type) {
    if ($type -is [string] -and $type -match 'ControlType\]::([A-Za-z]+)$') {
        $type = [System.Windows.Automation.ControlType].GetField($Matches[1]).GetValue($null)
    }
    $a = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $b = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    $condition = New-Object System.Windows.Automation.AndCondition($a, $b)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Assert-Control([string]$name, $type) {
    $control = Find-Control $name $type
    if ($null -eq $control) { throw "Missing $name ($type)" }
    $script:testCount++
    return $control
}
function Navigate([string]$name) {
    $button = Assert-Control $name [System.Windows.Automation.ControlType]::Button
    $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Start-Sleep -Milliseconds 150
}

Navigate '按钮'
$null = Assert-Control '保存更改' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '取消' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '删除记录' [System.Windows.Automation.ControlType]::Button

Navigate '输入'
$inputFields = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)))
if ($inputFields.Count -lt 3) { throw "Expected at least three input fields; got $($inputFields.Count)" }
$testCount++
$inputValue = $inputFields[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$inputValue.SetValue('UI automation smoke test')
if ($inputValue.Current.Value -ne 'UI automation smoke test') { throw 'Input edit failed' }
$testCount++

Navigate '选择'
$toggle = Assert-Control '允许后台同步' [System.Windows.Automation.ControlType]::CheckBox
$togglePattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
$before = $togglePattern.Current.ToggleState
$togglePattern.Toggle()
if ($togglePattern.Current.ToggleState -eq $before) { throw 'Switch toggle failed' }
$togglePattern.Toggle()
$testCount++

Navigate '列表'
$list = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::List)))
if (-not $list) { throw 'List not found' }
$testCount++
$item = $list.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'日历')))
if (-not $item) { throw 'Calendar list item not found' }
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$testCount++

Navigate '反馈'
foreach ($title in @('成功提示', '失败提示', '普通消息', '打开删除确认')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Button
}

Navigate '设计令牌'
$null = Assert-Control '强调色' [System.Windows.Automation.ControlType]::Text
$null = Assert-Control '危险' [System.Windows.Automation.ControlType]::Text

Navigate '评价与记录'
$null = Assert-Control '保存评价' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '复制评价摘要' [System.Windows.Automation.ControlType]::Button

$theme = Assert-Control '切换主题  ◑' [System.Windows.Automation.ControlType]::Button
$startedLight = ($null -ne (Find-Control '○ 浅色' [System.Windows.Automation.ControlType]::Text))
$theme.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 160
$expectedAfterChange = if ($startedLight) { '● 深色' } else { '○ 浅色' }
$null = Assert-Control $expectedAfterChange [System.Windows.Automation.ControlType]::Text
$theme.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$expectedRestored = if ($startedLight) { '○ 浅色' } else { '● 深色' }
$null = Assert-Control $expectedRestored [System.Windows.Automation.ControlType]::Text

Navigate '总览'
Write-Host "PASS: $testCount UI Automation assertions, 8 pages, input, switch, list selection and theme switching."
