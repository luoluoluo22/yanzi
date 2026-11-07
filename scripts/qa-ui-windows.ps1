#requires -Version 5.1
<#
Run real Windows WPF UI smoke tests in an isolated Git worktree.
This does not claim complete visual, screen-reader, or live-service QA.
#>
[CmdletBinding()]
param(
  [string]$RepoPath = (Join-Path $PSScriptRoot '..'),
  [string]$ExpectedSha = '',
  [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
  [ValidateRange(1,1000)][int]$MinimumPassingTests = 15,
  [ValidateRange(30,3600)][int]$StepTimeoutSeconds = 240,
  [string]$EvidenceDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Real Windows is required for UI smoke tests.' }
$root = (Resolve-Path -LiteralPath $RepoPath).Path
$sha = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Invalid Git worktree' }
if ($ExpectedSha -and $sha -ne $ExpectedSha) { throw "SHA mismatch: expected $ExpectedSha but found $sha" }
$changes = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0 -or @($changes | Where-Object { $_ }).Count -gt 0) { throw 'QA worktree is dirty: use an isolated clean worktree.' }
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $env:TEMP ('yanzi-ui-qa-' + $sha.Substring(0,12)) }
$null = New-Item -Path $EvidenceDirectory -ItemType Directory -Force
$report = [ordered]@{
 schema_version=1
 result='failed'
 machine=$env:COMPUTERNAME
 git_sha=$sha
 started_utc=[DateTimeOffset]::UtcNow.ToString('o')
 finished_utc=$null
 configuration=$Configuration
 checks=@()
 passed=0
 failed=0
 pending_verification=@('Both light/dark visual screenshots','Keyboard and assistive-technology interaction beyond automated checks','Live account and external service interactions')
 error=$null
}
function Run-Step([string]$label,[string[]]$argv) {
 $path=Join-Path $EvidenceDirectory ($label+'.log')
 $stdoutPath=$path+'.stdout'
 $stderrPath=$path+'.stderr'
 $argumentString=($argv|ForEach-Object {if($_ -match '\s'){ '"'+$_+'"' }else{$_}}) -join ' '
 $proc=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $argumentString -PassThru -NoNewWindow -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
 if(-not $proc.WaitForExit($StepTimeoutSeconds*1000)) {
  & taskkill.exe /F /T /PID $proc.Id 2>$null | Out-Null
  throw ("Step timed out after $StepTimeoutSeconds seconds: $label")
 }
 $proc.Refresh()
 $code=$proc.ExitCode
 if ($null -eq $code) { throw ("Step exited without a readable exit code: "+$label) }
 $out=@(Get-Content -LiteralPath $stdoutPath -ErrorAction SilentlyContinue)+@(Get-Content -LiteralPath $stderrPath -ErrorAction SilentlyContinue)
 $out | Set-Content -LiteralPath $path -Encoding UTF8
 $report.checks += [ordered]@{step=$label;exit_code=$code;log=$path}
 Write-Output ("STEP "+$label+" exit="+$code)
 if ($code -ne 0) { throw ("Step failed: "+$label+"; "+(($out | Select-Object -Last 12)-join [Environment]::NewLine)) }
 return $out
}
$failed=$false
try {
 $hostProject=Join-Path $root 'src\OpenQuickHost\OpenQuickHost.csproj'
 $testProject=Join-Path $root 'tests\WpfUiMigrationSmoke\WpfUiMigrationSmoke.csproj'
 $nuget=Join-Path $root 'tests\WpfUiMigrationSmoke\NuGet.Config'
 if (-not(Test-Path $testProject) -or -not(Test-Path $nuget)){throw 'WPF smoke test project or NuGet config missing'}
 $hostAssets=Join-Path $root 'src\OpenQuickHost\obj\project.assets.json'
 if (-not(Test-Path $hostAssets)) {
  $null=Run-Step 'host-restore' @('restore',$hostProject,'--nologo','-v','quiet','--ignore-failed-sources')
 }
 $null=Run-Step 'host-build' @('build',$hostProject,'-c',$Configuration,'--no-restore','--nologo','-v','quiet','-m:4')
 $null=Run-Step 'test-restore' @('restore',$testProject,'--configfile',$nuget,'--nologo','-v','quiet')
 $lines=@(Run-Step 'wpf-smoke' @('run','--project',$testProject,'-c',$Configuration,'--no-restore','-v','quiet'))
 $lastLine=($lines|Where-Object {$_ -match 'RESULT pass=\d+ fail=\d+'}|Select-Object -Last 1)
 if (-not $lastLine -or $lastLine -notmatch 'RESULT pass=(\d+) fail=(\d+)') {throw 'WPF STA smoke summary missing'}
 $report.passed=[int]$Matches[1]; $report.failed=[int]$Matches[2]
 if ($report.failed -ne 0 -or $report.passed -lt $MinimumPassingTests) {throw "WPF smoke failed: pass=$($report.passed) fail=$($report.failed)"}
 $report.result='windows-runtime-smoke-passed-awaiting-functional-visual-qa'
} catch {
 $failed=$true; $report.error=$_.Exception.Message
 Write-Output ("QA_FAILURE="+$report.error)
} finally {
 $report.finished_utc=[DateTimeOffset]::UtcNow.ToString('o')
 $evidence=Join-Path $EvidenceDirectory 'windows-ui-qa.json'
 $report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $evidence -Encoding UTF8
 Write-Output ("EVIDENCE="+$evidence)
 Write-Output ("QA_RESULT="+$report.result+" MACHINE="+$report.machine+" SHA="+$report.git_sha+" PASS="+$report.passed+" FAIL="+$report.failed)
}
if($failed){exit 1}
exit 0
