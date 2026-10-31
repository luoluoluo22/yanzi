#Requires -Version 5.1
# Offline fixture QA: no GitHub access, no installer, no repository writes.
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'release-lifecycle.ps1'
$base=Join-Path ([IO.Path]::GetTempPath()) ('yanzi-release-orchestrator-test-'+[guid]::NewGuid().ToString('N'))
$devices=Join-Path $base 'devices'
New-Item -Path $devices -ItemType Directory -Force | Out-Null
$version='99.8.7'
$published='2026-10-31T01:00:00Z'
$release=@{
    tag_name="v$version";draft=$false;prerelease=$false
    published_at=$published;html_url="https://github.com/luoluoluo22/yanzi/releases/tag/v$version"
    assets=@(@{name="Yanzi-win-Setup-$version.exe";size=91000000;digest='sha256:'+('a'*64)})
}
$release|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $base 'release.json') -Encoding UTF8
function Receipt([string]$machine,[string]$state='verified-running',[string]$v='99.8.7',
    [string]$time='2026-10-31T01:05:00Z',[bool]$running=$true) {
    return @{schemaVersion=1;machine=$machine;state=$state;installedVersion=$v;
        fileVersion=$v;checkedAtUtc=$time;processRunning=$running}
}
function Write-Receipt([string]$machine,$receipt) {
    $receipt|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $devices "$machine.json") -Encoding UTF8
}
function Run-Scenario([string]$name,[int]$expected) {
    $arguments=@('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass',
        '-File',$source,'-Version',$version,'-FixtureRoot',$base,'-DryRun','-StatusOnly',
        '-MaxWaitMinutes','2','-PollSeconds','2')
    $out=@(& powershell.exe @arguments 2>&1)
    $code=$LASTEXITCODE
    if ($code -ne $expected) {throw "$name expected=$expected actual=$code; $out"}
    Write-Output "PASS $name ($code)"
}
try {
    Write-Receipt 'PC-202409131508' (Receipt 'PC-202409131508')
    Write-Receipt 'CHINAMI-3IF988E' (Receipt 'CHINAMI-3IF988E')
    Run-Scenario 'both-installed-running' 0
    Write-Receipt 'CHINAMI-3IF988E' (Receipt 'CHINAMI-3IF988E' 'pending' '99.8.6')
    Run-Scenario 'old-office-version-not-success' 2
    Write-Receipt 'CHINAMI-3IF988E' (Receipt 'CHINAMI-3IF988E' 'verified-running' '99.8.7' '2026-10-30T22:00:00Z')
    Run-Scenario 'stale-receipt-not-success' 2
    Write-Receipt 'CHINAMI-3IF988E' (Receipt 'CHINAMI-3IF988E' 'failed')
    Run-Scenario 'failed-office-install-not-success' 2
    Write-Receipt 'CHINAMI-3IF988E' (Receipt 'CHINAMI-3IF988E' 'verified-running' '99.8.7' '2026-10-31T01:05:00Z' $false)
    Run-Scenario 'no-process-not-success' 2
    Remove-Item (Join-Path $devices 'CHINAMI-3IF988E.json') -Force
    Run-Scenario 'missing-receipt-not-success' 2
    Write-Output 'RELEASE_ORCHESTRATOR_QA=PASSED'
} finally {Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue}
