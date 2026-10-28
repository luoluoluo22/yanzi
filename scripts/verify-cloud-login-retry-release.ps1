$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project=Join-Path $root 'src\Yanzi.SyncVerification\Yanzi.SyncVerification.csproj'
$hostProject=Join-Path $root 'src\OpenQuickHost\OpenQuickHost.csproj'
[xml]$versionXml=Get-Content -LiteralPath $hostProject -Raw -Encoding UTF8
if ($versionXml.Project.PropertyGroup.Version -ne '1.0.21') { throw 'Unexpected host version.' }
& dotnet build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Windows verification build failed.' }
foreach ($test in @('--cloud-login-retry','--vip-version-policy')) {
    & dotnet run --project $project -c Release --no-build -- $test
    if ($LASTEXITCODE -ne 0) { throw "Regression failed: $test" }
}
& git -C $root diff --check HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Reviewed source has whitespace errors.' }
Write-Output 'CLOUD_LOGIN_RELEASE_VERIFIED'
