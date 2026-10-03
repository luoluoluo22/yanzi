param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $env:TEMP ('YanziUpdateTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
& javac -encoding UTF-8 -d $output (Join-Path $root 'mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/UpdateManifestClient.java') (Join-Path $PSScriptRoot 'java/UpdateManifestClientVerification.java')
if ($LASTEXITCODE -ne 0) { throw 'Update verification compilation failed' }
& java -cp $output cc.luoluoluo.yanzi.mobile.UpdateManifestClientVerification
if ($LASTEXITCODE -ne 0) { throw 'Update transport verification failed' }
& javac -encoding UTF-8 -d $output (Join-Path $root 'mobile/android/app/src/main/java/cc/luoluoluo/yanzi/mobile/UpdateDownloadNodes.java') (Join-Path $PSScriptRoot 'java/UpdateDownloadNodesVerification.java')
if ($LASTEXITCODE -ne 0) { throw 'Update node verification compilation failed' }
& java -cp $output cc.luoluoluo.yanzi.mobile.UpdateDownloadNodesVerification
if ($LASTEXITCODE -ne 0) { throw 'Update node verification failed' }
