# Runtime E2E QA: real Win32 sampling and isolated WebView2 Promise event/exit.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & dotnet build 'src/Yanzi.SyncVerification/Yanzi.SyncVerification.csproj' --no-restore -c Debug -p:SkipStopRunningApp=true -v:q
    if ($LASTEXITCODE -ne 0) { throw "Runtime verification build failed ($LASTEXITCODE)" }
    & dotnet 'src/Yanzi.SyncVerification/bin/Debug/net9.0-windows/Yanzi.SyncVerification.dll' --host-dispatch
    if ($LASTEXITCODE -ne 0) { throw "Dispatch regression failed ($LASTEXITCODE)" }
    & dotnet 'src/Yanzi.SyncVerification/bin/Debug/net9.0-windows/Yanzi.SyncVerification.dll' --host-runtime-dispatch
    if ($LASTEXITCODE -ne 0) { throw "Real Windows/WebView runtime QA failed ($LASTEXITCODE)" }
    Write-Output 'HOST_RUNTIME_DISPATCH_QA_PASS'
}
finally { Pop-Location }
