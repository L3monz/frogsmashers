#!/usr/bin/env bash
# Type-check the Unity scripts without opening the editor.
#
# Unity normally compiles on focus, which means a round trip through the GUI just to find
# out whether a change builds. This runs Unity's own Roslyn against the same assemblies the
# editor uses and reports errors directly.
#
# Two things it is NOT: it doesn't run the game, and it doesn't cover Assets/Editor scripts
# (those need UnityEditor.dll and Unity builds them into a separate assembly anyway).
#
# Requires the project to have been opened in the editor at least once, since it references
# Library/ScriptAssemblies for package DLLs.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/2019.2.12f1/Editor/Data"
CSC="$UNITY/Tools/Roslyn/csc.exe"
SCRIPTASM="$PROJECT_ROOT/HohimBrueh/Library/ScriptAssemblies"
OUT="${TMPDIR:-/tmp}/frogsmashers-compile-check"
mkdir -p "$OUT"

[ -x "$CSC" ] || { echo "Unity 2019.2.12f1 Roslyn not found at $CSC"; exit 1; }
[ -d "$SCRIPTASM" ] || { echo "No Library/ScriptAssemblies — open the project in Unity once first."; exit 1; }

RSP="$OUT/compile.rsp"
{
  echo "-target:library"
  # The defines of a Windows player build, so code inside #if UNITY_STANDALONE_WIN is checked too.
  echo "-define:UNITY_STANDALONE_WIN;UNITY_STANDALONE"
  echo "-out:\"$(wslpath -w "$OUT/GameCheck.dll")\""
  # Warnings the existing codebase already trips; we only care about errors here.
  echo "-nowarn:0169,0414,0649,0108,0219,0618,0162,0067,0429"
  echo "-r:\"$(wslpath -w "$UNITY/MonoBleedingEdge/lib/mono/unityjit/mscorlib.dll")\""
  echo "-r:\"$(wslpath -w "$UNITY/MonoBleedingEdge/lib/mono/unityjit/Facades/netstandard.dll")\""
  # Unity references these by default too (Stopwatch, LINQ and others live there).
  echo "-r:\"$(wslpath -w "$UNITY/MonoBleedingEdge/lib/mono/unityjit/System.dll")\""
  echo "-r:\"$(wslpath -w "$UNITY/MonoBleedingEdge/lib/mono/unityjit/System.Core.dll")\""
  for dll in "$UNITY/Managed/UnityEngine/"UnityEngine*.dll; do
    echo "-r:\"$(wslpath -w "$dll")\""
  done
  for name in Unity.InputSystem UnityEngine.UI Unity.TextMeshPro; do
    [ -f "$SCRIPTASM/$name.dll" ] && echo "-r:\"$(wslpath -w "$SCRIPTASM/$name.dll")\""
  done
  for dll in "$PROJECT_ROOT/HohimBrueh/Assets/Plugins/Hindsight/"*.dll; do
    [ -f "$dll" ] && echo "-r:\"$(wslpath -w "$dll")\""
  done
  find "$PROJECT_ROOT/HohimBrueh/Assets" -name '*.cs' -not -path '*/Editor/*' | while read -r f; do
    echo "\"$(wslpath -w "$f")\""
  done
} > "$RSP"

# Capture first, then grep. With pipefail, a pipe from csc into grep takes csc's non-zero exit
# code when there are errors, and the old "if csc | grep" then reported a failed build as clean.
OUTPUT="$("$CSC" -noconfig "@$(wslpath -w "$RSP")" 2>&1 || true)"
if grep -E ": error" <<<"$OUTPUT"; then
  echo "--- compile FAILED ---"
  exit 1
fi
echo "compile clean"

# Determinism guard: simulation code must not read the clock, draw unsynced random
# numbers, call transcendental functions, or read devices directly. See tools/sim-guard.py.
python3 "$PROJECT_ROOT/tools/sim-guard.py"

# State coverage: every field a simulated class assigns must be in its SyncState
# or excluded with a reason. See tools/state-coverage.py.
python3 "$PROJECT_ROOT/tools/state-coverage.py"
