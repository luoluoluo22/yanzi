param(
    [Parameter(Mandatory=$true)][string]$Serial,
    [Parameter(Mandatory=$true)][string]$AppApk,
    [Parameter(Mandatory=$true)][string]$TestApk
)
$ErrorActionPreference='Stop'
if($Serial -ne '81f7e66d' -and $Serial -notmatch '^emulator-\d+$'){throw 'Select the authorized OnePlus or an emulator.'}
$adb='F:\SDK\platform-tools\adb.exe'
$package='cc.luoluoluo.yanzi.mobile.dev'
function Run-Adb([string[]]$Arguments){
    $value=@(& $adb -s $Serial @Arguments)
    if($LASTEXITCODE -ne 0){throw 'ADB verification failed.'}
    return $value
}
$productionBefore=(Run-Adb @('shell','pm','path','cc.luoluoluo.yanzi.mobile')) -join "`n"
try {
    Run-Adb @('install','--no-streaming','-r',(Resolve-Path -LiteralPath $AppApk).Path) | Out-Null
    Run-Adb @('install','--no-streaming','-r',(Resolve-Path -LiteralPath $TestApk).Path) | Out-Null
    $result=Run-Adb @('shell','am','instrument','-w','-e','suite','update','-e','probeNetwork','true',($package+'.test/cc.luoluoluo.yanzi.mobile.LanRecoveryTest'))
    $resultDirectory=Join-Path $env:TEMP ('YanziDev/update-feedback/'+$Serial)
    New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
    [IO.File]::WriteAllLines((Join-Path $resultDirectory 'result.txt'),$result,[Text.UTF8Encoding]::new($false))
    Write-Output ($result -join "`n")
    if(($result -join "`n") -notmatch 'UPDATE_FEEDBACK=PASSED'){throw 'Native update feedback verification failed.'}
    $productionAfter=(Run-Adb @('shell','pm','path','cc.luoluoluo.yanzi.mobile')) -join "`n"
    if($productionBefore -ne $productionAfter){throw 'Production installation unexpectedly changed.'}
} finally {
    Run-Adb @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity')) | Out-Null
}
