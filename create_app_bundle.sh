#!/bin/bash
set -e

APP_NAME="Yanzi.app"
PUBLISH_DIR="publish"
SIGN_IDENTITY="Yanzi Local Developer"

echo "Creating macOS App Bundle structure..."
mkdir -p "$APP_NAME/Contents/MacOS"
mkdir -p "$APP_NAME/Contents/Resources"

echo "Copying application files using ditto to preserve relative symlinks..."
ditto "$PUBLISH_DIR" "$APP_NAME/Contents/MacOS"

echo "Placing Info.plist and yanzi.icns..."
mv "$APP_NAME/Contents/MacOS/Info.plist" "$APP_NAME/Contents/Info.plist"
mv "$APP_NAME/Contents/MacOS/Assets/yanzi.icns" "$APP_NAME/Contents/Resources/yanzi.icns"

echo "Setting permissions..."
chmod +x "$APP_NAME/Contents/MacOS/Yanzi.Avalonia"

# Check if "Yanzi Local Developer" signing identity exists and is valid
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
if [ ! -f "$KEYCHAIN" ]; then
    KEYCHAIN="$HOME/Library/Keychains/login.keychain"
fi

if ! security find-identity -v -p codesigning | grep -q "$SIGN_IDENTITY"; then
    echo "Creating persistent local developer certificate '$SIGN_IDENTITY' in keychain..."
    TMPDIR=$(mktemp -d)
    openssl req -new -newkey rsa:2048 -days 3650 -nodes -x509 \
      -subj "/CN=$SIGN_IDENTITY/O=Yanzi/C=CN" \
      -keyout "$TMPDIR/yanzi_dev.key" -out "$TMPDIR/yanzi_dev.crt" \
      -addext "keyUsage = critical, digitalSignature" \
      -addext "extendedKeyUsage = critical, codeSigning" 2>/dev/null

    openssl pkcs12 -export -out "$TMPDIR/yanzi_dev.p12" -inkey "$TMPDIR/yanzi_dev.key" -in "$TMPDIR/yanzi_dev.crt" -passout pass:yanzi

    security import "$TMPDIR/yanzi_dev.p12" -k "$KEYCHAIN" -P yanzi -T /usr/bin/codesign
    security add-trusted-cert -d -r trustRoot -k "$KEYCHAIN" "$TMPDIR/yanzi_dev.crt" 2>/dev/null || true
    security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "" "$KEYCHAIN" 2>/dev/null || true
    rm -rf "$TMPDIR"
fi

echo "Signing app bundle with persistent developer identity '$SIGN_IDENTITY'..."
codesign --force --deep --sign "$SIGN_IDENTITY" --identifier "com.yanzi.launcher" "$APP_NAME"

echo "SUCCESS: $APP_NAME has been created successfully with persistent code signature!"
