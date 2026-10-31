[CmdletBinding()]
param(
    [string]$Query='',
    [string]$UiQuery='',
    [string[]]$Paths=@(),
    [string]$Base='',
    [string[]]$Expect=@(),
    [string[]]$Preserve=@(),
    [switch]$RunTargeted,
    [switch]$RunHiddenUi,
    [switch]$DiscoverUnmapped,
    [string]$Out='',
    [switch]$Json
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python=(Get-Command python -ErrorAction SilentlyContinue)
if(-not $python){throw 'Python 3 is required for development preflight'}
$argsList=@((Join-Path $root 'tools\dev-navigator\preflight.py'),'--repo-root',$root)
if($Query){$argsList+=@('--query',$Query)}
if($UiQuery){$argsList+=@('--ui-query',$UiQuery)}
if($Base){$argsList+=@('--base',$Base)}
foreach($x in $Paths){$argsList+=@('--path',$x)}
foreach($x in $Expect){$argsList+=@('--expect',$x)}
foreach($x in $Preserve){$argsList+=@('--preserve',$x)}
if($RunTargeted){$argsList+='--run-targeted'}
if($RunHiddenUi){$argsList+='--run-hidden-ui'}
if($DiscoverUnmapped){$argsList+='--discover-unmapped'}
if($Out){$argsList+=@('--out',$Out)}
if($Json){$argsList+='--json'}
& $python.Source @argsList
exit $LASTEXITCODE
