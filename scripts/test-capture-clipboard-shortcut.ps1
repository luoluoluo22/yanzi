param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$checks = @(
  @{ Name = 'Workspace keyboard copy defers past PreviewKeyDown'; Path = 'src\Yanzi.Capture\CaptureWorkspaceWindow.cs' },
  @{ Name = 'Editor keyboard copy defers past PreviewKeyDown'; Path = 'src\Yanzi.Capture\EditorWindow.cs' }
)
foreach ($check in $checks) {
  $source = [IO.File]::ReadAllText((Join-Path $repo $check.Path))
  $keyPos = $source.IndexOf('if (key == Key.C)', [StringComparison]::Ordinal)
  if ($keyPos -lt 0) { throw "$($check.Name): Ctrl+C handler missing" }
  $endPos = $source.IndexOf('if (key == Key.Z)', $keyPos, [StringComparison]::Ordinal)
  if ($endPos -lt 0) { throw "$($check.Name): handler boundary missing" }
  $handler = $source.Substring($keyPos, $endPos - $keyPos)
  if ($handler -notmatch 'e\.Handled\s*=\s*true;[\s\S]*?if\s*\(!e\.IsRepeat\)[\s\S]*?Dispatcher\.BeginInvoke\([\s\S]*?new Action\(CopyImage\)') {
    throw "$($check.Name): copy is not deferred safely"
  }
  Write-Output "PASS: $($check.Name)"
}
$clipboard = [IO.File]::ReadAllText((Join-Path $repo 'src\Yanzi.Capture\CaptureClipboard.cs'))
if ($clipboard -notmatch 'Forms\.Clipboard\.SetDataObject\(bitmap, true, 12, 90\)') {
  throw 'Clipboard write does not include bounded retries'
}
if ($clipboard -notmatch 'sequenceAfter' -or $clipboard -notmatch 'imageFormatAvailable') {
  throw 'Clipboard success diagnostics missing'
}
Write-Output 'PASS: clipboard retries and observable post-copy diagnostics'
Write-Output 'PASS: capture shortcut source regression checks'
