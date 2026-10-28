function Test-YanziPhoneChatMessage([string]$Adb, [string]$Serial, [string]$Package, [string]$Marker, [string]$ArtifactRoot, [switch]$Contains, [int]$ExpectedCount = 0) {
    if ($Package -ne 'cc.luoluoluo.yanzi.mobile.dev') { throw 'Chat verification requires the isolated Dev package.' }
    $snapshot = Join-Path $ArtifactRoot ('chat-snapshot-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $snapshot | Out-Null
    $archive = Join-Path $snapshot 'databases.tar'
    try {
        # Binary redirect avoids PowerShell text decoding.
        $command = '"' + $Adb + '" -s ' + $Serial + ' exec-out run-as ' + $Package + ' tar -cf - databases > "' + $archive + '" 2>nul'
        cmd.exe /d /c $command
        if ($LASTEXITCODE -ne 0) { return $false }
        $entries = @(tar -tf $archive | Where-Object { $_ -match '^databases/yanzi-chat-history\.db(?:-wal|-shm)?$' })
        if ($entries.Count -eq 0) { return $false }
        tar -xf $archive -C $snapshot @entries
        if ($LASTEXITCODE -ne 0) { return $false }
        $mode = if ($Contains) { 'contains' } else { 'exact' }
        $found = & node --no-warnings (Join-Path $PSScriptRoot 'node/check-phone-chat.mjs') (Join-Path $snapshot 'databases/yanzi-chat-history.db') $Marker $mode $ExpectedCount 2>$null
        return $found -eq 'true'
    } finally {
        foreach ($name in @('yanzi-chat-history.db','yanzi-chat-history.db-wal','yanzi-chat-history.db-shm')) {
            Remove-Item -LiteralPath (Join-Path $snapshot ('databases/' + $name)) -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $snapshot 'databases') -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $snapshot -ErrorAction SilentlyContinue
    }
}
