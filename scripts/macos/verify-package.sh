#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
[ "$#" -ge 1 ] || { echo 'Usage: verify-package.sh <JavMetaLite.app> [--manifest JSON] [--archive ZIP] [--expected-sign-mode adhoc|developer-id] [--expected-team-id TEAM]' >&2; exit 2; }
if [ "$1" = '--help' ] || [ "$1" = '-h' ]; then exec python3 "$SCRIPT_DIR/verify_package.py" --help; fi
APP="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
shift
[ -d "$APP" ] || { echo 'App does not exist.' >&2; exit 1; }
/usr/bin/plutil -lint "$APP/Contents/Info.plist"
/usr/bin/codesign --verify --deep --strict --verbose=1 "$APP"
python3 "$SCRIPT_DIR/verify_package.py" "$APP" "$@"
