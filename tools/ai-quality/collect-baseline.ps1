# Read-only, privacy-preserving metadata collector for the isolated AI-quality loop.
# Does not send requests, read credentials, or output chat content.
param(
    [Parameter(Mandatory = $true)][string]$TopicsPath,
    [Parameter(Mandatory = $true)][string]$ShellLogPath
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $TopicsPath -PathType Leaf)) { throw 'topics file unavailable' }
if (-not (Test-Path -LiteralPath $ShellLogPath -PathType Leaf)) { throw 'shell log unavailable' }

$data = Get-Content -LiteralPath $TopicsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$topics = @()
if ($null -ne $data.topics) { $topics = @($data.topics) }
$pairs = New-Object System.Collections.ArrayList
$topicIndex = 0
$userMessageCount = 0
$assistantMessageCount = 0
foreach ($topic in $topics) {
    $topicIndex++
    $pendingUserLength = $null
    $pairIndex = 0
    foreach ($message in @($topic.messages)) {
        if ($null -eq $message) { continue }
        if ($message.isUser -eq $true) {
            $userMessageCount++
            $pendingUserLength = ([string]$message.text).Length
            continue
        }
        $assistantMessageCount++
        if ($null -ne $pendingUserLength) {
            $pairIndex++
            $length = ([string]$message.text).Length
            [void]$pairs.Add([pscustomobject]@{
                sampleId = ('topic' + $topicIndex + '_pair' + $pairIndex)
                userChars = $pendingUserLength
                assistantChars = $length
                assistantUnder80Chars = ($length -lt 80)
            })
            $pendingUserLength = $null
        }
    }
}

$events = New-Object System.Collections.ArrayList
foreach ($line in [System.IO.File]::ReadLines($ShellLogPath)) {
    $kind = $null
    if ($line.Contains('AI request start:')) { $kind = 'request-start' }
    elseif ($line.Contains('AI response success:')) { $kind = 'response-success' }
    elseif ($line.Contains('AI request failed:')) { $kind = 'request-failed' }
    elseif ($line.Contains('AI response empty:')) { $kind = 'response-empty' }
    if ($null -eq $kind) { continue }
    $stampMatch = [regex]::Match($line, '\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]')
    $stamp = if ($stampMatch.Success) { $stampMatch.Groups[1].Value } else { 'unknown' }
    $messageCountMatch = [regex]::Match($line, '(?<!\w)messages=(\d+)')
    $lengthMatch = [regex]::Match($line, '(?<!\w)contentLength=(\d+)')
    $event = [ordered]@{ localTimestamp = $stamp; kind = $kind }
    if ($kind -eq 'request-start' -and $messageCountMatch.Success) {
        $event['requestMessageCount'] = [int]$messageCountMatch.Groups[1].Value
    }
    if ($kind -eq 'response-success' -and $lengthMatch.Success) {
        $event['assistantChars'] = [int]$lengthMatch.Groups[1].Value
    }
    if ($kind -eq 'request-failed') {
        $failureCategory = 'other'
        if ($line.Contains(([string][char]0x672A + [char]0x914D + [char]0x7F6E))) { $failureCategory = 'not-configured' }
        elseif ($line.Contains('ResponseEnded') -or $line.Contains('prematurely')) { $failureCategory = 'response-ended-prematurely' }
        $event['failureCategory'] = $failureCategory
    }
    [void]$events.Add([pscustomobject]$event)
}
$successes = @($events | Where-Object { $_.kind -eq 'response-success' })
$starts = @($events | Where-Object { $_.kind -eq 'request-start' })
$failures = @($events | Where-Object { $_.kind -eq 'request-failed' })
$shortPairs = @($pairs | Where-Object { $_.assistantUnder80Chars }).Count

# Explicit allowlist: no prompts, messages, titles, provider settings,
# URLs, HTTP bodies, tokens, keys or file paths in serialized output.
[pscustomobject]@{
    schemaVersion = 1
    provenance = 'local AI topic metadata + shell log event metadata'
    topicsLastWriteLocal = (Get-Item -LiteralPath $TopicsPath).LastWriteTime.ToString('yyyy-MM-ddTHH:mm:sszzz')
    counts = [ordered]@{
        topics = $topics.Count
        userMessages = $userMessageCount
        assistantMessages = $assistantMessageCount
        comparableSavedPairs = $pairs.Count
        savedPairsUnder80Chars = $shortPairs
        loggedRequestStarts = $starts.Count
        loggedResponseSuccesses = $successes.Count
        loggedRequestFailures = $failures.Count
    }
    savedPairSamples = @($pairs | Select-Object -First 20)
    loggedEvents = @($events | Select-Object -Last 30)
} | ConvertTo-Json -Depth 8 -Compress
