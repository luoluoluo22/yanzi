param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$javaSource = Join-Path $repo 'mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile'
$classes = Join-Path $env:TEMP ('YanziDev/communication-java/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $classes | Out-Null
Push-Location $repo
try {
    $tests = @(Get-ChildItem cloudflare/src, cloudflare/tests, protocol/sdk, sdk/javascript -File |
        Where-Object { $_.Name -match '\.test\.(mjs|js)$' } | ForEach-Object { $_.FullName })
    & node --test @tests
    if ($LASTEXITCODE -ne 0) { throw 'Worker and SDK scenario tests failed.' }

    & javac -encoding UTF-8 -d $classes (Join-Path $javaSource 'CloudRequestRetry.java') `
        (Join-Path $javaSource 'LanConnectionHealth.java') `
        (Join-Path $PSScriptRoot 'java/LanConnectionHealthVerification.java') `
        (Join-Path $javaSource 'HttpResponseBody.java') `
        (Join-Path $PSScriptRoot 'java/CloudRequestRetryVerification.java') `
        (Join-Path $PSScriptRoot 'java/HttpResponseBodyVerification.java')
    if ($LASTEXITCODE -ne 0) { throw 'Android transport verification compilation failed.' }
    foreach ($verification in @('CloudRequestRetryVerification', 'HttpResponseBodyVerification', 'LanConnectionHealthVerification')) {
        & java -cp $classes "cc.luoluoluo.yanzi.mobile.$verification"
        if ($LASTEXITCODE -ne 0) { throw "Android transport verification failed: $verification" }
    }

    & dotnet run --project src/Yanzi.CoreVerification -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Shared account and settings concurrency verification failed.' }
    & (Join-Path $PSScriptRoot 'test-network-retry.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Desktop transport verification failed.' }
    Write-Output "COMMUNICATION_FOUNDATION=PASSED; Java artifacts: $classes"
} finally {
    Pop-Location
}
