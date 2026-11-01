#Requires -Version 5.1
# Safe, offline regression gate: no install, no release, no user-data writes.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$policy = Get-Content (Join-Path $root 'docs\development-integration-release-policy.md') -Raw -Encoding UTF8
$agents = Get-Content (Join-Path $root 'AGENTS.md') -Raw -Encoding UTF8
$app = Get-Content (Join-Path $root 'src\OpenQuickHost\App.xaml.cs') -Raw -Encoding UTF8
$window = Get-Content (Join-Path $root 'src\OpenQuickHost\RunningExtensionsWindow.xaml.cs') -Raw -Encoding UTF8
$evidence = Get-Content (Join-Path $root 'docs\incident-running-extensions-ui-freeze-20261101.md') -Raw -Encoding UTF8

function Assert-Has([string]$text, [string]$needle, [string]$name) {
    if (-not $text.Contains($needle)) { throw "Missing local QA contract: $name" }
    Write-Output "PASS: $name"
}

Assert-Has $policy '本地即时安装 QA 与 GitHub Release 延迟发布严格分离' 'immediate local QA and delayed public release separated'
Assert-Has $policy '入队满 72 小时' '72-hour public release trigger preserved'
Assert-Has $policy '连续闲置超过 10 分钟' '10-minute idle public release trigger preserved'
Assert-Has $policy '不能因为正式 GitHub Release 进入延迟队列，就跳过或推迟本机安装验证' 'public release delay cannot block local QA'
Assert-Has $agents '本地安装验证 ≠ GitHub 正式 Release' 'agent entrypoint links QA boundary'
Assert-Has $app 'arg.Equals("--running-extensions-preview"' 'dev-only real window preview switch'
Assert-Has $app 'arg.Equals("--running-extensions-qa"' 'dev-only churn QA switch'
Assert-Has $app 'HostRuntimeProfile.IsDevelopment && e.Args.Any' 'development guard'
Assert-Has $app 'RunningExtensionRegistry.RegisterManagedExtension' 'real registry churn registration'
Assert-Has $app 'RunningExtensionRegistry.Remove(id, "dev-only-qa-cleanup")' 'fixture cleanup'
Assert-Has $window 'await Task.Run(RunningExtensionRegistry.GetSnapshot)' 'off-dispatcher snapshot'
Assert-Has $window 'Interlocked.Exchange(ref _registryRefreshPosted' 'bounded dispatcher callbacks'
Assert-Has $evidence '60/60 successful responses' 'native UI heartbeat evidence'
Assert-Has $evidence 'count=24, changes=24' 'non-empty real UI list evidence'

$tokens = $null
$errors = $null
[void][Management.Automation.Language.Parser]::ParseFile($MyInvocation.MyCommand.Path, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw "PowerShell syntax invalid: $($errors[0])" }
Write-Output 'LOCAL_QA_RELEASE_SPLIT_CONTRACT_PASSED'
