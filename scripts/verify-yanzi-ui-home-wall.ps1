param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$C = [System.Windows.Automation.ControlType]
$p = Get-Process -Id $ProcessId -ErrorAction Stop
if ($p.MainWindowHandle -eq 0) { throw 'New Gallery window not created' }
$root = $A::FromHandle($p.MainWindowHandle)
function Find([string]$name, $control) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty,$name)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty,$control)))
    return $root.FindFirst($T::Descendants,$cond)
}
function Check([bool]$ok,[string]$msg) {
    if(-not $ok) { throw $msg }
    Write-Output ('PASS: '+$msg)
}
function CountHomeTiles {
    $all=$root.FindAll($T::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty,$C::Button)))
    $found=0
    foreach($element in $all) { if($element.Current.Name.StartsWith('查看组件 ')) {$found++} }
    return $found
}
$names=@([regex]::Matches(
    (Get-Content 'src/Yanzi.UI.Wpf/YanziComponentRegistry.cs' -Raw -Encoding UTF8),
    'new\("([^"]+)", YanziComponentStatus') | ForEach-Object {$_.Groups[1].Value})
Check ($names.Count -eq 64) 'source public registry has 64 components'
if ((CountHomeTiles) -eq 0) {
    $overview=Find '总览' $C::Button
    if ($null -ne $overview) { $overview.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250 }
}
Check ((CountHomeTiles) -eq 64) 'homepage contains 64 distinct title buttons'
foreach($name in $names) {
    Check ($null -ne (Find ('查看组件 '+$name) $C::Button)) ('homepage item '+$name)
}
$search=Find '首页筛选组件' $C::Edit
Check ($null -ne $search) 'homepage registry search is real editable input'
$v=$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$v.SetValue('Badge')
Start-Sleep -Milliseconds 120
Check ((CountHomeTiles) -eq 1) 'filter by Badge reduces 64 to 1'
$badge=Find '查看组件 Badge' $C::Button
Check ($null -ne $badge) 'Badge entry persists after filtering'
$badge.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 140
Check ($null -ne (Find '官方参考 / Badge' $C::Text)) 'Badge title navigated to independent detail page'
$back=Find '返回全部组件首页' $C::Button
Check ($null -ne $back) 'detail has return-to-home button'
$back.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 220
$search=Find '首页筛选组件' $C::Edit
Check ($search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'Badge') 'return preserved filter input'
Check ((CountHomeTiles) -eq 1) 'return preserved filtered component count'
$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
Start-Sleep -Milliseconds 120
Check ((CountHomeTiles) -eq 64) 'clear filter restores all 64 titles'
$last=Find '查看组件 Typography' $C::Button
$last.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 160
Check ($null -ne (Find '官方参考 / Typography' $C::Text)) 'last registry component navigates correctly'
$back=Find '返回全部组件首页' $C::Button
$back.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 170
Check ((CountHomeTiles) -eq 64) 'return restores whole component wall'

# The homepage must reuse genuine public Card / Field / Direction components,
# not hand-drawn placeholders that drift away from the shared library.
foreach($sample in @(
    @{ Name='Card'; Expected='Login to your account' },
    @{ Name='Field'; Expected='公共 Field / Label API' },
    @{ Name='Direction'; Expected='يمين · Right-to-left · 方向' }
)) {
    $entry=Find ('查看组件 '+$sample.Name) $C::Button
    Check ($null -ne $entry) ('component entry exists: '+$sample.Name)
    $entry.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 170
    Check ($null -ne (Find $sample.Expected $C::Text)) ('shared library preview visible: '+$sample.Name)
    (Find '返回全部组件首页' $C::Button).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
}

# Scroll restore is tested against the actual WPF scroll viewer, not a fake variable.
$all=$root.FindAll($T::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
$scrollPattern=$null
foreach($candidate in $all) {
    $o=$null
    if ($candidate.Current.ControlType -eq $C::Pane -and
        $candidate.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$o)) {
        $sp=[System.Windows.Automation.ScrollPattern]$o
        if($sp.Current.VerticallyScrollable) {$scrollPattern=$sp;break}
    }
}
Check ($null -ne $scrollPattern) 'catalog has true vertical scroll'
$scrollPattern.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll,65)
Start-Sleep -Milliseconds 150
$before=$scrollPattern.Current.VerticalScrollPercent
Check ($before -gt 45) 'catalog can scroll into lower entries'
$visibleEntry=Find '查看组件 Radio Group' $C::Button
Check ($null -ne $visibleEntry -and -not $visibleEntry.Current.IsOffscreen) 'scroll test targets a visible component'
$visibleEntry.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 160
(Find '返回全部组件首页' $C::Button).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 400
$all=$root.FindAll($T::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
$after=$null
foreach($candidate in $all) {
    $o=$null
    if ($candidate.Current.ControlType -eq $C::Pane -and
        $candidate.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$o)) {
        $sp=[System.Windows.Automation.ScrollPattern]$o
        if($sp.Current.VerticallyScrollable){$after=$sp.Current.VerticalScrollPercent;break}
    }
}
Check ($null -ne $after -and [Math]::Abs($after-$before) -lt 7) ('return restores scroll position '+[Math]::Round($before,1)+'% -> '+[Math]::Round($after,1)+'%')
Write-Output ('HOME_WALL_REGRESSION=PASS;PID='+$ProcessId+';TOTAL=64')
