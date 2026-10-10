$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'OpenQuickHost'
$r = Join-Path $base 'McpRuntime\raccoon\app'
$y = Join-Path $base 'McpRuntime\yanzi\app'
if (!(Test-Path (Join-Path $r 'src\index.js')) -or !(Test-Path (Join-Path $y 'src\http.js'))) {
  throw 'Portable MCP runtime has not been staged yet.'
}
foreach($p in @('src\config.js','src\peer-manager.js','src\tools.js','src\http.js','src\runtime-settings.js')) {
    & node --check (Join-Path $r $p)
    if ($LASTEXITCODE -ne 0) { throw "Raccoon syntax error: $p" }
}
foreach($p in @('src\http.js','src\server.js','src\oauth.js')) {
    & node --check (Join-Path $y $p)
    if ($LASTEXITCODE -ne 0) { throw "Yanzi syntax error: $p" }
}
Push-Location $r
try {
  & node --test (Join-Path $r 'test\device-names.test.js') (Join-Path $r 'test\device-name-integration.test.js')
  if ($LASTEXITCODE -ne 0) { throw 'Raccoon device routing tests failed' }
}
finally { Pop-Location }
Push-Location $y
try {
  & node --test
  if ($LASTEXITCODE -ne 0) { throw 'Yanzi MCP test suite failed' }
}
finally { Pop-Location }
foreach($ext in @('raccoon-manager','ext_ef5c4c9e801543059e8bc05760392ce1')) {
  $extension = Join-Path $base ('Extensions\' + $ext)
  $forbidden = @('node_modules','.runtime','.raccoon-runtime','.env')
  foreach($sub in $forbidden) {
    if(Test-Path (Join-Path $extension ('service\' + $sub))) { throw "$ext contains unsyncable service data: $sub" }
  }
}
Write-Output 'PORTABLE_MCP_VERIFICATION=PASS'
