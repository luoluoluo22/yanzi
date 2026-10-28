$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$extensions = Join-Path $root 'Extensions'
$data = Join-Path $root 'McpRuntime'
$rac = Join-Path $extensions 'raccoon-manager'
$yan = Join-Path $extensions 'ext_ef5c4c9e801543059e8bc05760392ce1'
$rRuntime = Join-Path $data 'raccoon'
$yRuntime = Join-Path $data 'yanzi'
foreach ($p in @($rRuntime,$yRuntime)) { New-Item -Path $p -ItemType Directory -Force | Out-Null }
$legacyR = 'F:\Desktop\kaifa\raccoon-mcp'
$legacyY = 'F:\Desktop\kaifa\OpenQuickHost\tools\yanzi-mcp'
if (-not (Test-Path (Join-Path $rRuntime '.env')) -and (Test-Path (Join-Path $legacyR '.env'))) {
  Copy-Item (Join-Path $legacyR '.env') (Join-Path $rRuntime '.env')
}
if (Test-Path (Join-Path $legacyR '.raccoon-runtime')) {
  foreach ($f in @('settings.json','oauth-state.json','secrets.json','cloudflare-tunnel-token.txt','tunnel.json','openai-runtime-key.txt','oauth-owner-password.txt')) {
    $s = Join-Path $legacyR ('.raccoon-runtime\' + $f)
    $d = Join-Path $rRuntime $f
    if ((Test-Path $s) -and !(Test-Path $d)) { Copy-Item -LiteralPath $s -Destination $d }
  }
  $t = Join-Path $legacyR '.raccoon-runtime\tools\tunnel-client'
  if ((Test-Path $t) -and !(Test-Path (Join-Path $rRuntime 'tools\tunnel-client'))) {
    New-Item (Join-Path $rRuntime 'tools') -ItemType Directory -Force | Out-Null
    Copy-Item $t (Join-Path $rRuntime 'tools') -Recurse -Force
  }
}
if (Test-Path (Join-Path $legacyY '.runtime')) {
  foreach ($f in @('oauth-owner-password.txt','oauth-state.json')) {
    $s = Join-Path $legacyY ('.runtime\' + $f)
    $d = Join-Path $yRuntime $f
    if ((Test-Path $s) -and !(Test-Path $d)) { Copy-Item -LiteralPath $s -Destination $d }
  }
}
function Replace-Exact([string]$p,[string]$old,[string]$new) {
  $s=[IO.File]::ReadAllText($p)
  if ($s.Contains($old)) {
    [IO.File]::WriteAllText($p,$s.Replace($old,$new),[Text.UTF8Encoding]::new($false))
  } elseif (!$s.Contains($new)) { throw ("Missing patch anchor: "+$p+" : "+$old) }
}
$service=Join-Path $rac 'service'
Replace-Exact (Join-Path $service 'src\config.js') 'process.loadEnvFile();' "process.loadEnvFile(process.env.RACCOON_ENV_FILE || '.env');"
Replace-Exact (Join-Path $service 'src\runtime-settings.js') 'path.join(projectDir, ".raccoon-runtime")' 'process.env.RACCOON_RUNTIME_DIR || path.join(projectDir, ".raccoon-runtime")'
Replace-Exact (Join-Path $service 'src\build-cache.js') 'path.join(projectDir, ".raccoon-runtime", "build-cache")' 'path.join(process.env.RACCOON_RUNTIME_DIR || path.join(projectDir, ".raccoon-runtime"), "build-cache")'
# Portable PowerShell startup scripts are reset and patched safely by fix-portable-mcp-scripts.py.
Replace-Exact (Join-Path $service 'scripts\connection-status.js') 'path.resolve(''.raccoon-runtime'')' 'path.resolve(process.env.RACCOON_RUNTIME_DIR || ''.raccoon-runtime'')'
# Keep runtime settings out of the package. Startup scripts and node inherit per-device runtime paths.
$racStart=@'
param([ValidateSet('start','restart','status')][string]$Action='start',[switch]$Recover)
$ErrorActionPreference = 'Stop'
$extension = Split-Path -Parent $MyInvocation.MyCommand.Path
$service = Join-Path $extension 'service'
$runtime = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\McpRuntime\raccoon'
New-Item -Path $runtime -ItemType Directory -Force | Out-Null
$env:RACCOON_RUNTIME_DIR = $runtime
$env:RACCOON_ENV_FILE = Join-Path $runtime '.env'
$env:RACCOON_SETTINGS_FILE = Join-Path $runtime 'settings.json'
$env:RACCOON_SECRET_FILE = Join-Path $runtime 'secrets.json'
$env:RACCOON_OAUTH_STATE_FILE = Join-Path $runtime 'oauth-state.json'
$env:RACCOON_SOURCE_BACKUP_DIR = Join-Path $runtime 'source-backups'
$env:RACCOON_PIPELINE_DIR = Join-Path $runtime 'pipelines'
if (!(Test-Path $env:RACCOON_ENV_FILE)) {
    $secret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    @("RACCOON_ROOT=$env:USERPROFILE",'RACCOON_TRANSPORT=http','RACCOON_HOST=127.0.0.1','RACCOON_PORT=3766',"RACCOON_TOKEN=$secret",'RACCOON_ENABLE_SHELL=0','RACCOON_READ_ONLY=0',"RACCOON_AUDIT_LOG=$runtime\audit.jsonl") |
        Set-Content $env:RACCOON_ENV_FILE -Encoding UTF8
}
if (!(Test-Path (Join-Path $service 'node_modules\@modelcontextprotocol\sdk'))) {
    $npm = (Get-Command npm.cmd -ErrorAction Stop).Source
    Push-Location $service
    try { & $npm ci --omit=dev --no-audit --no-fund --silent; if($LASTEXITCODE -ne 0){throw 'Raccoon dependencies install failed'} }
    finally { Pop-Location }
}
if ($Action -eq 'status') { & (Join-Path $service 'scripts\local.ps1') status; exit $LASTEXITCODE }
if ($Action -eq 'restart') { & (Join-Path $service 'scripts\local.ps1') stop }
# Respect an already-running original service until a maintenance cutover.
try {
  $h = Invoke-RestMethod -Uri 'http://127.0.0.1:3766/health' -TimeoutSec 2
  if ($h.ok) { Write-Output 'Existing Raccoon service healthy; avoiding duplicate port binding.'; exit 0 }
} catch {}
& (Join-Path $service 'scripts\startup.ps1') -Recover:$Recover
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
[IO.File]::WriteAllText((Join-Path $rac 'managed-start.ps1'),$racStart,[Text.UTF8Encoding]::new($false))
$yService=Join-Path $yan 'service'
$yHttp=Join-Path $yService 'src\http.js'
Replace-Exact $yHttp "const runtime=new URL('../.runtime/',import.meta.url);" "const runtime=process.env.YANZI_MCP_RUNTIME_DIR || fileURLToPath(new URL('../.runtime/',import.meta.url));"
Replace-Exact $yHttp "fs.readFileSync(new URL('oauth-owner-password.txt',runtime),'utf8').trim()" "fs.readFileSync(path.join(runtime,'oauth-owner-password.txt'),'utf8').trim()"
Replace-Exact $yHttp "fileURLToPath(new URL('oauth-state.json',runtime))" "path.join(runtime,'oauth-state.json')"
$startHttp=Join-Path $yService 'scripts\start-http.ps1'
$yanStart=@'
param()
$ErrorActionPreference='Stop'
$extension=Split-Path -Parent $MyInvocation.MyCommand.Path
$service=Join-Path $extension 'service'
$runtime=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\McpRuntime\yanzi'
New-Item $runtime -ItemType Directory -Force | Out-Null
$env:YANZI_MCP_RUNTIME_DIR=$runtime
$password=Join-Path $runtime 'oauth-owner-password.txt'
if(!(Test-Path $password)){
  [IO.File]::WriteAllText($password,[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)))
}
if(!(Test-Path (Join-Path $service 'node_modules\@modelcontextprotocol\sdk'))){
  $npm=(Get-Command npm.cmd -ErrorAction Stop).Source
  Push-Location $service
  try { & $npm ci --omit=dev --no-audit --no-fund --silent; if($LASTEXITCODE -ne 0){throw 'Yanzi MCP dependencies install failed'} }
  finally { Pop-Location }
}
& (Join-Path $service 'scripts\start-http.ps1')
if($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
[IO.File]::WriteAllText((Join-Path $yan 'managed-start.ps1'),$yanStart,[Text.UTF8Encoding]::new($false))
& python (Join-Path $PSScriptRoot 'fix-portable-mcp-scripts.py')
if($LASTEXITCODE -ne 0){throw 'Portable startup script patch failed'}
Write-Output "PORTABLE_MCP_CONFIGURED=OK runtime=$data"
