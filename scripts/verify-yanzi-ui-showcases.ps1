param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = if ($ProcessId -gt 0) { Get-Process -Id $ProcessId -ErrorAction Stop } else {
    Get-Process 'Yanzi.UI.Gallery' | Sort-Object StartTime -Descending | Select-Object -First 1
}
if ($p.MainWindowHandle -eq 0) { throw "Gallery has no window" }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$a = [System.Windows.Automation.AutomationElement]
function Find([string]$name, $type) {
    $condition=New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($a::NameProperty,$name)),
        (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty,$type)))
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Verify([bool]$test, [string]$description) {
    if (-not $test) { throw "FAIL: $description" }
    Write-Host "OK: $description"
}
$toggle=Find '☰ 目录' ([System.Windows.Automation.ControlType]::Button)
if($toggle) { $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
$filter=Find '筛选官方组件' ([System.Windows.Automation.ControlType]::Edit)
if($filter) { $filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('') }

$expect = @(
    @{Name='Accordion'; Child='What is your return policy?'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Alert'; Child='Payment successful'; Type=[System.Windows.Automation.ControlType]::Text},
    @{Name='Attachment'; Child='Remove sales-dashboard.pdf'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Avatar'; Child='+3'; Type=[System.Windows.Automation.ControlType]::Text},
    @{Name='Button Group'; Child='Archive'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Calendar'; Child='2026-10-08'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Card'; Child='Login'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Combobox'; Child='Combobox search'; Type=[System.Windows.Automation.ControlType]::Edit},
    @{Name='Button'; Child='Default'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Input'; Child='Email input'; Type=[System.Windows.Automation.ControlType]::Edit},
    @{Name='Select'; Child='Select option'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Dialog'; Child='Edit profile'; Type=[System.Windows.Automation.ControlType]::Button},
    @{Name='Dropdown Menu'; Child='Open menu ▾'; Type=[System.Windows.Automation.ControlType]::Button}
)
foreach ($entry in $expect) {
    $nav=Find $entry.Name ([System.Windows.Automation.ControlType]::Button)
    Verify ([bool]$nav) "sidebar navigates $($entry.Name)"
    $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 60
    $preview=Find '当前 WPF 预览' ([System.Windows.Automation.ControlType]::Text)
    $official=Find ('官方参考 / '+$entry.Name) ([System.Windows.Automation.ControlType]::Text)
    Verify ([bool]$preview -and [bool]$official -and
        $preview.Current.BoundingRectangle.Top -lt $official.Current.BoundingRectangle.Top) "main example precedes API prose for $($entry.Name)"
    $child=Find $entry.Child $entry.Type
    Verify ([bool]$child) "functional example exists for $($entry.Name)"
}
Write-Host "PASS: 13 source-aligned scenarios and preview-first ordering verified"
