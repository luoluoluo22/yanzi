param([string]$Version = '1.0.22')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$project=Get-Content (Join-Path $root 'src\OpenQuickHost\OpenQuickHost.csproj') -Encoding UTF8
if ($project.Project.PropertyGroup.Version.Trim() -ne $Version) { throw 'Version mismatch.' }
$solution=Get-Content (Join-Path $root 'OpenQuickHost.sln') -Raw -Encoding UTF8
if ($solution -match '(?m)^Project\([^\r\n]+ = "Yanzi\.Runtime"') { throw 'Independent runtime project in solution.' }
$runtimeProject=Join-Path $root 'src\Yanzi.Runtime\Yanzi.Runtime.csproj'
if (Test-Path -LiteralPath $runtimeProject) { throw 'Independent runtime project still in repository.' }
$source=Get-Content (Join-Path $root 'src\OpenQuickHost\App.xaml.cs') -Raw -Encoding UTF8
if ($source -notmatch 'LegacyRuntimeMigrationService.StopManagedProcesses') { throw 'Missing legacy runtime migration.' }
if ($source -match 'RuntimeConnection\.(EnsureAvailableAsync|AttachAsync)') { throw 'Unexpected Runtime attachment in startup.' }
$verification=Join-Path $root 'src\Yanzi.SyncVerification\Yanzi.SyncVerification.csproj'
& dotnet build $verification -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
foreach ($check in @('--single-process','--cloud-login-retry','--vip-version-policy')) {
    & dotnet run --project $verification -c Release --no-build -- $check
    if ($LASTEXITCODE -ne 0) { throw "Regression failed: $check" }
}
$profile=Join-Path $root 'src\Yanzi.RuntimeProfileVerification\Yanzi.RuntimeProfileVerification.csproj'
& dotnet run --project $profile -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Stable profile regression failed.' }
$payload=Join-Path $root '.artifacts\publish\win-x64'
if (Test-Path -LiteralPath $payload) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-release-layout.ps1') -PublishDirectory $payload -ExpectedVersion ($Version + '.0')
    if ($LASTEXITCODE -ne 0) { throw 'Release payload contains a Runtime.' }
}
& git -C $root diff --check HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Invalid source diff.' }
Write-Output 'YANZI_SINGLE_PROCESS_RELEASE_VERIFIED'
