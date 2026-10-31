# 燕子宿主事件调度：隔离任务日志的可重复验收。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & dotnet build 'src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj' -c Debug -p:SkipStopRunningApp=true -v:q
    if ($LASTEXITCODE -ne 0) { throw "dispatch verification build failed ($LASTEXITCODE)" }
    & dotnet 'src/Yanzi.SyncVerification/bin/Debug/net9.0-windows/Yanzi.SyncVerification.dll' --host-dispatch
    if ($LASTEXITCODE -ne 0) { throw "dispatch isolation test failed ($LASTEXITCODE)" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File 'scripts/verify-miniapp-repository-isolation.ps1'
    if ($LASTEXITCODE -ne 0) { throw "miniapp isolation regression failed ($LASTEXITCODE)" }
    Write-Output 'HOST_DISPATCH_GATE_PASS'
}
finally { Pop-Location }
