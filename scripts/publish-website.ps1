param(
    [string]$ProjectName = "openquickhost-site",
    [string]$Branch = "main",
    [string]$SitePath = ".\website",
    [string]$EnvFile = ".\.env",
    [string]$DefaultAccountId = "cc88cc0084b504db93ccd9462af37212",
    [string]$InstallerDir = ".\.artifacts\installer",
    [string]$ApiBase = "https://openquickhost-sync.a1137583371.workers.dev",
    [switch]$SkipVersionUpdate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Import-DotEnv {
    param([Parameter(Mandatory)]  [string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { throw "Missing env file: $Path" }

    $lines = Get-Content -LiteralPath $Path
    $rawValueLines = New-Object System.Collections.Generic.List[string]

    foreach ($raw in $lines) {
        $line = $raw.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) { continue }

        $sep = $line.IndexOf("=")
        if ($sep -le 0) { $rawValueLines.Add($line); continue }

        $name  = $line.Substring(0, $sep).Trim()
        $value = $line.Substring($sep + 1).Trim()

        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        [Environment]::SetEnvironmentVariable($name, $value, "Process")
    }

    if ([string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN) -and $rawValueLines.Count -eq 1) {
        [Environment]::SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", $rawValueLines[0], "Process")
    }
}

function Get-LatestInstallerVersion {
    param([Parameter(Mandatory)]  [string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory)) {
        Write-Host "  Installer directory not found: $Directory" -ForegroundColor Yellow
        return $null
    }

    $installers = Get-ChildItem -LiteralPath $Directory -Filter "YanziSetup-*.exe" | ForEach-Object {
        if ($_.Name -match 'YanziSetup-(\d+\.\d+\.\d+)\.exe') {
            [PSCustomObject]@{ File = $_; Version = [version]$Matches[1] }
        }
    } | Sort-Object -Property Version -Descending

    if (-not $installers -or $installers.Count -eq 0) {
        Write-Host "  No installer files found in $Directory" -ForegroundColor Yellow
        return $null
    }

    $latest = $installers[0]
    return @{ Version = $latest.Version.ToString(); FileName = $latest.File.Name; FilePath = $latest.File.FullName }
}

function Update-AppVersion {
    param(
        [Parameter(Mandatory)]  [string]$ApiBaseUrl,
        [Parameter(Mandatory)]  [string]$Token,
        [Parameter(Mandatory)]  [hashtable]$VersionInfo
    )

    $ver = $VersionInfo.Version
    $fn  = $VersionInfo.FileName
    $dlUrl = 'https://wwbnh.lanzout.com/b0pnkaj6j'
    $pubAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

    $bodyObj = [ordered]@{
        version       = $ver
        title         = 'Yanzi Launcher for Windows'
        notes         = "Version $ver released."
        download_url  = $dlUrl
        file_name     = $fn
        download_code = '62yn'
        provider      = 'lanzou'
        published_at  = $pubAt
    }
    $bodyJson = $bodyObj | ConvertTo-Json -Compress

    $hdrs = @{
        'Content-Type'  = 'application/json'
        'Authorization' = "Bearer $Token"
    }

    try {
        $resp = Invoke-RestMethod -Uri "$ApiBaseUrl/v1/admin/app/update/latest" -Method Put -Headers $hdrs -Body $bodyJson
        if ($resp.ok -or $resp.version) {
            Write-Host "  Version updated to $ver (file: $fn)" -ForegroundColor Green
            return $true
        } else {
            Write-Host "  Unexpected response: $($resp | ConvertTo-Json -Compress)" -ForegroundColor Yellow
            return $false
        }
    } catch {
        Write-Host "  Failed to update version: $_" -ForegroundColor Red
        return $false
    }
}

# ─── Main ───────────────────────────────────────────────────

Import-DotEnv -Path $EnvFile

if ([string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN)) {
    throw "CLOUDFLARE_API_TOKEN is not configured."
}

if ([string]::IsNullOrWhiteSpace($env:CLOUDFLARE_ACCOUNT_ID) -and $DefaultAccountId) {
    [Environment]::SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", $DefaultAccountId, "Process")
}

if (-not (Test-Path -LiteralPath $SitePath)) {
    throw "Missing website directory: $SitePath"
}

# Step 1: Version sync
if (-not $SkipVersionUpdate) {
    Write-Host ""
    Write-Host "[1/2] Checking latest installer version..." -ForegroundColor Cyan
    $vInfo = Get-LatestInstallerVersion -Directory $InstallerDir

    if ($vInfo) {
        Write-Host "  Latest installer: $($vInfo.FileName) (v$($vInfo.Version))"
        $authToken = $env:YANZI_AUTH_TOKEN
        if ([string]::IsNullOrWhiteSpace($authToken)) {
            Write-Host "  YANZI_AUTH_TOKEN not set in .env - skipping version API update." -ForegroundColor Yellow
            Write-Host "  Add YANZI_AUTH_TOKEN=<token> to .env to enable auto version sync." -ForegroundColor Yellow
        } else {
            Update-AppVersion -ApiBaseUrl $ApiBase -Token $authToken -VersionInfo $vInfo
        }
    } else {
        Write-Host "  No installers found, skipping version update." -ForegroundColor Yellow
    }
} else {
    Write-Host ""
    Write-Host "[1/2] Skipping version update." -ForegroundColor Yellow
}

# Step 2: Deploy
Write-Host ""
Write-Host "[2/2] Deploying Cloudflare Pages project '$ProjectName' from '$SitePath'..." -ForegroundColor Cyan
wrangler pages deploy $SitePath --project-name $ProjectName --branch $Branch
