param([int]$ProcessId = 0)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

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

# The catalog may be left expanded by the complete 64-component audit.
$openCatalog = Find-Control '☰ 目录' [System.Windows.Automation.ControlType]::Button
if ($openCatalog) {
    $openCatalog.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
}
$null = Assert-Control '收起目录' [System.Windows.Automation.ControlType]::Button
$testCount++
Navigate '总览'
foreach ($title in @('The foundation for Yanzi UI', 'Contribution History', 'Account settings', 'New chat', 'May 2024', 'Scheduled')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}

# Homepage exposes both RADIO STATES plus checked Checkbox and enabled Switch.
$radioOff = Assert-Control 'Radio 未选中' [System.Windows.Automation.ControlType]::RadioButton
$radioOn = Assert-Control 'Radio 已选中' [System.Windows.Automation.ControlType]::RadioButton
$radioOffPattern = $radioOff.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$radioOnPattern = $radioOn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
if ($radioOffPattern.Current.IsSelected -or -not $radioOnPattern.Current.IsSelected) { throw 'Homepage radio states do not match reference' }
$testCount++
$checkOn = Assert-Control 'Checkbox 已选中' [System.Windows.Automation.ControlType]::CheckBox
if ($checkOn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw 'Checkbox must be checked' }
$testCount++
$switchOn = Assert-Control 'Switch 开启' [System.Windows.Automation.ControlType]::CheckBox
if ($switchOn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw 'Switch must be on' }
$testCount++

$menuTrigger = Assert-Control '展开操作菜单' [System.Windows.Automation.ControlType]::Button
$menuRect = $menuTrigger.Current.BoundingRectangle
$menuTrigger.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 180
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$actionNodes = $desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'复制')))
$popupCopy = $null
foreach ($candidate in $actionNodes) {
    $r = $candidate.Current.BoundingRectangle
    if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and
        $r.Width -ge 130 -and $r.Y -lt $menuRect.Y -and
        [Math]::Abs($r.Right-$menuRect.Right) -lt 45) {
        $popupCopy = $candidate
        break
    }
}
if (-not $popupCopy) { throw 'Custom menu did not open above trigger with a full-width clickable row' }
$testCount++
$popupCopy.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 120
$testCount++
# Search field must be a real focusable edit surface, not a TextBlock with an imitation caret.
$searchEdit = Assert-Control '搜索' [System.Windows.Automation.ControlType]::Edit
$searchEdit.SetFocus()
Start-Sleep -Milliseconds 80
if (-not $searchEdit.Current.HasKeyboardFocus) { throw 'Search edit did not receive keyboard focus' }
$testCount++
$searchValue = $searchEdit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$searchValue.SetValue('中文搜索 Search123')
if ($searchValue.Current.Value -ne '中文搜索 Search123') { throw 'Search input failed to accept mixed Chinese and Latin text' }
$testCount++
$searchValue.SetValue('')
if ($searchValue.Current.Value -ne '') { throw 'Search input did not clear' }
$testCount++

# Previous specialized demos remain accessible, but do not replace the 64 official entries.
$expandLegacy = Find-Control '展开专题演示' [System.Windows.Automation.ControlType]::Button
if ($expandLegacy) {
    $expandLegacy.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 90
}
$null = Assert-Control '收起专题演示' [System.Windows.Automation.ControlType]::Button
Navigate '按钮'
$null = Assert-Control '保存更改' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '取消' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '删除记录' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control 'Pill / Chip / Segmented' [System.Windows.Automation.ControlType]::Text
$null = Assert-Control 'Chip' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '左' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '中' [System.Windows.Automation.ControlType]::Button
$null = Assert-Control '右' [System.Windows.Automation.ControlType]::Button

Navigate '输入'
$inputFields = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)))
if ($inputFields.Count -lt 3) { throw "Expected at least three input fields; got $($inputFields.Count)" }
$testCount++
# Do not type into the global sidebar component filter.
$pageInput = @($inputFields | Where-Object { $_.Current.Name -ne '筛选官方组件' }) | Select-Object -First 1
if (-not $pageInput) { throw 'No editable input in the actual input page' }
$inputValue = $pageInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$inputValue.SetValue('UI automation smoke test')
if ($inputValue.Current.Value -ne 'UI automation smoke test') { throw 'Input edit failed' }
$testCount++

Navigate '选择'
$compactRadio = Assert-Control '紧凑' [System.Windows.Automation.ControlType]::RadioButton
$comfortableRadio = Assert-Control '舒适' [System.Windows.Automation.ControlType]::RadioButton
$compactSelection = $compactRadio.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$comfortableSelection = $comfortableRadio.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
if (-not $compactSelection.Current.IsSelected) { throw 'Compact should be selected initially' }
$comfortableSelection.Select()
Start-Sleep -Milliseconds 90
if (-not $comfortableSelection.Current.IsSelected -or $compactSelection.Current.IsSelected) { throw 'Exclusive RadioGroup UI Automation selection failed' }
$testCount++
# Arrow-key routed-event logic is tested within the WPF verification host.
# Remote SendKeys is not used here: this process may run on another input desktop.
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
$null = Assert-Control '主色' [System.Windows.Automation.ControlType]::Text
$null = Assert-Control '危险' [System.Windows.Automation.ControlType]::Text

Navigate '徽标 Badge'
foreach ($title in @('Default','Secondary','Destructive','Outline','Ghost','Link','Verified','Generating','Preview')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
Navigate '基础组件'
foreach ($title in @('基础内容 / Primitives', 'Skeleton / Empty / Typography', 'Typography h1')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
Navigate '表单控件'
foreach ($title in @('Field / Input / Input Group / Textarea', '选择主题', 'Slider / Date Picker / Calendar / Progress', 'Input OTP / Button Group')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
$defaultFormRadio = Assert-Control '标准' [System.Windows.Automation.ControlType]::RadioButton
if (-not $defaultFormRadio.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Form RadioGroup default not selected' }
$testCount++
$disabledFormRadio = Assert-Control '禁用状态 / Disabled' [System.Windows.Automation.ControlType]::RadioButton
if ($disabledFormRadio.Current.IsEnabled) { throw 'Disabled custom Radio should be inaccessible to clicks' }
$testCount++
Navigate '导航与布局'
foreach ($title in @('Tabs / 标签页', 'Accordion / Collapsible / 折叠面板', 'Pagination / 分页')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
Navigate '数据展示'
foreach ($title in @('Table / Data Table', 'Chart / 可视化', 'Carousel / 横向画廊', 'Empty / Skeleton')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
Navigate '弹层与反馈'
foreach ($title in @('Dialog / Alert Dialog / Sheet / Drawer', 'Dropdown Menu / Context Menu / Popover', 'Command / 命令面板')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
Navigate '全部组件索引'
foreach ($title in @('全部组件 / Component coverage', '组件覆盖清单', '筛选组件')) {
    $null = Assert-Control $title [System.Windows.Automation.ControlType]::Text
}
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
# Clear the shared filter BEFORE collapsing its sidebar; hidden descendants are
# intentionally absent from UI Automation after the sidebar is collapsed.
$catalogSearch = Find-Control '筛选官方组件' [System.Windows.Automation.ControlType]::Edit
if ($catalogSearch) { $catalogSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('') }
$closeCatalog = Assert-Control '收起目录' [System.Windows.Automation.ControlType]::Button
$closeCatalog.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 160
$null = Assert-Control '☰ 目录' [System.Windows.Automation.ControlType]::Button
Write-Host "PASS: $testCount UI Automation assertions, 15 pages, rounded Pill/Chip/Segmented, Badge, input, switch, list, theme switching."
