$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
$config = 'D:\a\yanzi\yanzi\cloudflare\wrangler.toml'
$cases = @(
    @{Command='node wrangler.js dev --config D:\a\yanzi\yanzi\cloudflare\wrangler.toml --port 8823'; Expected=$true},
    @{Command='node wrangler.js dev --config "D:/a/yanzi/yanzi/cloudflare/wrangler.toml" --port 8823'; Expected=$true},
    @{Command='node wrangler.js dev --config D:\unrelated\cloudflare\wrangler.toml --port 8823'; Expected=$false},
    @{Command='node wrangler.js dev --config D:\a\yanzi\yanzi\cloudflare\wrangler.toml.backup'; Expected=$false},
    @{Command='node wrangler.js deploy --config D:\a\yanzi\yanzi\cloudflare\wrangler.toml'; Expected=$false},
    @{Command='workerd.exe serve --config D:\a\yanzi\yanzi\cloudflare\wrangler.toml'; Expected=$false}
)
foreach ($case in $cases) {
    $actual = Test-YanziWorkerProcess ([pscustomobject]@{CommandLine=$case.Command}) -ConfigPath $config
    if ($actual -ne $case.Expected) { throw 'Worker ownership identification failed.' }
}
Write-Output 'WORKER_CHECKOUT_PATH_AND_UNRELATED_PROCESS_BOUNDARIES=PASSED; checks=6'
