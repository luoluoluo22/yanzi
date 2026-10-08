param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if ($ProcessId -eq 0) {
    $p=Get-Process 'Yanzi.UI.Gallery'|Sort-Object StartTime -Descending|Select-Object -First 1
} else { $p=Get-Process -Id $ProcessId -ErrorAction Stop }
$root=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$element=[System.Windows.Automation.AutomationElement]
function FindItem([string]$name, $type) {
    $c=New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($element::NameProperty,$name)),
        (New-Object System.Windows.Automation.PropertyCondition($element::ControlTypeProperty,$type)))
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
}
$path=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\UiReview\ComponentAudit\avatar.json'
$oldFile=if(Test-Path $path){[IO.File]::ReadAllBytes($path)}else{$null}
try {
    $show=FindItem '☰ 目录' ([System.Windows.Automation.ControlType]::Button)
    if($show){$show.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
    $avatar=FindItem 'Avatar' ([System.Windows.Automation.ControlType]::Button)
    if(!$avatar){throw 'Avatar missing from official directory'}
    $avatar.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 120
    $note=FindItem '组件对比备注' ([System.Windows.Automation.ControlType]::Edit)
    if(!$note){throw 'Missing editable comparison notes'}
    $sample='AUTOMATED-PERSISTENCE-VERIFY-' + [guid]::NewGuid().ToString('N')
    $note.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($sample)
    $save=FindItem '保存 Avatar 核对记录' ([System.Windows.Automation.ControlType]::Button)
    if(!$save){throw 'Missing save action'}
    $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 90
    if(!(Test-Path $path)){throw 'Audit record not saved'}
    $record=Get-Content $path -Raw -Encoding UTF8|ConvertFrom-Json
    if($record.Name -ne 'Avatar' -or $record.Notes -ne $sample -or $record.Results.Count -ne 6){
        throw 'Persisted record has the wrong name, text or six comparison states'
    }
    if(@($record.Results|Where-Object {$_ -ne '待核对'}).Count -ne 0){
        throw 'Unreviewed states should not silently become Pass'
    }
    $next=FindItem '下一项：Badge →' ([System.Windows.Automation.ControlType]::Button)
    if(!$next){throw 'Missing sequential Next action'}
    $next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 85
    if(!(FindItem '官方参考 / Badge' ([System.Windows.Automation.ControlType]::Text))){
        throw 'Next must navigate to Badge'
    }
    Write-Host 'PASS: audited Avatar record saved with six pending checks; Next navigates Badge; original record preserved'
}
finally {
    if($null -eq $oldFile){
        if(Test-Path $path){Remove-Item $path -Force}
    } else {
        [IO.File]::WriteAllBytes($path,$oldFile)
    }
}
exit 0
