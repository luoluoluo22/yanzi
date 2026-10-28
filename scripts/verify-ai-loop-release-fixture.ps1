$ErrorActionPreference='Stop'
$path='F:\Desktop\kaifa\OpenQuickHost\.artifacts\ai-loop-release-gate-fixture'
$m=Get-Content (Join-Path $path 'manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($m.id -ne 'ai-loop-release-gate-fixture' -or $m.version -ne '0.0.3'){throw 'Invalid test fixture'}
Write-Output 'AI_LOOP_RELEASE_PREFLIGHT_PASS'

