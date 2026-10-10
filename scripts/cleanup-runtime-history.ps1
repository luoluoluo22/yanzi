param(
    [switch]$Delete,
    [int]$GraceMinutes = 90,
    [int]$RecentPerGroup = 2,
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'YanziRuntime'),
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "Runtime root missing: $Root" }
if ($Root -notlike '*\YanziRuntime') { throw "Unexpected Runtime root. Refusing cleanup: $Root" }
if ($GraceMinutes -lt 0 -or $RecentPerGroup -lt 1) { throw 'Invalid retention arguments.' }
$now = Get-Date
$cutoff = $now.AddMinutes(-$GraceMinutes)
$groups = @('versions', 'shells', 'bundled')
$entries = New-Object 'System.Collections.Generic.List[object]'
foreach ($group in $groups) {
    $folder = Join-Path $Root $group
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
    foreach ($item in @(Get-ChildItem -LiteralPath $folder -Directory -Force)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        $entries.Add([pscustomobject]@{
            Group = $group
            Name = $item.Name
            Path = $item.FullName
            LastWriteTime = $item.LastWriteTime
            Reasons = (New-Object 'System.Collections.Generic.List[string]')
            Files = 0
            Bytes = [long]0
            Status = 'Pending'
        })
    }
}
function Protect-Path {
    param([string]$Value, [string]$Reason)
    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    foreach ($entry in $entries) {
        if ($Value.IndexOf($entry.Path, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
           -not $entry.Reasons.Contains($Reason)) {
            $entry.Reasons.Add($Reason)
        }
    }
}
# Preserve all existing rollback pointer targets, including historical .before/.deactivated pointers.
$pointers = @(Get-ChildItem -LiteralPath $Root -File -Filter 'runtime*.json' -ErrorAction SilentlyContinue)
foreach ($pointer in $pointers) {
    try {
        $obj = Get-Content -LiteralPath $pointer.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($prop in @('executable','stableShell')) {
            $v = $obj.$prop
            if ($v -is [string]) { Protect-Path $v ("pointer:" + $pointer.Name) }
        }
    } catch { Write-Warning ("Cannot parse pointer; skipping deletion to be safe: " + $pointer.FullName); throw }
}
# Actual live processes, including command lines of helpers that may hold a snapshot open.
$processes = @(Get-CimInstance Win32_Process -ErrorAction Stop)
foreach ($proc in $processes) {
    if ($proc.ProcessId -eq $PID) { continue }
    if ($proc.ExecutablePath) { Protect-Path $proc.ExecutablePath ("running:" + $proc.ProcessId) }
    if ($proc.CommandLine -and $proc.CommandLine.Contains($Root)) { Protect-Path $proc.CommandLine ("cmdline:" + $proc.ProcessId) }
}
# Persistent launch references.
foreach ($reg in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
                  'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
                  'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')) {
    if (-not (Test-Path $reg)) { continue }
    $obj = Get-ItemProperty -Path $reg -ErrorAction SilentlyContinue
    foreach ($property in $obj.PSObject.Properties) {
        if ($property.Name -like 'PS*') { continue }
        if ($property.Value -is [string]) { Protect-Path $property.Value ("registry:" + $property.Name) }
    }
}
foreach ($svc in @(Get-CimInstance Win32_Service -ErrorAction SilentlyContinue)) {
    if ($svc.PathName) { Protect-Path $svc.PathName ("service:" + $svc.Name) }
}
foreach ($task in @(Get-ScheduledTask -ErrorAction SilentlyContinue)) {
    foreach ($action in @($task.Actions)) {
        if ($null -eq $action) { continue }
        Protect-Path ($action.Execute + ' ' + $action.Arguments + ' ' + $action.WorkingDirectory) ("task:" + $task.TaskName)
    }
}
foreach ($folder in @([Environment]::GetFolderPath('Startup'),
                      [Environment]::GetFolderPath('StartMenu'),
                      [Environment]::GetFolderPath('CommonStartMenu'),
                      [Environment]::GetFolderPath('Desktop'),
                      [Environment]::GetFolderPath('CommonDesktopDirectory'))) {
    if ([string]::IsNullOrEmpty($folder) -or -not (Test-Path $folder)) { continue }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $scanRecursively = $folder.EndsWith('Start Menu', [StringComparison]::OrdinalIgnoreCase)
        $shortcutFiles = if ($scanRecursively) { @(Get-ChildItem -LiteralPath $folder -File -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue) } else { @(Get-ChildItem -LiteralPath $folder -File -Filter '*.lnk' -ErrorAction SilentlyContinue) }
        foreach ($shortcut in $shortcutFiles) {
            try {
                $link = $shell.CreateShortcut($shortcut.FullName)
                Protect-Path ($link.TargetPath + ' ' + $link.Arguments + ' ' + $link.WorkingDirectory) ("shortcut:" + $shortcut.Name)
            } catch { }
        }
    } catch { Write-Warning ("Shortcut check skipped: " + $folder) }
}
# Keep a small number of latest snapshots and a cool-down window, even if not referenced yet.
foreach ($group in $groups) {
    $newest = @($entries | Where-Object {$_.Group -eq $group} | Sort-Object LastWriteTime -Descending | Select-Object -First $RecentPerGroup)
    foreach ($entry in $newest) { $entry.Reasons.Add('retention:newest') }
}
foreach ($entry in $entries) {
    if ($entry.LastWriteTime -gt $cutoff) { $entry.Reasons.Add('retention:recent') }
}
# To avoid half-preserved deployments, keep counterpart with the same snapshot name.
foreach ($entry in @($entries | Where-Object {$_.Reasons.Count -gt 0})) {
    foreach ($paired in @($entries | Where-Object {$_.Group -ne $entry.Group -and $_.Name -eq $entry.Name})) {
        if ($paired.Reasons.Count -eq 0) { $paired.Reasons.Add('retention:paired:' + $entry.Group) }
    }
}
foreach ($entry in $entries) {
    $files = @(Get-ChildItem -LiteralPath $entry.Path -Recurse -File -Force -ErrorAction Stop)
    $entry.Files = $files.Count
    $entry.Bytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
    $entry.Status = if ($entry.Reasons.Count) { 'KEEP' } else { 'CANDIDATE' }
}
if (-not $ReportPath) {
    $reportFolder = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'noop'
    $reportFolder = Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts\runtime-cleanup'
    New-Item -ItemType Directory -Path $reportFolder -Force | Out-Null
    $ReportPath = Join-Path $reportFolder ("audit-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
}
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$reportDirectory = Split-Path $ReportPath -Parent
if (-not (Test-Path $reportDirectory)) { New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null }
$save = {
    $data = [pscustomobject]@{
        GeneratedAt = (Get-Date).ToString('o')
        RuntimeRoot = $Root
        DeleteRequested = [bool]$Delete
        ProtectedPointerFiles = @($pointers | Select-Object -ExpandProperty Name)
        Items = @($entries | ForEach-Object {
            [pscustomobject]@{
                Group=$_.Group;Name=$_.Name;Path=$_.Path;LastWriteTime=$_.LastWriteTime.ToString('o')
                Bytes=$_.Bytes;Files=$_.Files;Reasons=@($_.Reasons.ToArray());Status=$_.Status
            }
        })
    }
    $data | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
& $save
$candidates = @($entries | Where-Object {$_.Status -eq 'CANDIDATE'})
$bytes = [long](($candidates | Measure-Object -Property Bytes -Sum).Sum)
Write-Output ("AUDIT: total={0}; candidate={1}; potentialMiB={2}; report={3}" -f $entries.Count, $candidates.Count, [math]::Round($bytes / 1MB, 2), $ReportPath)
foreach ($g in $groups) {
    $subset = @($candidates | Where-Object {$_.Group -eq $g})
    Write-Output ("CANDIDATES: {0}: count={1} MiB={2}" -f $g, $subset.Count, [math]::Round((($subset | Measure-Object -Property Bytes -Sum).Sum) / 1MB, 2))
}
if ($Delete) {
    # Double-check live process paths and pointer contents after auditing; refuse if changed.
    $recentPointers = @(Get-ChildItem -LiteralPath $Root -File -Filter 'runtime*.json')
    if ((@($recentPointers | Select-Object -ExpandProperty FullName | Sort-Object) -join '|') -ne
        (@($pointers | Select-Object -ExpandProperty FullName | Sort-Object) -join '|')) { throw 'Pointers changed during audit. Aborting.' }
    $liveNow = @(Get-CimInstance Win32_Process -ErrorAction Stop)
    foreach ($candidate in $candidates) {
        $canDelete = $true
        foreach ($proc in $liveNow) {
            if (($proc.ExecutablePath -and $proc.ExecutablePath.IndexOf($candidate.Path, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
                ($proc.CommandLine -and $proc.CommandLine.IndexOf($candidate.Path, [StringComparison]::OrdinalIgnoreCase) -ge 0)) {
                $canDelete = $false
                break
            }
        }
        if (-not $canDelete) { $candidate.Status = 'SKIP_NOW_ACTIVE'; continue }
        if (-not (Test-Path -LiteralPath $candidate.Path -PathType Container)) { $candidate.Status = 'SKIP_MISSING'; continue }
        $latest = Get-Item -LiteralPath $candidate.Path
        if ($latest.LastWriteTime -ne $candidate.LastWriteTime) { $candidate.Status = 'SKIP_CHANGED'; continue }
        try {
            Remove-Item -LiteralPath $candidate.Path -Recurse -Force -ErrorAction Stop
            $candidate.Status = 'DELETED'
            Write-Output ("DELETED {0}/{1} {2} MiB" -f $candidate.Group, $candidate.Name, [math]::Round($candidate.Bytes / 1MB, 1))
        } catch {
            $candidate.Status = 'FAILED: ' + $_.Exception.Message
            Write-Warning ("Deletion failed: " + $candidate.Path + " " + $_.Exception.Message)
        }
    }
    & $save
}
$deleted = @($entries | Where-Object {$_.Status -eq 'DELETED'})
$failed = @($entries | Where-Object {$_.Status -like 'FAILED*'})
Write-Output ("RESULT: deleted={0} freedMiB={1} failed={2} kept={3} report={4}" -f $deleted.Count,
    [math]::Round((($deleted | Measure-Object -Property Bytes -Sum).Sum) / 1MB, 2), $failed.Count,
    @($entries | Where-Object {$_.Status -eq 'KEEP'}).Count, $ReportPath);
