[CmdletBinding()]
param(
    [string]$Description='',
    [string]$ScreenshotNote='',
    [string]$Log='',
    [switch]$Json
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python=Get-Command python -ErrorAction SilentlyContinue
if(-not $python){throw 'Python 3 required for developer diagnosis'}
$argsList=@((Join-Path $root 'tools\dev-navigator\diagnose.py'),'--repo-root',$root)
if($Description){$argsList+=@('--description',$Description)}
if($ScreenshotNote){$argsList+=@('--screenshot-note',$ScreenshotNote)}
if($Log){$argsList+=@('--log',$Log)}
if($Json){$argsList+='--json'}
& $python.Source @argsList
exit $LASTEXITCODE
