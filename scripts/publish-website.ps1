param(
    [string]$ProjectName = "openquickhost-site",
    [string]$Branch = "main",
    [string]$SitePath = ".\website",
    [string]$EnvFile = ".\.env",
    [string]$DefaultAccountId = "cc88cc0084b504db93ccd9462af37212"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Import-DotEnv {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing env file: $Path"
    }

    $lines = Get-Content -LiteralPath $Path
    $rawValueLines = New-Object System.Collections.Generic.List[string]

    $lines | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            return
        }

        $separatorIndex = $line.IndexOf("=")
        if ($separatorIndex -le 0) {
            $rawValueLines.Add($line)
            return
        }

        $name = $line.Substring(0, $separatorIndex).Trim()
        $value = $line.Substring($separatorIndex + 1).Trim()

        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        [System.Environment]::SetEnvironmentVariable($name, $value, "Process")
    }

    if ([string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN) -and $rawValueLines.Count -eq 1) {
        [System.Environment]::SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", $rawValueLines[0], "Process")
    }
}

Import-DotEnv -Path $EnvFile

if ([string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN)) {
    throw "CLOUDFLARE_API_TOKEN is not configured."
}

if ([string]::IsNullOrWhiteSpace($env:CLOUDFLARE_ACCOUNT_ID) -and -not [string]::IsNullOrWhiteSpace($DefaultAccountId)) {
    [System.Environment]::SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", $DefaultAccountId, "Process")
}

if (-not (Test-Path -LiteralPath $SitePath)) {
    throw "Missing website directory: $SitePath"
}

Write-Host "Deploying Cloudflare Pages project '$ProjectName' from '$SitePath'..." -ForegroundColor Cyan
wrangler pages deploy $SitePath --project-name $ProjectName --branch $Branch
