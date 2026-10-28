# Publish a tested home development commit to the independent browser-helper channel.
# Uncommitted work, non-fast-forward histories and WIP are never automatically published.
param(
    [string]$SourceRepo = 'F:\Desktop\kaifa\OpenQuickHost',
    [switch]$Publish
)
$ErrorActionPreference='Stop'
if (!(Test-Path (Join-Path $SourceRepo 'browser-extension\manifest.json'))) {
    throw 'Missing browser-extension source'
}
$dirty=@(& git -C $SourceRepo status --porcelain=v1 -- browser-extension)
if($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {throw 'Source repo has uncommitted/untracked changes'}
$sha=(& git -C $SourceRepo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $sha -notmatch '^[a-f0-9]{40}$'){throw 'Bad source commit'}
$manifest=Get-Content (Join-Path $SourceRepo 'browser-extension\manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.name -notmatch 'Yanzi Browser Helper'){throw 'Unexpected extension identity'}
$files=@(Get-ChildItem (Join-Path $SourceRepo 'browser-extension') -File -Recurse -Filter '*.js' |
    Where-Object {$_.FullName -notmatch '\\scratch\\'})
foreach($file in $files){$null=& node --check $file.FullName 2>&1; if($LASTEXITCODE -ne 0){throw "Syntax error: $($file.FullName)"}}
$existingLine=@(& git -C $SourceRepo ls-remote --heads origin browser-helper-channel)
if($LASTEXITCODE -ne 0){throw 'Cannot inspect remote channel'}
$existing=if($existingLine.Count){($existingLine[0] -split '\s+')[0]}else{''}
if($existing -eq $sha){Write-Output "ALREADY_PUBLISHED=$sha";exit 0}
if($existing -match '^[0-9a-f]{40}$'){
    & git -C $SourceRepo merge-base --is-ancestor $existing $sha
    if($LASTEXITCODE -ne 0){throw 'Not fast-forward: review and reconcile existing channel first'}
}
if(!$Publish){Write-Output "PLAN_ONLY=True FROM=$existing TO=$sha JS_CHECKED=$($files.Count)";exit 0}
& git -C $SourceRepo push origin ($sha + ':refs/heads/browser-helper-channel')
if($LASTEXITCODE -ne 0){throw 'Channel push failed'}
$confirmed=@(& git -C $SourceRepo ls-remote --heads origin browser-helper-channel)
if($LASTEXITCODE -ne 0 -or $confirmed.Count -ne 1 -or ($confirmed[0] -split '\s+')[0] -ne $sha){throw 'Remote channel SHA not confirmed'}
Write-Output "PUBLISHED=$sha JS_CHECKED=$($files.Count)"
