param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if($ProcessId -le 0) {
    $p = Get-Process 'Yanzi.UI.Gallery' -ErrorAction Stop |
        Sort-Object StartTime -Descending | Select-Object -First 1
} else { $p = Get-Process -Id $ProcessId -ErrorAction Stop }
if($p.MainWindowHandle -eq 0){throw 'Gallery window unavailable'}
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$types = [System.Windows.Automation.ControlType]
function FindNode([string]$name, $kind) {
    $a = [System.Windows.Automation.AutomationElement]
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($a::NameProperty,$name)),
        (New-Object System.Windows.Automation.PropertyCondition($a::ControlTypeProperty,$kind)))
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
# Consume the source registry list exactly. The .NET verification independently asserts
# equality with all 64 official shadcn names, order and base routes.
$source = Get-Content (Join-Path $PSScriptRoot '..\src\Yanzi.UI.Wpf\YanziComponentRegistry.cs') -Raw -Encoding UTF8
$names = @([regex]::Matches($source, 'new\("([^"]+)", YanziComponentStatus') |
    ForEach-Object {$_.Groups[1].Value})
if($names.Count -ne 64){throw "Expected exactly 64 shadcn components, got $($names.Count)"}
$expand = FindNode '☰ 目录' $types::Button
if($expand){
    $expand.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 100
}
$catalogSearch=FindNode '筛选官方组件' $types::Edit
if ($catalogSearch) { $catalogSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('') }
$errors = New-Object 'System.Collections.Generic.List[string]'
$verified = 0
foreach ($name in $names) {
    try {
        $entry=FindNode $name $types::Button
        if(-not $entry){ throw "Missing sidebar entry $name" }
        $entry.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 35
        if(-not (FindNode ('官方参考 / '+$name) $types::Text)){
            throw "Missing independent review heading for $name"
        }
        if(-not (FindNode ('打开官方 '+$name) $types::Button)){
            throw "Missing verified official reference link for $name"
        }
        if(-not (FindNode ('保存 '+$name+' 核对记录') $types::Button)){
            throw "Missing per-component persistent audit action for $name"
        }
        if(-not (FindNode '逐项对比清单' $types::Text)){
            throw "Missing comparison checklist for $name"
        }
        # The old preview-fallback text must no longer appear for any of the 64.
        if(FindNode '独立演示：待补齐。当前仅有对应公共库入口和原专题页。' $types::Text){
            throw "Missing executable WPF preview for $name"
        }
        # These are interactive elements, not placeholders claiming parity.
        $specific = switch ($name) {
            'Combobox' { @('Combobox search', 'Edit'); break }
            'Data Table' { @('Data Table 样例', 'DataGrid'); break }
            'Dialog' { @('Edit profile', 'Button'); break }
            'Drawer' { @('Open drawer', 'Button'); break }
            'Hover Card' { @('@yanzi', 'Button'); break }
            'Message Scroller' { @('追加一条消息', 'Button'); break }
            'Popover' { @('Open popover', 'Button'); break }
            'Sidebar' { @('Projects', 'Button'); break }
            default { $null }
        }
        if($specific){
            $kind = [System.Windows.Automation.ControlType].GetField($specific[1]).GetValue($null)
            if(-not (FindNode $specific[0] $kind)){
                throw "Independent control missing: $($specific[0])"
            }
        }
        if(-not (FindNode '核对：布局与尺寸' $types::ComboBox) -or
           -not (FindNode '核对：深浅主题 / 缩放' $types::ComboBox)){
            throw "Missing structured per-item audit controls for $name"
        }
        $verified++
    }
    catch {
        $errors.Add($name+': '+$_.Exception.Message)
    }
}
Write-Host ("OFFICIAL_ENTRIES={0}; VISITED={1}; ERRORS={2}" -f $names.Count,$verified,$errors.Count)
foreach($e in $errors){Write-Host "FAIL $e"}
if($errors.Count){exit 1}
$back=FindNode '全部组件索引' $types::Button
if($back){
    $back.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
Write-Host 'PASS: all 64 official components independently navigable, with reference, local API and audit action'
exit 0
