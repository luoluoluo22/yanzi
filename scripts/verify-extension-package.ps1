param(
    [string]$Configuration = "Debug",
    [string]$Framework = "net9.0-windows",
    [switch]$WebDav
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.Net.Http

function Convert-ToHexLower([byte[]]$Bytes) {
    return (($Bytes | ForEach-Object { $_.ToString("x2") }) -join "")
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\OpenQuickHost\OpenQuickHost.csproj"
$verifyDir = Join-Path $root ".artifacts\package-verify"
$publishDir = Join-Path $verifyDir "app"
$sampleDir = Join-Path $verifyDir "sample-extension"

if (Test-Path $verifyDir) {
    Remove-Item -Path $verifyDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $sampleDir | Out-Null

@"
{
  "id": "package-verify-extension",
  "name": "Package Verify Extension",
  "version": "0.1.0",
  "runtime": "csharp",
  "entryMode": "inline",
  "script": {
    "source": "using OpenQuickHost.CSharpRuntime; public static class YanziAction { public static string Run(YanziActionContext context) => context.InputText; }"
  }
}
"@ | Set-Content -Path (Join-Path $sampleDir "manifest.json") -Encoding UTF8

@"
# Package Verify Extension

This file exists only for package verification.
"@ | Set-Content -Path (Join-Path $sampleDir "README.md") -Encoding UTF8

dotnet build $project -c $Configuration -f $Framework -o $publishDir | Out-Host

$testerDir = Join-Path $verifyDir "tester"
New-Item -ItemType Directory -Force -Path $testerDir | Out-Null

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$project" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $testerDir "PackageVerify.csproj") -Encoding UTF8

@"
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using OpenQuickHost.Sync;

var sampleDir = args[0];
var method = typeof(ExtensionPackageService).GetMethod(
    "BuildDirectoryPackage",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Cannot find BuildDirectoryPackage.");
var bytes = (byte[])method.Invoke(null, new object[] { sampleDir })!;
var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
using var stream = new MemoryStream(bytes, writable: false);
using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
Console.WriteLine(Convert.ToBase64String(bytes));
Console.WriteLine($"Bytes={bytes.Length}");
Console.WriteLine($"SHA256={hash}");
Console.WriteLine($"Entries={string.Join(",", archive.Entries.Select(x => x.FullName))}");
"@ | Set-Content -Path (Join-Path $testerDir "Program.cs") -Encoding UTF8

$result = dotnet run --project (Join-Path $testerDir "PackageVerify.csproj") -- $sampleDir
$base64 = ($result | Where-Object { $_ -notmatch "^(Bytes=|SHA256=|Entries=)" } | Select-Object -Last 1).Trim()
$bytes = [Convert]::FromBase64String($base64)
$sha = [Security.Cryptography.SHA256]::Create()
$hash = Convert-ToHexLower ($sha.ComputeHash($bytes))

$stream = [IO.MemoryStream]::new($bytes, $false)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
$entries = @($archive.Entries | Select-Object -ExpandProperty FullName)
$archive.Dispose()
$stream.Dispose()

Write-Host "Local package is valid."
Write-Host "  Bytes: $($bytes.Length)"
Write-Host "  SHA256: $hash"
Write-Host "  Entries: $($entries -join ', ')"

if (-not $WebDav) {
    return
}

$runtimeDir = Join-Path $root "src\OpenQuickHost\bin\$Configuration\$Framework"
$settingsPath = Join-Path $runtimeDir "appsettings.local.json"
$credentialPath = Join-Path $runtimeDir "webdavcredentials.dat"
if (!(Test-Path $settingsPath) -or !(Test-Path $credentialPath)) {
    throw "WebDAV runtime settings or credentials not found under $runtimeDir"
}

$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
Add-Type -AssemblyName System.Security
$protectedBytes = [IO.File]::ReadAllBytes($credentialPath)
$plainBytes = [Security.Cryptography.ProtectedData]::Unprotect(
    $protectedBytes,
    $null,
    [Security.Cryptography.DataProtectionScope]::CurrentUser)
$credential = [Text.Encoding]::UTF8.GetString($plainBytes) | ConvertFrom-Json
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($settings.webDavUsername + ":" + $credential.password))

$client = [Net.Http.HttpClient]::new()
$client.BaseAddress = [Uri]::new($settings.webDavServerUrl.TrimEnd("/") + "/")
$client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new("Basic", $basic)

function Convert-ToRemotePath([string]$RelativePath) {
    $rootPath = if ([string]::IsNullOrWhiteSpace($settings.webDavRootPath)) { "/yanzi" } else { $settings.webDavRootPath }
    $rootPath = $rootPath.Trim("/")
    $suffix = (($RelativePath.Replace("\", "/").Trim("/") -split "/") | ForEach-Object { [uri]::EscapeDataString($_) }) -join "/"
    return "$rootPath/$suffix"
}

function Ensure-Collection([string]$RelativePath) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new("MKCOL"), (Convert-ToRemotePath $RelativePath))
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    if ($response.IsSuccessStatusCode -or [int]$response.StatusCode -eq 405 -or [int]$response.StatusCode -eq 409) {
        return
    }

    throw "MKCOL $RelativePath failed: $([int]$response.StatusCode) $($response.ReasonPhrase)"
}

$remotePackage = "packages/package-verify-extension/$hash.zip"
Ensure-Collection "yanzi"
Ensure-Collection "packages"
Ensure-Collection "packages/package-verify-extension"

$content = [Net.Http.ByteArrayContent]::new($bytes)
$content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/zip")
$put = $client.PutAsync((Convert-ToRemotePath $remotePackage), $content).GetAwaiter().GetResult()
if (!$put.IsSuccessStatusCode) {
    throw "PUT failed: $([int]$put.StatusCode) $($put.ReasonPhrase)"
}

$remoteBytes = $client.GetByteArrayAsync((Convert-ToRemotePath $remotePackage)).GetAwaiter().GetResult()
$remoteHash = Convert-ToHexLower ($sha.ComputeHash($remoteBytes))
$remoteStream = [IO.MemoryStream]::new($remoteBytes, $false)
$remoteArchive = [IO.Compression.ZipArchive]::new($remoteStream, [IO.Compression.ZipArchiveMode]::Read)
$remoteEntries = @($remoteArchive.Entries | Select-Object -ExpandProperty FullName)
$remoteArchive.Dispose()
$remoteStream.Dispose()

Write-Host "WebDAV package roundtrip is valid."
Write-Host "  Bytes: $($remoteBytes.Length)"
Write-Host "  SHA256: $remoteHash"
Write-Host "  Entries: $($remoteEntries -join ', ')"

if ($remoteHash -ne $hash) {
    throw "Remote hash mismatch."
}
