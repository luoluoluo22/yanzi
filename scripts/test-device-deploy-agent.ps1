#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$files = @(
    (Join-Path $PSScriptRoot 'device-deploy-agent.ps1'),
    (Join-Path $PSScriptRoot 'install-device-deploy-agent.ps1'),
    (Join-Path $PSScriptRoot 'deploy-to-devices.ps1')
)
foreach ($file in $files) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) { throw "PowerShell parser errors in $file : $errors" }
}

# Only the TEST harness uses a synthetic GitHub release response. The production
# receiver remains unmodified, pins the real publisher and checks SHA-256.
# This avoids unsafe CI dependencies on live GitHub API availability / WAF status.
$isolated = Join-Path ([IO.Path]::GetTempPath()) ('yanzi-device-deploy-test-' + [guid]::NewGuid().ToString('N'))
New-Item $isolated -ItemType Directory -Force | Out-Null
$runner = Join-Path $isolated 'mock-ci-receiver.ps1'
$template = @'
$ErrorActionPreference = 'Stop'
$env:LOCALAPPDATA = '__ISOLATED_ROOT__'
$agent = '__AGENT_SCRIPT__'
function Invoke-RestMethod {
    param([string] $Uri, [hashtable] $Headers, [int] $TimeoutSec)
    if ($Uri -ne 'https://api.github.com/repos/luoluoluo22/yanzi/releases/latest') {
        throw "Unexpected untrusted release metadata endpoint: $Uri"
    }
    if ($Headers['Accept'] -ne 'application/vnd.github+json') {
        throw 'Missing expected GitHub media type'
    }
    return [pscustomobject]@{
        draft = $false
        prerelease = $false
        tag_name = 'v99.0.0'
        assets = @([pscustomobject]@{
            name = 'Yanzi-win-Setup-99.0.0.exe'
            browser_download_url = 'https://github.com/luoluoluo22/yanzi/releases/download/v99.0.0/Yanzi-win-Setup-99.0.0.exe'
            digest = 'sha256:' + ('a' * 64)
            size = [long]1048576
        })
    }
}
$first = @(& $agent -Mode check)
if (@($first | Where-Object { $_ -match '^DEPLOY_AVAILABLE=v99\.0\.0$' }).Count -ne 1) {
    throw "First safe check failed: $first"
}
$second = @(& $agent -Mode check)
if (@($second | Where-Object { $_ -match '^DEPLOY_AVAILABLE=v99\.0\.0$' }).Count -ne 1) {
    throw "Second safe check failed: $second"
}
$status = (& $agent -Mode status | Out-String) | ConvertFrom-Json
if (-not $status.machine -or $status.state -ne 'available' -or $status.targetVersion -ne '99.0.0') {
    throw 'Deployment receipt status mismatch'
}
$packages = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\DeploymentAgent\packages'
if (@(Get-ChildItem -LiteralPath $packages -File -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'A check-only validation must not download or execute a package'
}
Write-Output "DEVICE_DEPLOY_TEST=PASSED; parse=3; repeatCheck=2; machine=$($status.machine); state=$($status.state); fixture=offline"
'@
try {
    $content = $template.Replace('__AGENT_SCRIPT__', $files[0].Replace("'", "''")).
        Replace('__ISOLATED_ROOT__', $isolated.Replace("'", "''"))
    [IO.File]::WriteAllText($runner, $content, (New-Object Text.UTF8Encoding($false)))
    & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $runner
    if ($LASTEXITCODE -ne 0) { throw "Isolated device deploy verification failed: $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $isolated -Recurse -Force -ErrorAction SilentlyContinue
}
