param(
    [string]$BaseUrl = "http://127.0.0.1:53919",
    [string]$Token = $env:YANZI_AGENT_API_TOKEN,
    [string]$ExtensionId = "taskbar-calendar"
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "Set YANZI_AGENT_API_TOKEN before running this test."
}
$headers = @{ "X-Yanzi-Token" = $Token }
$root = "$BaseUrl/v1/extensions/$([Uri]::EscapeDataString($ExtensionId))"
$tempImage = Join-Path ([IO.Path]::GetTempPath()) ("yanzi-ui-smoke-" + [guid]::NewGuid().ToString("N") + ".png")

function Invoke-UiJson([string]$Method, [string]$Url, $Payload = $null) {
    if ($null -eq $Payload) {
        return Invoke-RestMethod -Uri $Url -Method $Method -Headers $headers
    }
    $json = ConvertTo-Json -InputObject $Payload -Depth 8 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $Url -Method $Method -Headers $headers -ContentType "application/json; charset=utf-8" -Body $bytes
}

try {
    $health = Invoke-RestMethod "$BaseUrl/health"
    if (-not $health.ok) { throw "Local Agent API health check failed." }
    $initial = Invoke-UiJson GET "$root/status"
    $opened = Invoke-UiJson POST "$root/ui/open" @{ launchIfNeeded = $true }
    if (-not $opened.ok -or -not $opened.window.visible) { throw "Extension window could not be opened." }
    $windows = Invoke-UiJson GET "$root/ui/windows"
    if ($windows.windows.Count -lt 1) { throw "UI window enumeration returned no windows." }

    $elements = Invoke-UiJson GET "$root/ui/elements?maxDepth=5&limit=150"
    $ids = @($elements.elements | ForEach-Object { $_.automationId })
    if ($ExtensionId -eq "taskbar-calendar") {
        if ($ids -notcontains "calendar.add" -or $ids -notcontains "calendar.search") {
            throw "Calendar automation IDs are missing."
        }
    }

    $capture = Invoke-UiJson POST "$root/ui/capture" @{ mode = "auto"; windowId = "primary" }
    if (-not $capture.ok -or $capture.capture.width -lt 1) { throw "Capture failed." }
    Invoke-WebRequest -Uri ($BaseUrl + $capture.capture.imageUrl) -Headers $headers -UseBasicParsing -OutFile $tempImage
    $bytes = [IO.File]::ReadAllBytes($tempImage)
    if ($bytes.Length -lt 100 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
        throw "Captured data is not a valid PNG."
    }

    $stepCount = 0
    $pictureCount = 1
    if ($ExtensionId -eq "taskbar-calendar") {
        $steps = @(
            @{ type = "invoke"; automationId = "calendar.add"; waitFor = "calendar.editor.title" },
            @{ type = "invoke"; automationId = "calendar.editor.cancel" }
        )
        try {
            $scenario = Invoke-UiJson POST "$root/ui/actions" @{ captureEach = $true; actions = $steps }
            if (-not $scenario.ok -or $scenario.steps.Count -ne 2 -or $scenario.captures.Count -ne 2) {
                throw "Calendar action/capture sequence returned incomplete results."
            }
            $stepCount = $scenario.steps.Count
            $pictureCount += $scenario.captures.Count
        }
        catch {
            try {
                $null = Invoke-UiJson POST "$root/ui/actions" @{
                    actions = @(@{ type = "invoke"; automationId = "calendar.editor.cancel" })
                }
            } catch {}
            throw
        }
    }

    $final = Invoke-UiJson GET "$root/status"
    if (-not $final.isRunning) { throw "Extension stopped unexpectedly." }
    Write-Host "PASS: health, window discovery/open, UI elements, authenticated PNG."
    Write-Host "PASS: $stepCount UI actions; $pictureCount screenshots; extension running=$($final.isRunning)."
    Write-Host "Original running state: $($initial.isRunning)."
}
finally {
    if (Test-Path $tempImage) { Remove-Item $tempImage -Force }
}

