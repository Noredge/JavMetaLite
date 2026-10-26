#!/bin/bash
# Compile-only compatibility check. Windows runtime gates remain PowerShell tasks.
set -euo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
CONFIGURATION=Release
parse_options "$@"
check_environment
cd "$REPO_ROOT"
LOG_DIRECTORY="$REPO_ROOT/artifacts/test-results"
mkdir -p "$LOG_DIRECTORY"
LOG="$LOG_DIRECTORY/windows-crossbuild.log"
RESULTS="$LOG_DIRECTORY/windows-crossbuild-results.tsv"
: > "$LOG"
printf 'project\texit_code\n' > "$RESULTS"
for project in JavMetaLite.App JavMetaLite.SmokeTests JavMetaLite.RegressionTests JavMetaLite.UiSmokeTests; do
  printf 'Compile-only: %s (%s)\n' "$project" "$CONFIGURATION" | tee -a "$LOG"
  set +e
  "$DOTNET" build "$project/$project.csproj" -c "$CONFIGURATION" -p:EnableWindowsTargeting=true 2>&1 | tee -a "$LOG"
  result=${PIPESTATUS[0]}
  set -e
  printf '%s\t%s\n' "$project" "$result" >> "$RESULTS"
  printf 'EXIT_CODE: %s\n' "$result" | tee -a "$LOG"
  [ "$result" -eq 0 ] || exit "$result"
done
printf 'Windows cross-compilation passed. No Windows runtime test was executed.\n' | tee -a "$LOG"
