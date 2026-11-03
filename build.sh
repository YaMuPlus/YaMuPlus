#!/bin/sh
# Builds both YaMu+ releases with the installed .NET SDK 9+ on macOS or Linux: dist/YaMuPlus-windows-x64.exe and, for
# Apple Silicon, dist/YaMuPlus-macos-arm64.dmg. The .app bundle gets an ad-hoc signature (an unsealed bundle is called
# damaged by macOS after download) and goes into the DMG: codesign and hdiutil on a Mac; on Linux rcodesign, xorriso
# and dmg from tools/ (prebuilt, tools/make-tools.sh). build-docker.sh runs this script in the .NET SDK container.
set -eu
cd "$(dirname "$0")"
[ "$(dotnet --version | cut -d. -f1)" -ge 9 ] || { echo ".NET SDK 9 or newer is required" >&2; exit 1; }
BUILD=${BUILD:-$(date +%s)} # build number: Unix time (UTC), the same in both releases
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/YaMuPlus.csproj)
PUB=src/bin/publish
rm -rf dist "$PUB"
mkdir -p dist

# runs a tool that writes a log: the log is shown only when it fails
quiet() { out=$("$@" 2>&1) || { echo "$out" >&2; return 1; }; }

publish() {
  dotnet publish src/YaMuPlus.csproj -c Release -r "$1" --self-contained true -p:DebugType=none -p:BuildNumber="$BUILD" -o "$PUB/$1"
}

echo "── 1/2 Windows (win-x64)"
publish win-x64
cp "$PUB/win-x64/YaMuPlus.exe" dist/YaMuPlus-windows-x64.exe

echo "── 2/2 macOS (osx-arm64)"
publish osx-arm64
STAGE="$PUB/mac"
APP="$STAGE/YaMu+.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$PUB/osx-arm64/YaMuPlus" "$PUB/osx-arm64"/*.dylib "$APP/Contents/MacOS/"
chmod 755 "$APP/Contents/MacOS/"*
cp src/Assets/icon.icns "$APP/Contents/Resources/AppIcon.icns"
sed -e "s/__VERSION__/$VERSION/g" -e "s/__BUILD__/$BUILD/g" src/Info.plist > "$APP/Contents/Info.plist"

# the DMG window: the app, the link to Applications and the background with the arrow between them; .DS_Store holds
# the layout (made once with dmgbuild) and refers to /Volumes/YaMu+/.background.tiff
ln -s /Applications "$STAGE/Applications"
cp src/Assets/dmg/background.tiff "$STAGE/.background.tiff"
cp src/Assets/dmg/DS_Store "$STAGE/.DS_Store"
if [ "$(uname -s)" = Darwin ]; then
  xattr -cr "$APP"
  codesign --force --deep -s - "$APP"
  hdiutil create -quiet -volname "YaMu+" -srcfolder "$STAGE" -format UDZO dist/YaMuPlus-macos-arm64.dmg
else
  T=tools/linux-$(uname -m)
  quiet "$T/rcodesign" sign "$APP"
  # ISO 9660 with Rock Ridge (-R: exact permissions, the owner keeps write access) keeps the link; dmg compresses it
  quiet "$T/xorriso" -as mkisofs -V "YaMu+" -D -l -R -no-pad -o "$PUB/image.iso" "$STAGE"
  quiet "$T/dmg" "$PUB/image.iso" dist/YaMuPlus-macos-arm64.dmg
fi
rm -rf "$PUB"
echo "Done:" dist/*
