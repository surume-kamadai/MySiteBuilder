#!/usr/bin/env bash
# ============================================================
# pack-mac.sh — macOS 配布物（.app → dmg）を作る。
# pack-mac.sh — build the macOS distributable (.app → dmg).
#
# 前提 / Prereqs:
#   - macOS + .NET SDK
#   - create-dmg:  brew install create-dmg
#   実行 / Run:    RID=osx-arm64 bash scripts/pack-mac.sh 1.0.0
#
# 本番配布では Apple Developer 署名 + notarize が別途必要。
# Production distribution additionally needs Apple Developer signing + notarization.
# ============================================================
set -euo pipefail

VERSION="${1:-1.0.0}"
RID="${RID:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PUB="$ROOT/publish/$RID"
APP="$ROOT/publish/SiteBuilder.app"

dotnet publish "$ROOT/src/SiteBuilder.Host/SiteBuilder.Host.csproj" \
    -c Release -r "$RID" --self-contained -o "$PUB"

# .app バンドルを組み立てる / assemble the .app bundle.
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cp -R "$PUB/." "$APP/Contents/MacOS/"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Site Builder</string>
    <key>CFBundleDisplayName</key><string>Site Builder</string>
    <key>CFBundleIdentifier</key><string>com.surumekamadai.sitebuilder</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleExecutable</key><string>SiteBuilder</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

# dmg を作る / build the dmg.
create-dmg "$ROOT/publish/SiteBuilder-${VERSION}.dmg" "$APP"

echo "macOS package created: publish/SiteBuilder-${VERSION}.dmg"
