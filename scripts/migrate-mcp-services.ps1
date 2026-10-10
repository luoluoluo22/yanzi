param(
  [string]$RaccoonSource = 'F:\Desktop\kaifa\raccoon-mcp',
  [string]$YanziSource = 'F:\Desktop\kaifa\OpenQuickHost\tools\yanzi-mcp'
)
$ErrorActionPreference = 'Stop'
$extensions = Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions'
$hostSyncFile = 'F:\Desktop\kaifa\OpenQuickHost\src\OpenQuickHost\Sync\ExtensionPackageService.cs'
$body = [IO.File]::ReadAllText($hostSyncFile)
$anchor = 'if (segments.Any(static segment =>'
$inject = @'
if (segments.Any(static segment =>
                segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".runtime", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".raccoon-runtime", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
                (segment.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) &&
                 !segment.Equals(".env.example", StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

'@
if (!$body.Contains('segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)')) {
    if (!$body.Contains($anchor)) { throw 'Sync package filter anchor missing' }
    [IO.File]::WriteAllText($hostSyncFile, $body.Replace($anchor, $inject + $anchor), [Text.UTF8Encoding]::new($false))
}
foreach ($item in @(
    @{ Source=$RaccoonSource; Target=(Join-Path $extensions 'raccoon-manager\service'); Extra=@('pipelines','plugin.json') },
    @{ Source=$YanziSource; Target=(Join-Path $extensions 'ext_ef5c4c9e801543059e8bc05760392ce1\service'); Extra=@() }
)) {
    if (Test-Path (Join-Path $item.Target 'package-lock.json')) { Write-Output ('SKIP_ALREADY_MIGRATED '+$item.Target); continue }
    New-Item -Path $item.Target -ItemType Directory -Force | Out-Null
    foreach ($sub in (@('src','scripts','test','assets','package.json','package-lock.json','.env.example','README.md') + $item.Extra)) {
        $source = Join-Path $item.Source $sub
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $item.Target -Recurse -Force }
    }
    Write-Output ("BUNDLED " + $item.Target + " files=" + @(Get-ChildItem -LiteralPath $item.Target -Recurse -File).Count)
}
Write-Output 'SYNC_FILTER_PATCH=OK'
