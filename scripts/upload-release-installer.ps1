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

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $root ".artifacts\installer\YanziSetup-$plainVersion.exe"
}

if (!(Test-Path -LiteralPath $InstallerPath)) {
    throw "Installer not found: $InstallerPath. Run scripts\publish-installer.ps1 first."
}
$InstallerPath = (Resolve-Path $InstallerPath).Path

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

$hash = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$fileName = Split-Path $InstallerPath -Leaf
$notesPath = Join-Path ([IO.Path]::GetTempPath()) "yanzi-release-$plainVersion.md"

@"
一键安装包：$fileName

SHA256: $hash
"@ | Set-Content -LiteralPath $notesPath -Encoding UTF8

$releaseExists = $true
gh release view $tag --repo $Repo *> $null
if ($LASTEXITCODE -ne 0) {
    $releaseExists = $false
}

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

gh release upload $tag $InstallerPath --repo $Repo --clobber | Out-Host

if (-not $Draft) {
    gh release edit $tag --repo $Repo --draft=false | Out-Host
}

gh release view $tag --repo $Repo --json tagName,name,isDraft,url,assets | Out-Host
