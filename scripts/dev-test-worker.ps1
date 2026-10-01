function Stop-YanziLocalWorkerPort {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port
    )

    function Is-YanziWorkerProcess($process) {
        if (-not $process) { return $false }
        $commandLine = [string]$process.CommandLine
        return $commandLine -match "wrangler.*\bdev\b" -and
               $commandLine -match "OpenQuickHost[\\/]cloudflare[\\/]wrangler\.toml"
    }

    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)" -ErrorAction SilentlyContinue
        if (-not $owner) { continue }

        $candidate = $owner
        if ($owner.Name -eq "workerd.exe" -and $owner.ParentProcessId) {
            $candidate = Get-CimInstance Win32_Process -Filter "ProcessId=$($owner.ParentProcessId)" -ErrorAction SilentlyContinue
        }

        if (-not (Is-YanziWorkerProcess $candidate)) {
            throw "Port $Port is occupied by an unrelated process: $($owner.Name) PID=$($owner.ProcessId)"
        }

        $root = $candidate
        for ($depth = 0; $depth -lt 8; $depth++) {
            if (-not $root.ParentProcessId) { break }
            $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($root.ParentProcessId)" -ErrorAction SilentlyContinue
            if (-not (Is-YanziWorkerProcess $parent)) { break }
            $root = $parent
        }

        cmd.exe /d /c "taskkill /PID $($root.ProcessId) /T /F >nul 2>nul"
    }

    $deadline = (Get-Date).AddSeconds(8)
    do {
        $remaining = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
        if ($remaining.Count -eq 0) { return }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "Local Yanzi Worker did not release port $Port."
}
