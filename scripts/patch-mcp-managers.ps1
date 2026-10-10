$ErrorActionPreference='Stop'
$root=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'
$r=Join-Path $root 'raccoon-manager'
$y=Join-Path $root 'ext_ef5c4c9e801543059e8bc05760392ce1'
function Change([string]$file,[string]$old,[string]$new) {
  $s=[IO.File]::ReadAllText($file)
  if ($s.Contains($old)) { [IO.File]::WriteAllText($file,$s.Replace($old,$new),[Text.UTF8Encoding]::new($false)) }
  elseif (!$s.Contains($new)) { throw ("Patch anchor missing: "+$file+" : "+$old) }
}
$rp=Join-Path $r 'RaccoonManager.cs'
Change $rp 'private static readonly string recoveryDirectory = @"F:\Desktop\kaifa\raccoon-mcp\.raccoon-runtime";' 'private static readonly string recoveryDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "McpRuntime", "raccoon");'
Change $rp 'WorkingDirectory=@"F:\Desktop\kaifa\raccoon-mcp"' 'WorkingDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "raccoon-manager", "service")'
Change $rp '@"F:\Desktop\kaifa\raccoon-mcp\scripts\startup.ps1"' 'Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "raccoon-manager", "managed-start.ps1")'
Change $rp 'private const string Project = @"F:\Desktop\kaifa\raccoon-mcp";' 'private static readonly string Project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "raccoon-manager", "service");'
Change $rp 'private static readonly string Runtime = Path.Combine(Project, ".raccoon-runtime");' 'private static readonly string Runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "McpRuntime", "raccoon");'
# The controller already calls extension-relative control.ps1 for explicit start/restart.
$ctrl=@'
param([ValidateSet('start','restart')][string]$Action='start')
$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot 'managed-start.ps1'
& $script -Action $Action
if($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'@
[IO.File]::WriteAllText((Join-Path $r 'control.ps1'),$ctrl,[Text.UTF8Encoding]::new($false))
# Change the active entry file, leaving the original main.cs untouched for rollback.
$source=Join-Path $y 'main.cs'
$dest=Join-Path $y 'McpManager.cs'
if (!(Test-Path $dest)) { Copy-Item $source $dest }
Change $dest 'private const string Project = @"F:\Desktop\kaifa\OpenQuickHost\tools\yanzi-mcp";' 'private static readonly string Project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "ext_ef5c4c9e801543059e8bc05760392ce1");'
Change $dest 'WorkingDirectory=Project' 'WorkingDirectory=Path.Combine(Project, "service")'
Change $dest 'Path.Combine(Project,"scripts","start-http.ps1")' 'Path.Combine(Project,"managed-start.ps1")'
Change $dest 'Path.Combine(Project,".runtime",' 'Path.Combine(Runtime,'
Change $dest 'private static readonly object Gate = new object();' 'private static readonly object Gate = new object();' # idempotent
$s=[IO.File]::ReadAllText($dest)
if (!$s.Contains('private static readonly string Runtime =')) {
 $s=$s.Replace('private static readonly string Project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "ext_ef5c4c9e801543059e8bc05760392ce1");',
 'private static readonly string Project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "ext_ef5c4c9e801543059e8bc05760392ce1");'+[Environment]::NewLine+'    private static readonly string Runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "McpRuntime", "yanzi");')
}
$s=$s.Replace('if(!await CheckGate.WaitAsync(0))return;','if(!await CheckGate.WaitAsync(0))return;'+[Environment]::NewLine+'        Directory.CreateDirectory(Runtime);')
[IO.File]::WriteAllText($dest,$s,[Text.UTF8Encoding]::new($false))
foreach($pair in @(@($r,'0.3.0', $null), @($y,'0.2.0','McpManager.cs'))) {
  $m=Join-Path $pair[0] 'manifest.json'
  $j=Get-Content $m -Raw -Encoding UTF8 | ConvertFrom-Json
  $j.version=$pair[1]
  if($pair[2]) { $j.entry=$pair[2] }
  [IO.File]::WriteAllText($m,($j|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
}
Write-Output 'MCP_MANAGERS_UPDATED=OK'
