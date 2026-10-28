#!/bin/bash
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
parse_options "$@"
check_environment
cd "$REPO_ROOT"
LOG_DIR="$REPO_ROOT/artifacts/test-results/macos-$ARCH"
mkdir -p "$LOG_DIR"
printf 'suite\texit_code\n' > "$LOG_DIR/results.tsv"
run_logged() {
  local suite="$1"; shift
  set +e
  "$@" 2>&1 | tee "$LOG_DIR/$suite.log"
  local result=${PIPESTATUS[0]}
  set -e
  printf '%s\t%s\n' "$suite" "$result" >> "$LOG_DIR/results.tsv"
  [ "$result" -eq 0 ] || exit "$result"
}
run_logged localization python3 "$SCRIPT_DIR/test-localization.py"
run_logged release-tools python3 "$SCRIPT_DIR/test_release_tools.py"
run_logged release-manifest python3 "$SCRIPT_DIR/test_release_manifest.py"
run_logged portable "$DOTNET" run --project JavMetaLite.PortableTests/JavMetaLite.PortableTests.csproj -c "$CONFIGURATION"
run_logged worker-build "$DOTNET" build JavMetaLite.Worker/JavMetaLite.Worker.csproj -c "$CONFIGURATION"
run_logged search-host-build "$DOTNET" build JavMetaLite.Worker.RegressionHost/JavMetaLite.Worker.RegressionHost.csproj -c "$CONFIGURATION"
WORKER="$REPO_ROOT/JavMetaLite.Worker/bin/$CONFIGURATION/net10.0/JavMetaLite.Worker"
# The development apphost needs this explicit SDK root. Published apps never do.
export DOTNET_ROOT="${DOTNET_ROOT:-$(python3 -c 'import pathlib,sys; print(pathlib.Path(sys.argv[1]).resolve().parent)' "$DOTNET")}"
export JAVMETALITE_TEST_WORKER="$WORKER"
export JAVMETALITE_SEARCH_TEST_HOST="$REPO_ROOT/JavMetaLite.Worker.RegressionHost/bin/$CONFIGURATION/net10.0/JavMetaLite.Worker.RegressionHost"
# CLT Swift 6.4 swiftbuild can omit the installed Testing macro plugin on incremental
# builds. Load that toolchain's plugin explicitly when present; no machine path is stored.
SWIFT_TEST_COMMAND=(/usr/bin/xcrun swift test --package-path "$REPO_ROOT/macos" -c "$SWIFT_CONFIGURATION" --arch "$SWIFT_ARCH")
SWIFT_TOOL_DIRECTORY="$(dirname "$(/usr/bin/xcrun --find swift)")"
TESTING_PLUGIN="$SWIFT_TOOL_DIRECTORY/../lib/swift/host/plugins/testing/libTestingMacros.dylib"
if [ -f "$TESTING_PLUGIN" ]; then
  SWIFT_TEST_COMMAND+=(-Xswiftc -load-plugin-library -Xswiftc "$TESTING_PLUGIN")
fi
run_logged swift "${SWIFT_TEST_COMMAND[@]}"
run_logged xcode-sources python3 "$SCRIPT_DIR/test_xcode_sources.py" --arch "$SWIFT_ARCH"
if [ -f "$SCRIPT_DIR/test_worker.py" ]; then
  run_logged protocol python3 "$SCRIPT_DIR/test_worker.py" "$WORKER" --require-search-host
  run_logged queue-interactions python3 "$SCRIPT_DIR/test_queue_interactions.py" "$WORKER"
  run_logged retention python3 "$SCRIPT_DIR/test_worker_retention.py" "$WORKER"
  run_logged artwork-cache python3 "$SCRIPT_DIR/test_worker_artwork_cache.py" "$WORKER" --require-search-host
fi
printf 'Automated test logs: %s\n' "$LOG_DIR"
echo 'Interactive native UI, IME/VoiceOver, real removable volumes, SMB, live providers and notarization require separate recorded checks.'
