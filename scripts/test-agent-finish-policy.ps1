$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$script = Join-Path $root 'scripts\finish-agent-task.ps1'
$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($script,[ref]$tokens,[ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) { throw "finish-agent-task syntax invalid: $($parseErrors[0].Message)" }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ("yanzi-agent-policy-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
function CheckGit([string[]]$argsForGit) {
    & git -C $fixture @argsForGit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Git fixture operation failed: $($argsForGit -join ' ')" }
}
function InvokePlan([bool]$expectedSuccess) {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $result = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $script -RepositoryRoot $fixture -VerificationScript (Join-Path $fixture 'verify.ps1') -Offline 2>&1)
        $passed = ($LASTEXITCODE -eq 0)
    } finally { $ErrorActionPreference = $oldPreference }
    if ($passed -ne $expectedSuccess) { throw "Unexpected result; expectedSuccess=$expectedSuccess output=$($result -join ' ')" }
    if ($expectedSuccess -and ($result -join ' ') -notmatch 'INTEGRATION_PLAN') { throw 'Missing plan output.' }
}
try {
    CheckGit @('init','-b','main')
    CheckGit @('config','user.name','Agent Fixture')
    CheckGit @('config','user.email','fixture@example.invalid')
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'),'base')
    [IO.File]::WriteAllText((Join-Path $fixture 'verify.ps1'),"Write-Output 'VERIFIED'")
    CheckGit @('add','--','README.md','verify.ps1')
    CheckGit @('commit','-m','baseline')
    CheckGit @('update-ref','refs/remotes/origin/main','HEAD')
    CheckGit @('checkout','-b','feat/test-integration')
    [IO.File]::AppendAllText((Join-Path $fixture 'README.md'), ' next')
    CheckGit @('add','--','README.md')
    CheckGit @('commit','-m','feat: example')
    InvokePlan $true
    [IO.File]::AppendAllText((Join-Path $fixture 'README.md'),' dirty')
    InvokePlan $false
    CheckGit @('checkout','--','README.md')
    New-Item -ItemType Directory -Path (Join-Path $fixture 'cloudflare') | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'cloudflare\index.js'),'// production change')
    CheckGit @('add','--','cloudflare/index.js')
    CheckGit @('commit','-m','feat: production change')
    InvokePlan $false
    Write-Output 'AGENT_FINISH_POLICY_TEST_OK cleanBranch=pass dirty=blocked cloudflare=blocked'
}
finally {
    if (Test-Path $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
