$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tracked = @(& git -C $root ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked files.' }
$unwanted = @($tracked | Where-Object {
    $_ -match '(^|/)node_modules/' -or
    $_ -match '\.xaml\.bak$|_wpftmp\.csproj$' -or
    $_ -match '^mobile/android/clean_diff.*\.patch$'
})
if ($unwanted.Count) { throw "Tracked local artifacts: $($unwanted -join ', ')" }
$samples = @('node_modules/.mf/cf.json', 'src/OpenQuickHost/Example.xaml.bak',
    'src/OpenQuickHost/OpenQuickHost_example_wpftmp.csproj', 'mobile/android/clean_diff.patch')
foreach ($sample in $samples) {
    & git -C $root check-ignore -q -- $sample
    if ($LASTEXITCODE -ne 0) { throw "Artifact is not ignored: $sample" }
}
& git -C $root diff --check
if ($LASTEXITCODE -ne 0) { throw 'Whitespace validation failed.' }
Write-Output 'Repository artifact cleanup verified.'
