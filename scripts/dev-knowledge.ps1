[CmdletBinding()]
param(
    [ValidateSet('coverage','qa')][string]$Mode='coverage',
    [string[]]$Features=@(),
    [string]$Query='',
    [ValidateRange(0,100)][int]$Limit=25,
    [switch]$Json
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python=Get-Command python -ErrorAction SilentlyContinue
if(-not $python){throw 'Python 3 required for developer knowledge'}
$argsList=@((Join-Path $root 'tools\dev-navigator\knowledge_cli.py'),$Mode,'--repo-root',$root,'--limit',"$Limit")
foreach($f in $Features){$argsList+=@('--feature',$f)}
if($Query){$argsList+=@('--query',$Query)}
if($Json){$argsList+='--json'}
& $python.Source @argsList
exit $LASTEXITCODE
