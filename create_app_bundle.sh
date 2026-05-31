#!/bin/bash
set -e

APP_NAME="Yanzi.app"
PUBLISH_DIR="publish"

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

echo "Signing app bundle with stable ad-hoc identity..."
codesign --force --deep --sign - --identifier "com.yanzi.launcher" "$APP_NAME"

echo "SUCCESS: $APP_NAME has been created successfully!"
