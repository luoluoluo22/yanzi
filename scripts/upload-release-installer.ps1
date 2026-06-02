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

@"
一键安装包：$fileName

SHA256: $hash
"@ | Set-Content -LiteralPath $notesPath -Encoding UTF8

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

$filesToUpload = Get-ChildItem -Path $installerOutDir -File | Where-Object { $_.Name -match "nupkg|releases\.win\.json|Setup.*\.exe|Portable.*\.zip" }
foreach ($file in $filesToUpload) {
    Write-Host "Uploading $($file.Name) to Release $tag..."
    gh release upload $tag $file.FullName --repo $Repo --clobber *> $null
    Write-Host "Successfully uploaded $($file.Name)."
}

if (-not $Draft) {
    gh release edit $tag --repo $Repo --draft=false | Out-Host
}

gh release view $tag --repo $Repo --json tagName,name,isDraft,url,assets | Out-Host
