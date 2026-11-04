#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $result = @(& dotnet run --project tests/AiChatLogPerformanceVerification -c Release)
    if ($LASTEXITCODE -ne 0 -or $result -notcontains 'AI_CHAT_LOG_PERFORMANCE_VERIFIED') {
        throw 'AI chat log cache regression failed.'
    }
    & dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Windows host build failed.' }
    & powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-desktop-performance.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Desktop and AI chat regression failed.' }
    Write-Output 'AI_CHAT_LOG_PERFORMANCE_INTEGRATION_VERIFIED'
}
finally { Pop-Location }
