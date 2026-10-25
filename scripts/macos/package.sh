#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
CONFIGURATION=Release
parse_options "$@"
"$SCRIPT_DIR/build.sh" "${FORWARD_ARGUMENTS[@]}"
STAGING_ROOT="$(mktemp -d "$REPO_ROOT/artifacts/macos/.package-$ARCH.XXXXXX")"
trap 'rm -rf "$STAGING_ROOT"' EXIT
ZIP="$STAGING_ROOT/JavMetaLite-macOS-$ARCH-$CONFIGURATION-$SIGN_MODE.zip"
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$ZIP"
python3 "$SCRIPT_DIR/release_manifest.py" payload --app "$APP_PATH" --output "$ZIP.manifest.json" --archive "$ZIP"
# Validate the archive after extraction, including generic payload signatures.
python3 - "$ZIP" "$SCRIPT_DIR/verify-package.sh" "$SCRIPT_DIR/test_worker.py" "${VERIFY_ARGUMENTS[@]}" <<'PYVERIFY'
import os, pathlib, subprocess, sys, tempfile
with tempfile.TemporaryDirectory(prefix="JavMetaLite 中文 解包 ") as temporary:
    subprocess.run(['/usr/bin/ditto', '-x', '-k', sys.argv[1], temporary], check=True)
    app = pathlib.Path(temporary) / 'JavMetaLite.app'
    subprocess.run([sys.argv[2], str(app), '--manifest', sys.argv[1] + '.manifest.json', '--archive', sys.argv[1], *sys.argv[4:]], check=True)
    worker = app / 'Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker'
    environment = {'HOME': temporary, 'TMPDIR': temporary, 'PATH': '/usr/bin:/bin'}
    # This no-SDK extraction gate runs only the shipping executable.
    # Fixed-provider integration is mandatory in test.sh, using a separate nonpublishable host.
    subprocess.run([sys.executable, sys.argv[3], str(worker), '--production-only'], env=environment, check=True)
    subprocess.run([sys.executable, str(pathlib.Path(sys.argv[3]).with_name('test_queue_interactions.py')), str(worker)], env=environment, check=True)
    subprocess.run([sys.executable, str(pathlib.Path(sys.argv[3]).with_name('test_worker_retention.py')), str(worker)], env=environment, check=True)
PYVERIFY
(cd "$(dirname "$ZIP")" && /usr/bin/shasum -a 256 "$(basename "$ZIP")" > "$(basename "$ZIP").sha256")
DESTINATION="$REPO_ROOT/artifacts/macos/$(basename "$ZIP")"
mv "$ZIP" "$DESTINATION"
mv "$ZIP.manifest.json" "$DESTINATION.manifest.json"
mv "$ZIP.sha256" "$DESTINATION.sha256"
cp "$DESTINATION.manifest.json" "$BUILD_ROOT/manifest.json"
printf 'Packaged %s build, not notarized: %s\n' "$SIGN_MODE" "$DESTINATION"
