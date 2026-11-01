#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cloud = Get-Content -LiteralPath (Join-Path $root 'src\OpenQuickHost\MainWindow.CloudSync.cs') -Raw -Encoding UTF8
$hostWindow = Get-Content -LiteralPath (Join-Path $root 'src\OpenQuickHost\MainWindow.xaml.cs') -Raw -Encoding UTF8

function Require-Text([string]$text, [string]$needle, [string]$why) {
    if (-not $text.Contains($needle)) { throw "FAILED: $why" }
    Write-Output "PASS: $why"
}

$start = $cloud.IndexOf('private void ExtensionContentWatcher_Changed(')
$end = $cloud.IndexOf('private static HashSet<string> LoadPersonalSyncDeletedExtensionIds(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Watcher handler boundaries missing' }
$handler = $cloud.Substring($start, $end - $start)
Require-Text $handler 'lock (_extensionFileChangeGate)' 'Watch events are batched before Dispatcher'
Require-Text $handler '_pendingExtensionFileIds.Add(segments[0])' 'Dirty extension IDs are coalesced'
Require-Text $handler '_pendingExtensionCatalogChange |= catalogChanged' 'Catalog changes survive coalescing'
Require-Text $handler 'if (_extensionFileFlushPosted)' 'One pending UI callback per burst'
Require-Text $cloud 'Interval = TimeSpan.FromMilliseconds(400)' 'Bounded flush interval'
Require-Text $cloud '_extensionFileFlushPosted = false;' 'Posted gate reset at flush'
Require-Text $cloud 'QueueBackgroundWebDavSync("extension-file-change");' 'Personal sync is still scheduled'
Require-Text $cloud 'SyncLocalExtensionsToCloud();' 'Account sync is still scheduled'
Require-Text $hostWindow '_extensionFileChangeFlushTimer.Stop();' 'Flush timer stops when window closes'
Require-Text $cloud 'arg.Equals("--watcher-qa"' 'Dev-only real watcher stress entrypoint'
if ($handler.Contains('QueueBackgroundWebDavSync(') -or $handler.Contains('SyncLocalExtensionsToCloud(')) {
    throw 'FAILED: per-event direct sync returned to watcher handler'
}
Write-Output 'PASS: per-event handler does not call cloud sync directly'
Write-Output 'WATCHER_BATCH_REGRESSION_PASS'
