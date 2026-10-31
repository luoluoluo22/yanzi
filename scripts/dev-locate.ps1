param(
    [ValidateSet('index','find','ui','impact')][string]$Mode='find',
    [string]$Query='',
    [string[]]$Paths=@(),
    [string]$Base='',
    [switch]$Json,
    [switch]$Force
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$navigator=Join-Path $root 'tools\dev-navigator\navigator.py'
$python=Get-Command python -ErrorAction SilentlyContinue
if(-not $python){throw 'Python 3 is required for the developer navigator'}
$argsList=@($navigator,$Mode,'--repo-root',$root)
if($Query){$argsList+=@('--query',$Query)}
if($Base){$argsList+=@('--base',$Base)}
foreach($path in $Paths){$argsList+=@('--path',$path)}
if($Json){$argsList+='--json'}
if($Force){$argsList+='--force'}
& $python.Source @argsList
exit $LASTEXITCODE
