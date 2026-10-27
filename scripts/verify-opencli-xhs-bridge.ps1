param()
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & node --check browser-extension/opencli-background.js
    if ($LASTEXITCODE) { throw 'Background syntax check failed' }
    & node --check browser-extension/popup/opencli-panel.js
    if ($LASTEXITCODE) { throw 'Popup syntax check failed' }
    & node --test tests/opencli-extension-bridge.test.cjs
    if ($LASTEXITCODE) { throw 'Extension bridge tests failed' }
    & dotnet run --project tests/OpenCliBridgeVerification/OpenCliBridgeVerification.csproj
    if ($LASTEXITCODE) { throw 'Host bridge tests failed' }
    & dotnet build src/OpenQuickHost/OpenQuickHost.csproj -c Release --no-restore -v minimal
    if ($LASTEXITCODE) { throw 'Desktop build failed' }
    & git diff --check
    if ($LASTEXITCODE) { throw 'Whitespace verification failed' }
}
finally { Pop-Location }
