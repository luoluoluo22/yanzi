param()
$ErrorActionPreference='Stop'
$extension=$PSScriptRoot
$id=Split-Path $extension -Leaf
$key=if($id -eq 'raccoon-manager'){'raccoon'}else{'yanzi'}
$source=Join-Path $extension 'service'
if (!(Test-Path -LiteralPath (Join-Path $source 'package-lock.json'))) { throw 'Raccoon service package is missing; reinstall the extension.' }
$nodeExe = & (Join-Path $extension 'ensure-node.ps1')
if (!$nodeExe -or !(Test-Path -LiteralPath $nodeExe)) { throw 'Node bootstrap did not return a valid executable.' }
$env:Path = (Split-Path -Parent $nodeExe) + ';' + $env:Path
$runtime=Join-Path $env:LOCALAPPDATA ("OpenQuickHost\McpRuntime\"+$key)
$target=Join-Path $runtime 'app'
New-Item -Path $target -ItemType Directory -Force | Out-Null
foreach($entry in @('src','scripts','assets','test','pipelines','package.json','package-lock.json','README.md')) {
  $from=Join-Path $source $entry
  if(Test-Path $from){Copy-Item -LiteralPath $from -Destination $target -Recurse -Force}
}
$lock=Join-Path $source 'package-lock.json'
$hash=(Get-FileHash $lock -Algorithm SHA256).Hash
$stamp=Join-Path $runtime 'dependencies.sha256'
if(!(Test-Path (Join-Path $target 'node_modules\@modelcontextprotocol\sdk')) -or !(Test-Path $stamp) -or (Get-Content $stamp -Raw).Trim() -ne $hash){
  $npm=(Get-Command npm.cmd -ErrorAction Stop).Source
  Push-Location $target
  try{& $npm ci --omit=dev --no-audit --no-fund --silent
    if($LASTEXITCODE -ne 0){throw ("Dependency install failed: "+$id)}
    [IO.File]::WriteAllText($stamp,$hash,[Text.UTF8Encoding]::new($false))
  }finally{Pop-Location}
}
Write-Output $target
