#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
CONFIGURATION=Release
parse_options "$@"
"$SCRIPT_DIR/build.sh" --defer-worker-tests "${FORWARD_ARGUMENTS[@]}"
STAGING_ROOT="$(mktemp -d "$REPO_ROOT/artifacts/macos/.package-$ARCH.XXXXXX")"
trap 'rm -rf "$STAGING_ROOT"' EXIT
ZIP="$STAGING_ROOT/JavMetaLite-macOS-$ARCH-$CONFIGURATION-$SIGN_MODE.zip"
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$ZIP"
python3 "$SCRIPT_DIR/release_manifest.py" payload --app "$APP_PATH" --output "$ZIP.manifest.json" --archive "$ZIP"
EXTRACTED="$STAGING_ROOT/解包 App"
mkdir "$EXTRACTED"
/usr/bin/ditto -x -k "$ZIP" "$EXTRACTED"
"$SCRIPT_DIR/verify-package.sh" "$EXTRACTED/JavMetaLite.app" --manifest "$ZIP.manifest.json" \
  --archive "$ZIP" --launch-app "${VERIFY_ARGUMENTS[@]}"
(cd "$(dirname "$ZIP")" && /usr/bin/shasum -a 256 "$(basename "$ZIP")" > "$(basename "$ZIP").sha256")
DESTINATION="$REPO_ROOT/artifacts/macos/$(basename "$ZIP")"
mv "$ZIP" "$DESTINATION"
mv "$ZIP.manifest.json" "$DESTINATION.manifest.json"
mv "$ZIP.sha256" "$DESTINATION.sha256"
cp "$DESTINATION.manifest.json" "$BUILD_ROOT/manifest.json"
printf 'Packaged %s build, not notarized: %s\n' "$SIGN_MODE" "$DESTINATION"
