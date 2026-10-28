param(
    [Parameter(Mandatory = $true)][string]$EntryPath,
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
# The existing C# miniapp runtime compiles main.cs only.
# Embed this reusable adapter during development; track its SDK copy as the source of truth.
$source = Join-Path $PSScriptRoot '..\..\sdk\miniapps\HomeAssistantStateChangedStream.cs'
$entry = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EntryPath)
if (-not (Test-Path -LiteralPath $source)) { throw "Cannot find common HA event source: $source" }
if (-not (Test-Path -LiteralPath $entry)) { throw "Cannot find miniapp entry: $entry" }
$shared = [IO.File]::ReadAllText($source).Replace("`r`n", "`n").TrimEnd("`n")
$original = [IO.File]::ReadAllText($entry)
$current = $original.Replace("`r`n", "`n")
$begin = '// <YANZI_HA_EVENT_STREAM>'
$end = '// </YANZI_HA_EVENT_STREAM>'
$render = $begin + "`n" + $shared + "`n" + $end
$hasBegin = $current.Contains($begin)
$hasEnd = $current.Contains($end)
if ($hasBegin -ne $hasEnd) { throw "Unmatched HA stream block markers in $entry" }
if ($hasBegin) {
    $pattern = [regex]::Escape($begin) + '[\s\S]*?' + [regex]::Escape($end)
    if ([regex]::Matches($current, $pattern).Count -ne 1) { throw "Expected one shared stream block" }
    $current = [regex]::Replace($current, $pattern,
        [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $render })
} else {
    $current = $current.TrimEnd("`n") + "`n`n" + $render + "`n"
}
foreach ($namespace in @('System.Net.WebSockets', 'System.Text.Json')) {
    if ($current -notmatch ('(?m)^using\s+' + [regex]::Escape($namespace) + '\s*;')) {
        $current = "using $namespace;`n" + $current
    }
}
$before = $original.Replace("`r`n", "`n")
if ($Check) {
    if ($before -cne $current) { throw "Shared HA event adapter is out of date: $entry" }
    Write-Output "HA_SHARED_SOURCE_CHECK=PASS"
    return
}
if ($before -cne $current) {
    [IO.File]::WriteAllText($entry, $current, [Text.UTF8Encoding]::new($true))
    Write-Output "HA_SHARED_SOURCE_UPDATED=$entry"
} else { Write-Output "HA_SHARED_SOURCE_UNCHANGED=$entry" }
