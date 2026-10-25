#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ARCH="arm64"
CONFIGURATION="Debug"
SIGN_MODE="adhoc"
SIGN_IDENTITY=""
TEAM_ID=""
ALLOW_MODIFIED_SOURCE_EXPORT=false
fail() { echo "JavMetaLite: $*" >&2; exit 1; }
parse_options() {
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --arch) [ "$#" -ge 2 ] || fail '--arch requires arm64 or x64'; ARCH="$2"; shift 2 ;;
      --configuration) [ "$#" -ge 2 ] || fail '--configuration requires Debug or Release'; CONFIGURATION="$2"; shift 2 ;;
      --sign-mode) [ "$#" -ge 2 ] || fail '--sign-mode requires adhoc or developer-id'; SIGN_MODE="$2"; shift 2 ;;
      --sign-identity) [ "$#" -ge 2 ] || fail '--sign-identity requires a full Developer ID identity or SHA-1'; SIGN_IDENTITY="$2"; shift 2 ;;
      --team-id) [ "$#" -ge 2 ] || fail '--team-id requires a ten-character Apple Team ID'; TEAM_ID="$2"; shift 2 ;;
      --allow-modified-source-export) ALLOW_MODIFIED_SOURCE_EXPORT=true; shift ;;
      --help|-h) echo 'Options: --arch arm64|x64 --configuration Debug|Release --sign-mode adhoc|developer-id [--sign-identity IDENTITY] [--team-id TEAM] [--allow-modified-source-export]'; exit 0 ;;
      *) fail "Unknown option: $1" ;;
    esac
  done
  case "$ARCH" in arm64) SWIFT_ARCH=arm64 ;; x64) SWIFT_ARCH=x86_64 ;; *) fail "Unsupported architecture: $ARCH" ;; esac
  case "$CONFIGURATION" in Debug) SWIFT_CONFIGURATION=debug ;; Release) SWIFT_CONFIGURATION=release ;; *) fail "Unsupported configuration: $CONFIGURATION" ;; esac
  case "$SIGN_MODE" in
    adhoc) [ -z "$SIGN_IDENTITY$TEAM_ID" ] || fail 'Ad-hoc mode cannot accept a signing identity or team.' ;;
    developer-id) [ -n "$SIGN_IDENTITY" ] || fail 'Developer ID mode requires --sign-identity; no ad-hoc fallback.' ;;
    *) fail 'Unsupported signing mode; unsigned output is forbidden.' ;;
  esac
  [ -z "$TEAM_ID" ] || [[ "$TEAM_ID" =~ ^[A-Z0-9]{10}$ ]] || fail 'Team ID must contain exactly ten uppercase letters or digits.'
  SIGN_ARGUMENTS=(--mode "$SIGN_MODE")
  PROVENANCE_ARGUMENTS=(--arch "$ARCH" --configuration "$CONFIGURATION" --sign-mode "$SIGN_MODE")
  VERIFY_ARGUMENTS=(--expected-sign-mode "$SIGN_MODE")
  FORWARD_ARGUMENTS=(--arch "$ARCH" --configuration "$CONFIGURATION" --sign-mode "$SIGN_MODE")
  if [ "$ALLOW_MODIFIED_SOURCE_EXPORT" = true ]; then PROVENANCE_ARGUMENTS+=(--allow-modified-source-export); FORWARD_ARGUMENTS+=(--allow-modified-source-export); fi
  if [ -n "$SIGN_IDENTITY" ]; then SIGN_ARGUMENTS+=(--identity "$SIGN_IDENTITY"); FORWARD_ARGUMENTS+=(--sign-identity "$SIGN_IDENTITY"); fi
  if [ -n "$TEAM_ID" ]; then SIGN_ARGUMENTS+=(--team-id "$TEAM_ID"); VERIFY_ARGUMENTS+=(--expected-team-id "$TEAM_ID"); FORWARD_ARGUMENTS+=(--team-id "$TEAM_ID"); fi
  BUILD_ROOT="$REPO_ROOT/artifacts/macos/$ARCH/$CONFIGURATION"
  APP_PATH="$BUILD_ROOT/JavMetaLite.app"
  WORKER_PUBLISH="$REPO_ROOT/artifacts/worker/osx-$ARCH/$CONFIGURATION"
}
check_signing_identity() {
  # Validate before restore/build; credentials remain in Keychain and never enter a manifest.
  python3 - "$SCRIPT_DIR" "$SIGN_MODE" "$SIGN_IDENTITY" "$TEAM_ID" <<'PY'
import sys
sys.path.insert(0, sys.argv[1])
from sign_bundle import signing_identity
try:
    signing_identity(sys.argv[2], sys.argv[3] or None, sys.argv[4] or None)
except (ValueError, OSError) as error:
    sys.exit('Signing preflight failed: ' + str(error))
PY
}
check_environment() {
  [ "$(uname -s)" = Darwin ] || fail 'macOS is required for the native client and package.'
  command -v python3 >/dev/null || fail 'Python 3 is required for package validation and protocol regression tests.'
  DOTNET="${DOTNET:-$(command -v dotnet || true)}"
  [ -n "$DOTNET" ] && [ -x "$DOTNET" ] || fail 'Install the exact SDK from global.json or set DOTNET to its executable. The scripts never install dependencies.'
  REQUIRED_SDK="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$REPO_ROOT/global.json")"
  ACTUAL_SDK="$(cd "$REPO_ROOT" && "$DOTNET" --version)" || fail "The pinned SDK $REQUIRED_SDK is unavailable."
  [ "$ACTUAL_SDK" = "$REQUIRED_SDK" ] || fail "Expected SDK $REQUIRED_SDK; got $ACTUAL_SDK. global.json is not changed by these scripts."
  /usr/bin/xcrun --find swift >/dev/null 2>&1 || fail 'Install Xcode or Apple Command Line Tools with the macOS SDK.'
  SDK_VERSION="$(/usr/bin/xcrun --sdk macosx --show-sdk-version)"
  [ "${SDK_VERSION%%.*}" -ge 15 ] || fail "macOS 15 SDK or newer required; found $SDK_VERSION."
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
  mkdir -p "$BUILD_ROOT" "$REPO_ROOT/artifacts/test-results"
  echo "Building $ARCH / $CONFIGURATION; .NET SDK $ACTUAL_SDK; macOS SDK $SDK_VERSION"
  /usr/bin/xcrun swift --version
  if /usr/bin/xcodebuild -version >/dev/null 2>&1; then
    /usr/bin/xcodebuild -version
  else
    echo 'Full Xcode is unavailable: using the committed Swift package with Apple Command Line Tools. Xcode UI tests are not claimed.'
  fi
}
