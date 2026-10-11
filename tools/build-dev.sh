#!/usr/bin/env bash
# Make a Windows development build from the command line: builds/dev/FrogSmashers.exe.
#
# Usage: tools/build-dev.sh
#
# Close Unity first: the editor locks the project. The build uses the project's scripting
# backend (IL2CPP), so the first build takes several minutes.
# The running build writes its log, including the [SIM] diagnostics, to
#   C:\Users\<you>\AppData\LocalLow\DefaultCompany\Free Lives Template\Player.log
# Read it with: tools/sim-log-report.py player
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$PROJECT_ROOT/HohimBrueh"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/2019.2.12f1/Editor/Unity.exe"
LOG="$PROJECT/Logs/build-dev.log"

if /mnt/c/Windows/System32/tasklist.exe /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
  echo "Unity is running. Close it, then run this again."
  exit 1
fi
mkdir -p "$PROJECT/Logs"

status=0
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$(wslpath -w "$PROJECT")" \
  -executeMethod BuildScript.BuildDevelopmentWindows \
  -logFile "$(wslpath -w "$LOG")" || status=$?

grep -a "\[BUILD\]" "$LOG" | tail -1 || true
if [ "$status" -ne 0 ]; then
  echo "Build failed (exit $status). Errors from $LOG:"
  grep -a -E "error CS|Error building|BuildFailedException|IL2CPP error" "$LOG" | head -20 || true
  exit 1
fi
echo "Built: $PROJECT_ROOT/builds/dev/FrogSmashers.exe"
