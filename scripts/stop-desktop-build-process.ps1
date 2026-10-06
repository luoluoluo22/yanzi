param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [int]$GracefulTimeoutMs = 15000,
    [string]$PipeName = "Yanzi.OpenQuickHost.Dev.Shell.Pipe"
)

$ErrorActionPreference = "Stop"
$resolvedExecutable = [IO.Path]::GetFullPath($ExecutablePath)

function Get-TargetProcesses {
    @(
        Get-CimInstance Win32_Process -Filter "Name = 'Yanzi.exe'" |
            Where-Object {
                $_.ExecutablePath -and
                [IO.Path]::GetFullPath($_.ExecutablePath).Equals($resolvedExecutable, [StringComparison]::OrdinalIgnoreCase)
            }
    )
}

function Send-GracefulShutdownRequest {
    $client = $null
    $writer = $null
    try {
        $client = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            $PipeName,
            [System.IO.Pipes.PipeDirection]::Out,
            [System.IO.Pipes.PipeOptions]::Asynchronous)

        # Startup can take several seconds. A short connect timeout keeps this
        # loop responsive while we wait for the primary instance to expose IPC.
        $client.Connect(250)

        $writer = [System.IO.StreamWriter]::new($client)
        $writer.AutoFlush = $true
        $writer.Write("__shutdown__")
        $writer.Flush()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($writer) {
            $writer.Dispose()
        }
        elseif ($client) {
            $client.Dispose()
        }
    }
}

$targets = Get-TargetProcesses
if ($targets.Count -eq 0) {
    return
}

$targetIds = @($targets | ForEach-Object { $_.ProcessId })
$deadline = [DateTime]::UtcNow.AddMilliseconds([Math]::Max(0, $GracefulTimeoutMs))
$requestSent = $false

do {
    $remaining = @(
        Get-TargetProcesses |
            Where-Object { $targetIds -contains $_.ProcessId }
    )

    if ($remaining.Count -eq 0) {
        Write-Host "Yanzi development process exited gracefully."
        return
    }

    if (-not $requestSent) {
        $requestSent = Send-GracefulShutdownRequest
        if ($requestSent) {
            Write-Host "Graceful Yanzi shutdown requested through the single-instance pipe."
        }
    }

    Start-Sleep -Milliseconds 100
}
while ([DateTime]::UtcNow -lt $deadline)

# Last-resort fallback for a hung or older build. Forced termination skips
# NotifyIcon.Dispose and can leave an Explorer tray ghost, so this path should
# only execute when graceful IPC shutdown is unavailable or stuck.
foreach ($process in $remaining) {
    Write-Warning "Yanzi PID $($process.ProcessId) did not exit gracefully; forcing termination."
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}
