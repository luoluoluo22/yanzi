param(
    [Parameter(Mandatory=$true)][string]$Serial,
    [Parameter(Mandatory=$true)][string]$AppApk,
    [Parameter(Mandatory=$true)][string]$TestApk
)
$ErrorActionPreference='Stop'
if($Serial -notmatch '^emulator-\d+$'){throw 'LAN fault fixtures require an explicitly selected emulator.'}
$adb='F:\SDK\platform-tools\adb.exe'
$package='cc.luoluoluo.yanzi.mobile.dev'
$artifact=Join-Path $env:TEMP ('YanziDev/lan-native/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $artifact | Out-Null
function Run-Adb([string[]]$Arguments){
    $value=@(& $adb -s $Serial @Arguments)
    if($LASTEXITCODE -ne 0){throw 'ADB fixture operation failed.'}
    return $value
}
try {
    Run-Adb @('install','-r',(Resolve-Path -LiteralPath $AppApk).Path) | Out-Null
    Run-Adb @('install','-r',(Resolve-Path -LiteralPath $TestApk).Path) | Out-Null
    Run-Adb @('shell','pm','clear',$package) | Out-Null
    $result=Run-Adb @('shell','am','instrument','-w',($package+'.test/cc.luoluoluo.yanzi.mobile.LanRecoveryTest'))
    [IO.File]::WriteAllLines((Join-Path $artifact 'result.txt'),$result,[Text.UTF8Encoding]::new($false))
    if(($result -join "`n") -notmatch 'LAN_NATIVE_RECOVERY=PASSED'){throw ($result -join "`n")}
    Write-Output ($result -join "`n")
    Write-Output "LAN_NATIVE_ARTIFACTS=$artifact"
} finally {
    Run-Adb @('shell','am','force-stop',$package) | Out-Null
    Run-Adb @('shell','pm','clear',$package) | Out-Null
    Run-Adb @('shell','am','start','-n',($package+'/cc.luoluoluo.yanzi.mobile.MainActivity')) | Out-Null
}
