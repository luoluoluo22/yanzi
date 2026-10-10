$ErrorActionPreference='Stop'
$root='F:\Desktop\kaifa\OpenQuickHost'
$source=Join-Path $root 'tools\idle-chatgpt-tasks'
$manifest=Get-Content (Join-Path $source 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.id -ne 'idle-chatgpt-tasks' -or $manifest.version -ne '0.2.4'){throw 'Incorrect candidate version'}
$text=Get-Content (Join-Path $source 'IdleTaskApp.cs') -Encoding UTF8 -Raw
foreach($symbol in @('ReadReportedStatus','LastDeploymentStatus','LastReleaseStatus','LastBusinessStatus','MaxInterruptedRetries','NextEligibleAt','idle.tasks.list')){
 if(-not $text.Contains($symbol)){throw "Regression symbol missing: $symbol"}
}
& dotnet run --project (Join-Path $root 'tests\IdleTaskRecoveryVerification\IdleTaskRecoveryVerification.csproj') -c Release -p:SkipStopRunningApp=true --no-launch-profile
if($LASTEXITCODE -ne 0){throw 'Idle task 0.2.4 fixture regression failed'}
Write-Output 'IDLE_0_2_4_PREFLIGHT_PASS'
