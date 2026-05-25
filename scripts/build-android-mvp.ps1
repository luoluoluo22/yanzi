param(
    [string]$SdkPath = $env:ANDROID_HOME,
    [string]$Configuration = "debug"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SdkPath)) {
    $SdkPath = "F:\SDK"
}

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$AppRoot = Join-Path $ProjectRoot "mobile\android\app"
$BuildTools = Join-Path $SdkPath "build-tools\35.0.0"
$AndroidJar = Join-Path $SdkPath "platforms\android-35\android.jar"
$Aapt2 = Join-Path $BuildTools "aapt2.exe"
$D8 = Join-Path $BuildTools "d8.bat"
$ZipAlign = Join-Path $BuildTools "zipalign.exe"
$ApkSigner = Join-Path $BuildTools "apksigner.bat"
$BuildRoot = Join-Path $AppRoot "build\manual-$Configuration"
$CompiledRes = Join-Path $BuildRoot "compiled-res.zip"
$GeneratedJava = Join-Path $BuildRoot "generated"
$Classes = Join-Path $BuildRoot "classes"
$Dex = Join-Path $BuildRoot "dex"
$UnsignedApk = Join-Path $BuildRoot "yanzi-mobile-unsigned.apk"
$AlignedApk = Join-Path $BuildRoot "yanzi-mobile-aligned.apk"
$OutputApk = Join-Path $BuildRoot "yanzi-mobile-$Configuration.apk"
$DebugKeystore = Join-Path $env:USERPROFILE ".android\debug.keystore"

foreach ($required in @($AndroidJar, $Aapt2, $D8, $ZipAlign, $ApkSigner)) {
    if (-not (Test-Path $required)) {
        throw "Missing Android build tool: $required"
    }
}

Remove-Item -LiteralPath $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $BuildRoot, $GeneratedJava, $Classes, $Dex | Out-Null

& $Aapt2 compile --dir (Join-Path $AppRoot "src\main\res") -o $CompiledRes
if ($LASTEXITCODE -ne 0) { throw "aapt2 compile failed: $LASTEXITCODE" }

& $Aapt2 link `
    -o $UnsignedApk `
    -I $AndroidJar `
    --manifest (Join-Path $AppRoot "src\main\AndroidManifest.xml") `
    -R $CompiledRes `
    --java $GeneratedJava `
    --min-sdk-version 26 `
    --target-sdk-version 35 `
    --version-code 1 `
    --version-name "0.1.0" `
    --auto-add-overlay `
    --debug-mode
if ($LASTEXITCODE -ne 0) { throw "aapt2 link failed: $LASTEXITCODE" }

$javaSources = @(
    Get-ChildItem (Join-Path $AppRoot "src\main\java") -Recurse -Filter *.java
    Get-ChildItem $GeneratedJava -Recurse -Filter *.java
) | ForEach-Object { $_.FullName }

& javac -encoding UTF-8 -source 8 -target 8 -classpath $AndroidJar -d $Classes @javaSources
if ($LASTEXITCODE -ne 0) { throw "javac failed: $LASTEXITCODE" }

$classFiles = Get-ChildItem $Classes -Recurse -Filter *.class | ForEach-Object { $_.FullName }
& $D8 --debug --min-api 26 --lib $AndroidJar --output $Dex @classFiles
if ($LASTEXITCODE -ne 0) { throw "d8 failed: $LASTEXITCODE" }

Push-Location $Dex
try {
    & jar uf $UnsignedApk classes.dex
    if ($LASTEXITCODE -ne 0) { throw "jar update failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
}

& $ZipAlign -f -p 4 $UnsignedApk $AlignedApk
if ($LASTEXITCODE -ne 0) { throw "zipalign failed: $LASTEXITCODE" }

if (-not (Test-Path $DebugKeystore)) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $DebugKeystore) | Out-Null
    & keytool -genkeypair `
        -keystore $DebugKeystore `
        -storepass android `
        -alias androiddebugkey `
        -keypass android `
        -keyalg RSA `
        -keysize 2048 `
        -validity 10000 `
        -dname "CN=Android Debug,O=Android,C=US"
    if ($LASTEXITCODE -ne 0) { throw "debug keystore generation failed: $LASTEXITCODE" }
}

& $ApkSigner sign `
    --ks $DebugKeystore `
    --ks-pass pass:android `
    --key-pass pass:android `
    --out $OutputApk `
    $AlignedApk
if ($LASTEXITCODE -ne 0) { throw "apksigner failed: $LASTEXITCODE" }

& $ApkSigner verify $OutputApk
if ($LASTEXITCODE -ne 0) { throw "apk verify failed: $LASTEXITCODE" }

Write-Host "Android MVP APK built: $OutputApk"
