# Non-destructive regression: inherited RACCOON_* must not override this mini-app's .env.
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ("yanzi-raccoon-env-isolation-" + [Guid]::NewGuid().ToString("N"))
$extension = Join-Path $root 'fixture'
$runtime = Join-Path $root 'OpenQuickHost\McpRuntime\raccoon'
$service = Join-Path $root 'fake-service'
$results = Join-Path $root 'observed.json'
$source = Join-Path $PSScriptRoot '..\extensions\raccoon-manager\managed-start.ps1'
try {
    New-Item -ItemType Directory -Force -Path $extension,$runtime,(Join-Path $service 'scripts') | Out-Null
    Copy-Item $source (Join-Path $extension 'managed-start.ps1')
    [IO.File]::WriteAllText((Join-Path $extension 'stage-service.ps1'),
        '$script:ErrorActionPreference="Stop"; Write-Output $env:YANZI_TEST_FAKE_SERVICE')
    [IO.File]::WriteAllText((Join-Path $service 'scripts\startup.ps1'), @'
param([switch]$Recover)
@{
    Port = $env:RACCOON_PORT
    Token = $env:RACCOON_TOKEN
    Shell = $env:RACCOON_ENABLE_SHELL
    Root = $env:RACCOON_ROOT
    RuntimeDir = $env:RACCOON_RUNTIME_DIR
} | ConvertTo-Json | Set-Content -LiteralPath $env:YANZI_TEST_RESULT -Encoding UTF8
'@)
    [IO.File]::WriteAllText((Join-Path $service 'scripts\local.ps1'), 'param([string]$Action)')
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    @(
        'RACCOON_PORT=' + $port
        'RACCOON_TOKEN=fixture-local-token'
        'RACCOON_ENABLE_SHELL=0'
        'RACCOON_ROOT=' + $root
        'RACCOON_HOST=127.0.0.1'
        'RACCOON_TRANSPORT=http'
    ) | Set-Content (Join-Path $runtime '.env') -Encoding UTF8
    $old = @{}
    foreach ($name in @('LOCALAPPDATA','RACCOON_PORT','RACCOON_TOKEN',
                        'RACCOON_ENABLE_SHELL','RACCOON_ROOT','YANZI_TEST_RESULT',
                        'YANZI_TEST_FAKE_SERVICE')) {
        $old[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    try {
        $env:LOCALAPPDATA = $root
        $env:RACCOON_PORT = '3766'
        $env:RACCOON_TOKEN = 'foreign-mcp-token'
        $env:RACCOON_ENABLE_SHELL = '1'
        $env:RACCOON_ROOT = 'foreign-mcp-root'
        $env:YANZI_TEST_RESULT = $results
        $env:YANZI_TEST_FAKE_SERVICE = $service
        & (Join-Path $extension 'managed-start.ps1') -Action start | Out-Null
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "Startup returned $LASTEXITCODE" }
    } finally {
        foreach ($name in $old.Keys) {
            [Environment]::SetEnvironmentVariable($name, $old[$name], 'Process')
        }
    }
    if (-not (Test-Path $results)) { throw 'Fixture startup did not run' }
    $actual = Get-Content $results -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($actual.Port -ne [string]$port) { throw 'Inherited port replaced local port' }
    if ($actual.Token -ne 'fixture-local-token') { throw 'Inherited token replaced local token' }
    if ($actual.Shell -ne '0') { throw 'Inherited shell permission escaped isolation' }
    if ($actual.Root -ne $root) { throw 'Inherited root escaped isolation' }
    if ($actual.RuntimeDir -ne $runtime) { throw 'Runtime path did not stay device local' }
    Write-Output 'RACCOON_ENV_ISOLATION_PASS: device-local port/token/root and disabled shell verified'
} finally {
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
}
