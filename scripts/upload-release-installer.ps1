param(
    [string]$Version = "0.1.0",
    [string]$Repo = "luoluoluo22/yanzi",
    [string]$Target = "main",
    [string]$InstallerPath = "",
    [switch]$Draft,
    [switch]$KeepProxy
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$tag = if ($Version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) { $Version } else { "v$Version" }
$plainVersion = $tag.TrimStart("v")

$installerOutDir = Join-Path $root ".artifacts\installer"
if (!(Test-Path $installerOutDir)) {
    throw "Installer directory not found: $installerOutDir. Run scripts\publish-installer.ps1 first."
}

$fileName = "Yanzi-win-Setup-$plainVersion.exe"
$installerSetupPath = Join-Path $installerOutDir $fileName
if (!(Test-Path -LiteralPath $installerSetupPath)) {
    throw "$fileName not found under $installerOutDir. Run scripts\publish-installer.ps1 first."
}

if (-not $KeepProxy) {
    $env:HTTP_PROXY = ""
    $env:HTTPS_PROXY = ""
    $env:ALL_PROXY = ""
    $env:NO_PROXY = ""
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    throw "GitHub CLI was not found. Install gh first: https://cli.github.com/"
}

gh api user --jq .login | Out-Host

$hash = (Get-FileHash -LiteralPath $installerSetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
$notesPath = Join-Path ([IO.Path]::GetTempPath()) "yanzi-release-$plainVersion.md"

$notesContent = @"
一键安装包：$fileName

SHA256: $hash
"@
[System.IO.File]::WriteAllText($notesPath, $notesContent, [System.Text.Encoding]::UTF8)

$releaseExists = $true
$oldEAP = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
gh release view $tag --repo $Repo *> $null
if ($LASTEXITCODE -ne 0) {
    $releaseExists = $false
}
$ErrorActionPreference = $oldEAP

if (-not $releaseExists) {
    gh release create $tag `
        --repo $Repo `
        --target $Target `
        --title "Yanzi $plainVersion" `
        --notes-file $notesPath `
        --draft | Out-Host
} else {
    gh release edit $tag `
        --repo $Repo `
        --title "Yanzi $plainVersion" `
        --notes-file $notesPath | Out-Host
}

$filesToUpload = Get-ChildItem -Path $installerOutDir -File | Where-Object { $_.Name.Contains($plainVersion) -or $_.Name -match "releases\.win\.json|RELEASES" }
foreach ($file in $filesToUpload) {
    $maxRetries = 5
    $retryCount = 0
    $uploaded = $false

    while (-not $uploaded -and $retryCount -lt $maxRetries) {
        $retryCount++
        try {
            Write-Host "Uploading $($file.Name) to Release $tag... (Attempt $retryCount/$maxRetries)"
            
            $oldEAP = $ErrorActionPreference
            $ErrorActionPreference = "Stop"
            
            & gh release upload $tag $file.FullName --repo $Repo --clobber
            
            $ErrorActionPreference = $oldEAP
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Successfully uploaded $($file.Name)."
                $uploaded = $true
            } else {
                throw "gh release upload returned non-zero exit code: $LASTEXITCODE"
            }
        } catch {
            Write-Warning ("Failed to upload {0} on attempt {1}: {2}" -f $file.Name, $retryCount, $_)
            if ($retryCount -lt $maxRetries) {
                Write-Host "Waiting 5 seconds before retrying..."
                Start-Sleep -Seconds 5
            } else {
                throw "Failed to upload $($file.Name) after $maxRetries attempts."
            }
        }
    }
}

if (-not $Draft) {
    gh release edit $tag --repo $Repo --draft=false | Out-Host
}

gh release view $tag --repo $Repo --json tagName,name,isDraft,url,assets | Out-Host
