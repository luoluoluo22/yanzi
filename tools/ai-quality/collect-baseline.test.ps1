$ErrorActionPreference = 'Stop'
$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('yanzi-quality-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
try {
    $topicsFile = Join-Path $directory 'topics.json'
    $logFile = Join-Path $directory 'shell.log'
    $fixture = '{"topics":[{"messages":[{"isUser":true,"text":"PRIVATE_TEST_SENTINEL_123"},{"isUser":false,"text":"HIDDEN_ASSISTANT_PAYLOAD_567"}]}]}'
    Set-Content -LiteralPath $topicsFile -Value $fixture -Encoding UTF8
    Set-Content -LiteralPath $logFile -Value @(
        '[2026-10-24 09:00:00.000] AI request start: messages=1',
        '[2026-10-24 09:00:01.000] AI response success: contentLength=28'
    ) -Encoding UTF8
    $collector = Join-Path $PSScriptRoot 'collect-baseline.ps1'
    $json = (& $collector -TopicsPath $topicsFile -ShellLogPath $logFile | Out-String)
    $value = $json | ConvertFrom-Json
    if ($value.schemaVersion -ne 1) { throw 'Unexpected schema version' }
    if ($value.counts.userMessages -ne 1 -or $value.counts.assistantMessages -ne 1) {
        throw 'Message totals are incorrect'
    }
    if ($value.counts.comparableSavedPairs -ne 1) { throw 'Pair count is incorrect' }
    if ($value.counts.loggedRequestStarts -ne 1 -or $value.counts.loggedResponseSuccesses -ne 1) {
        throw 'Event counts are incorrect'
    }
    foreach ($private in @('PRIVATE_TEST_SENTINEL_123', 'HIDDEN_ASSISTANT_PAYLOAD_567', $topicsFile, $logFile)) {
        if ($json.Contains($private)) { throw 'Sensitive input leaked into output' }
    }
    Write-Output 'AI quality collector smoke PASS'
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
}