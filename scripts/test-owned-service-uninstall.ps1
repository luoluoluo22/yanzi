# Non-destructive fixture: no real service, task, key, or Yanzi installation is modified.
$ErrorActionPreference='Stop'
$original=$env:LOCALAPPDATA
$repo=Split-Path $PSScriptRoot -Parent
$cleanup=Join-Path $repo 'src\OpenQuickHost\ServiceModules\uninstall-owned-services.ps1'
$sandbox=Join-Path $env:TEMP ('yanzi-owned-service-test-'+[guid]::NewGuid().ToString('N'))
$env:LOCALAPPDATA=$sandbox
try {
    $id='yanzi-owned-fixture'
    $localDirectory='YanziOwnedFixture'+[guid]::NewGuid().ToString('N').Substring(0,8)
    $task='YanziFixture'+[guid]::NewGuid().ToString('N').Substring(0,8)
    $root=Join-Path $sandbox $localDirectory
    $ledger=Join-Path $sandbox 'OpenQuickHost\ServiceModules\owned-installations.json'
    $manifestDir=Join-Path $sandbox 'manifest'
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'app\src'),(Join-Path $root 'repo'),
        (Join-Path $root 'data\inbox'),(Split-Path -Parent $ledger),$manifestDir|Out-Null
    $entry=Join-Path $root 'app\src\main.mjs'
    Set-Content -LiteralPath $entry -Value '// fixture'
    Set-Content -LiteralPath (Join-Path $root 'repo\README.txt') -Value 'fixture'
    Set-Content -LiteralPath (Join-Path $root 'data\inbox\user-file.txt') -Value 'keep me'
    foreach($name in @('config.json','control.token','private-network.secret')){
        Set-Content -LiteralPath (Join-Path $root $name) -Value 'dummy-fixture'
    }
    $record=[ordered]@{
        owner='yanzi-on-demand-service-v1';id=$id
        repository='https://github.com/luoluoluo22/yanzi-transfer-service.git'
        revision=('0'*40);localDirectory=$localDirectory
        managedTaskName=$task;managedEntryPoint='app/src/main.mjs'
        state='installed'
    }
    $descriptor=[ordered]@{
        id=$id;repository=$record.repository;revision=$record.revision
        localDirectory=$localDirectory;managedTaskName=$task
        managedEntryPoint='app/src/main.mjs'
    }
    function Save-Case {
        [IO.File]::WriteAllText($ledger,(ConvertTo-Json -InputObject @($record) -Depth 8),[Text.UTF8Encoding]::new($false))
        New-Item -ItemType Directory -Force -Path $manifestDir|Out-Null
        [IO.File]::WriteAllText((Join-Path $manifestDir ($id+'.json')),
            (ConvertTo-Json -InputObject $descriptor -Depth 8),[Text.UTF8Encoding]::new($false))
    }
    Save-Case
    & $cleanup -ManifestDir $manifestDir -LedgerPath $ledger -DryRun | Out-Null
    if(-not(Test-Path -LiteralPath $entry)){throw 'DryRun unexpectedly removed executable'}
    $dry=Get-Content (Join-Path $sandbox 'OpenQuickHost\ServiceUninstall\latest.json') -Raw|ConvertFrom-Json
    if($dry.failures -ne 0 -or $dry.results[0].status -ne 'dry-run'){throw 'DryRun failed'}
    Write-Output 'PASS: dry-run verified manifest but changed nothing'

    # Metadata mismatch must fail closed; no task and no files are deleted.
    $record.managedEntryPoint='app/src/other.mjs'
    Save-Case
    & $cleanup -ManifestDir $manifestDir -LedgerPath $ledger | Out-Null
    $failed=Get-Content (Join-Path $sandbox 'OpenQuickHost\ServiceUninstall\latest.json') -Raw|ConvertFrom-Json
    if($failed.failures -ne 1 -or -not(Test-Path -LiteralPath $entry)){throw 'Metadata mismatch did not fail closed'}
    Write-Output 'PASS: forged ownership metadata refused cleanup'

    $record.managedEntryPoint='app/src/main.mjs'
    Save-Case
    & $cleanup -ManifestDir $manifestDir -LedgerPath $ledger | Out-Null
    $outcome=Get-Content (Join-Path $sandbox 'OpenQuickHost\ServiceUninstall\latest.json') -Raw|ConvertFrom-Json
    if($outcome.failures -ne 0 -or $outcome.results[0].status -ne 'removed'){throw 'Owned fixture removal failed'}
    if(Test-Path -LiteralPath $entry){throw 'Owned executable remains'}
    if(Test-Path -LiteralPath (Join-Path $root 'config.json')){throw 'Owned config remains'}
    if(-not(Test-Path -LiteralPath (Join-Path $root 'data\inbox\user-file.txt'))){throw 'User file was deleted'}
    $entries=Get-Content $ledger -Raw|ConvertFrom-Json
    if(@($entries).Count -gt 0){throw 'Ledger still contains removed service'}
    Write-Output 'PASS: owned service removed, unrelated user files preserved, ledger cleared'

    # No ledger = unowned. Do not inspect or remove a standalone installation.
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $entry) | Out-Null
    Set-Content -LiteralPath $entry -Value '// preexisting independent service'
    Remove-Item -LiteralPath $ledger -Force
    Save-Case
    Remove-Item -LiteralPath $ledger -Force
    & $cleanup -ManifestDir $manifestDir -LedgerPath $ledger | Out-Null
    if(-not(Test-Path -LiteralPath $entry)){throw 'Unowned service was deleted'}
    Write-Output 'PASS: preexisting service without Yanzi ledger is left untouched'
    Write-Output 'OWNED_SERVICE_UNINSTALL_TEST_PASS'
}
finally {
    $env:LOCALAPPDATA=$original
    if(Test-Path -LiteralPath $sandbox){Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue}
}
