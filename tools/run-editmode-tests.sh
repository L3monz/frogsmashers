#!/usr/bin/env bash
# Run the EditMode tests (Assets/Editor/Tests) in batch mode, without opening the editor.
#
# Usage: tools/run-editmode-tests.sh [test name filter]
#
# Unity locks the project while the editor is open, so close it first. The first run
# after a change also imports and compiles, which takes a minute or two.
# Results: HohimBrueh/Logs/editmode-results.xml, log: HohimBrueh/Logs/editmode-tests.log
# (both gitignored).
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$PROJECT_ROOT/HohimBrueh"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/2019.2.12f1/Editor/Unity.exe"
RESULTS="$PROJECT/Logs/editmode-results.xml"
LOG="$PROJECT/Logs/editmode-tests.log"

[ -x "$UNITY" ] || { echo "Unity 2019.2.12f1 not found at $UNITY"; exit 1; }
if /mnt/c/Windows/System32/tasklist.exe /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
  echo "Unity is running. Close it, then run this again."
  exit 1
fi

mkdir -p "$PROJECT/Logs"
rm -f "$RESULTS"

FILTER=()
[ $# -gt 0 ] && FILTER=(-testFilter "$1")

# -runTests quits by itself when the tests finish; do not add -quit.
"$UNITY" -batchmode -nographics \
  -projectPath "$(wslpath -w "$PROJECT")" \
  -runTests -testPlatform EditMode \
  -testResults "$(wslpath -w "$RESULTS")" \
  -logFile "$(wslpath -w "$LOG")" \
  "${FILTER[@]}" || true

if [ ! -f "$RESULTS" ]; then
  echo "No results file. Compile errors, or the run did not start. See $LOG:"
  grep -E "error CS|Exception|Aborting" "$LOG" | head -20 || true
  exit 1
fi

python3 - "$RESULTS" <<'EOF'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
cases = root.iter("test-case")
failed = 0
total = 0
for case in cases:
    total += 1
    result = case.get("result")
    if result != "Passed":
        failed += 1
        message = case.find("failure/message")
        text = (message.text or "").strip() if message is not None else ""
        print(f"FAILED {case.get('fullname')}\n    {text[:400]}")
print(f"editmode tests: {total} run, {failed} failed -- " + ("FAILED" if failed else "ok"))
sys.exit(1 if failed or total == 0 else 0)
EOF
