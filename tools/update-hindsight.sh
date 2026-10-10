#!/usr/bin/env bash
# Build Hindsight in Release and copy its DLLs into the Unity project.
#
# Usage: tools/update-hindsight.sh [path/to/hindsight]
#
# Frog Smashers uses Hindsight as two compiled DLLs in Assets/Plugins/Hindsight/. This
# replaces the manual build-and-copy. The default Hindsight path is the sibling folder
# C:\gamedev-projects\hindsight.
#
# Close Unity first. Unity reloads plugins when it gets focus, and replacing a DLL while
# the editor is in Play mode reloads the scripts in the middle of play.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HINDSIGHT="${1:-$PROJECT_ROOT/../hindsight}"
PLUGINS="$PROJECT_ROOT/HohimBrueh/Assets/Plugins/Hindsight"
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"

[ -d "$HINDSIGHT" ] || { echo "Hindsight not found at $HINDSIGHT"; exit 1; }
[ -x "$DOTNET" ] || { echo "dotnet not found at $DOTNET"; exit 1; }

if /mnt/c/Windows/System32/tasklist.exe /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
  echo "Unity is running. Close it, then run this again."
  exit 1
fi

"$DOTNET" build "$(wslpath -w "$HINDSIGHT/Hindsight.sln")" -c Release --nologo -v quiet

for name in Hindsight.Core Hindsight.Determinism; do
  src="$HINDSIGHT/src/$name/bin/Release/netstandard2.0/$name.dll"
  [ -f "$src" ] || { echo "Build output missing: $src"; exit 1; }
  cp "$src" "$PLUGINS/$name.dll"
  echo "updated $name.dll"
done
