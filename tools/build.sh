#!/usr/bin/env bash
# Build the Windows player from the command line, with IL2CPP.
#
# Usage: tools/build.sh dev        -> builds/dev/FrogSmashers.exe      (development build)
#        tools/build.sh release    -> builds/release/FrogSmashers.exe  (what players get)
#
# Close Unity first: the editor locks the project. The first IL2CPP build takes several minutes.
# Every build carries a build ID: the short commit hash, plus "-dirty" when tracked files have
# uncommitted changes. Online play refuses to connect two different builds.
# The running build writes its log, including the [SIM] diagnostics, to
#   C:\Users\<you>\AppData\LocalLow\DefaultCompany\Free Lives Template\Player.log
# Read it with: tools/sim-log-report.py player
set -euo pipefail

KIND="${1:-dev}"
case "$KIND" in
  dev) METHOD=BuildScript.BuildDevelopmentWindows ;;
  release) METHOD=BuildScript.BuildReleaseWindows ;;
  *) echo "Usage: tools/build.sh dev|release"; exit 2 ;;
esac

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$PROJECT_ROOT/HohimBrueh"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/2019.2.12f1/Editor/Unity.exe"
LOG="$PROJECT/Logs/build-$KIND.log"

if /mnt/c/Windows/System32/tasklist.exe /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
  echo "Unity is running. Close it, then run this again."
  exit 1
fi
mkdir -p "$PROJECT/Logs"

BUILD_ID="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
git -C "$PROJECT_ROOT" diff --quiet HEAD -- || BUILD_ID="$BUILD_ID-dirty"
echo "Building $KIND, build ID $BUILD_ID"

status=0
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$(wslpath -w "$PROJECT")" \
  -executeMethod "$METHOD" -buildId "$BUILD_ID" \
  -logFile "$(wslpath -w "$LOG")" || status=$?

grep -a "\[BUILD\]" "$LOG" | tail -1 || true
if [ "$status" -ne 0 ]; then
  echo "Build failed (exit $status). Errors from $LOG:"
  grep -a -E "error CS|Error building|BuildFailedException|IL2CPP error" "$LOG" | head -20 || true
  exit 1
fi
echo "Built: $PROJECT_ROOT/builds/$KIND/FrogSmashers.exe"
