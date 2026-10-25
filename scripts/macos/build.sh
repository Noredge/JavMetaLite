#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
parse_options "$@"
check_environment
check_signing_identity
cd "$REPO_ROOT"
"$DOTNET" restore JavMetaLite.Worker/JavMetaLite.Worker.csproj -r "osx-$ARCH" --locked-mode \
  -p:NuGetLockFilePath="packages.osx-$ARCH.lock.json"
rm -rf "$WORKER_PUBLISH"
mkdir -p "$WORKER_PUBLISH"
"$DOTNET" publish JavMetaLite.Worker/JavMetaLite.Worker.csproj -c "$CONFIGURATION" \
  -r "osx-$ARCH" --no-restore --self-contained true -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:PublishSingleFile=false \
  -p:PublishTrimmed=false -p:PublishAot=false -o "$WORKER_PUBLISH"
/usr/bin/xcrun swift build --package-path "$REPO_ROOT/macos" -c "$SWIFT_CONFIGURATION" --arch "$SWIFT_ARCH"
SWIFT_BIN="$(/usr/bin/xcrun swift build --package-path "$REPO_ROOT/macos" -c "$SWIFT_CONFIGURATION" --arch "$SWIFT_ARCH" --show-bin-path)"
[ -x "$SWIFT_BIN/JavMetaLite" ] || fail 'Swift build did not produce JavMetaLite.'
# Only this script-owned build output is replaced; no media or source directory is touched.
rm -rf "$APP_PATH"
mkdir -p "$APP_PATH/Contents/MacOS" "$APP_PATH/Contents/Resources"
HELPER="$APP_PATH/Contents/Helpers/JavMetaLiteWorker.bundle/Contents"
mkdir -p "$HELPER/MacOS"
cp "$SWIFT_BIN/JavMetaLite" "$APP_PATH/Contents/MacOS/JavMetaLite"
cp "$REPO_ROOT/macos/JavMetaLiteMac/Resources/Info.plist" "$APP_PATH/Contents/Info.plist"
find "$SWIFT_BIN" -maxdepth 1 -name '*.bundle' -type d -exec cp -R '{}' "$APP_PATH/Contents/Resources/" \;
cp -R "$WORKER_PUBLISH/." "$HELPER/MacOS/"
python3 - "$APP_PATH/Contents/Info.plist" "$HELPER/Info.plist" <<'PLIST'
import pathlib, plistlib, sys
app = plistlib.loads(pathlib.Path(sys.argv[1]).read_bytes())
info = {key: app[key] for key in ('CFBundleVersion', 'CFBundleShortVersionString', 'LSMinimumSystemVersion')}
info.update(CFBundleIdentifier=app['CFBundleIdentifier'] + '.worker', CFBundleExecutable='JavMetaLite.Worker', CFBundleName='JavMetaLite Worker', CFBundlePackageType='BNDL')
pathlib.Path(sys.argv[2]).write_bytes(plistlib.dumps(info))
PLIST
cp "$REPO_ROOT/macos/JavMetaLiteMac/Resources/JavMetaLite.icns" "$APP_PATH/Contents/Resources/JavMetaLite.icns"
cp "$REPO_ROOT/LICENSE" "$APP_PATH/Contents/Resources/LICENSE.txt"
cp "$REPO_ROOT/THIRD_PARTY_NOTICES.md" "$APP_PATH/Contents/Resources/THIRD_PARTY_NOTICES.md"
if [ -f "$REPO_ROOT/docs/macos/THIRD_PARTY_NOTICES.md" ]; then
  cp "$REPO_ROOT/docs/macos/THIRD_PARTY_NOTICES.md" "$APP_PATH/Contents/Resources/MACOS_THIRD_PARTY_NOTICES.md"
fi
if [ -d "$REPO_ROOT/docs/macos/licenses" ]; then
  cp -R "$REPO_ROOT/docs/macos/licenses" "$APP_PATH/Contents/Resources/"
fi
chmod +x "$APP_PATH/Contents/MacOS/JavMetaLite" "$HELPER/MacOS/JavMetaLite.Worker"
cp "$REPO_ROOT/docs/macos/PACKAGE.md" "$APP_PATH/Contents/Resources/READ-ME.md"
python3 "$SCRIPT_DIR/release_manifest.py" build --app "$APP_PATH" --dotnet "$DOTNET" \
  "${PROVENANCE_ARGUMENTS[@]}"
"$SCRIPT_DIR/sign.sh" "$APP_PATH" "${SIGN_ARGUMENTS[@]}" --report "$BUILD_ROOT/signing-report.json"
python3 "$SCRIPT_DIR/release_manifest.py" payload --app "$APP_PATH" --output "$BUILD_ROOT/manifest.json"
"$SCRIPT_DIR/verify-package.sh" "$APP_PATH" --manifest "$BUILD_ROOT/manifest.json" "${VERIFY_ARGUMENTS[@]}"
printf 'Verified %s app (not notarized): %s\n' "$SIGN_MODE" "$APP_PATH"
