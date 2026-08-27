param (
    [string]$AccountId = "cc88cc0084b504db93ccd9462af37212",
    [string]$ScriptName = "yanzi-sync"
)

$ErrorActionPreference = "Continue"
$rootDir = Resolve-Path (Join-Path $PSScriptRoot "..")

function Parse-EnvFile ($filePath) {
    $envHash = @{}
    if (Test-Path $filePath) {
        Get-Content $filePath | ForEach-Object {
            $line = $_.Trim()
            if ($line -and -not $line.StartsWith("#") -and $line.Contains("=")) {
                $parts = $line.Split("=", 2)
                $key = $parts[0].Trim()
                $val = $parts[1].Trim().Trim('"').Trim("'")
                $envHash[$key] = $val
            }
        }
    }
    return $envHash
}

$rootEnv = Parse-EnvFile (Join-Path $rootDir ".env")
$devVars = Parse-EnvFile (Join-Path $rootDir "cloudflare\.dev.vars")

$token = if ($rootEnv.ContainsKey("CLOUDFLARE_API_TOKEN")) { $rootEnv["CLOUDFLARE_API_TOKEN"] } else { $devVars["CLOUDFLARE_API_TOKEN"] }
$resendKey = if ($rootEnv.ContainsKey("RESEND_API_KEY")) { $rootEnv["RESEND_API_KEY"] } else { $devVars["RESEND_API_KEY"] }
$resendFrom = if ($rootEnv.ContainsKey("RESEND_FROM_EMAIL")) { $rootEnv["RESEND_FROM_EMAIL"] } else { $devVars["RESEND_FROM_EMAIL"] }
$authTokenSecret = if ($rootEnv.ContainsKey("AUTH_TOKEN_SECRET")) { $rootEnv["AUTH_TOKEN_SECRET"] } else { $devVars["AUTH_TOKEN_SECRET"] }

if (-not $token) {
    Write-Host "[Cloudflare Sync] CLOUDFLARE_API_TOKEN not found, skipping secret sync."
    exit 0
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type"  = "application/json"
}

# 1. Deploy latest version
try {
    $versionsUrl = "https://api.cloudflare.com/client/v4/accounts/$AccountId/workers/scripts/$ScriptName/versions"
    $versionsRes = Invoke-RestMethod -Uri $versionsUrl -Method Get -Headers $headers
    if ($versionsRes.success -and $versionsRes.result.items.Count -gt 0) {
        $latestVersionId = $versionsRes.result.items[0].id
        $deployUrl = "https://api.cloudflare.com/client/v4/accounts/$AccountId/workers/scripts/$ScriptName/deployments"
        $deployBody = @{
            versions = @(
                @{
                    version_id = $latestVersionId
                    percentage = 100
                }
            )
        } | ConvertTo-Json -Depth 5

        Invoke-RestMethod -Uri $deployUrl -Method Post -Headers $headers -Body $deployBody | Out-Null
        Write-Host "[Cloudflare Sync] Latest version ($latestVersionId) deployed successfully."
    }
} catch {
    Write-Host "[Cloudflare Sync] Note on deploying latest version: $_"
}

# 2. Upload secrets
$secretsToUpload = @()
if ($authTokenSecret) { $secretsToUpload += @{ name = "AUTH_TOKEN_SECRET"; text = $authTokenSecret; type = "secret_text" } }
if ($resendKey)         { $secretsToUpload += @{ name = "RESEND_API_KEY"; text = $resendKey; type = "secret_text" } }
if ($resendFrom)        { $secretsToUpload += @{ name = "RESEND_FROM_EMAIL"; text = $resendFrom; type = "secret_text" } }

foreach ($s in $secretsToUpload) {
    $name = $s.name
    $secretUrl = "https://api.cloudflare.com/client/v4/accounts/$AccountId/workers/scripts/$ScriptName/secrets"
    $secretBody = $s | ConvertTo-Json -Compress
    try {
        $res = Invoke-RestMethod -Uri $secretUrl -Method Put -Headers $headers -Body $secretBody
        if ($res.success) {
            Write-Host "[Cloudflare Sync] Secret $name bound successfully."
        }
    } catch {
        Write-Host "[Cloudflare Sync] Secret $name bind error: $_"
    }
}
