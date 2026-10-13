#!/usr/bin/env bash
# Build Hootenanny's protocol library in Release and copy it into the Unity project.
#
# Usage: tools/update-hootenanny.sh [path/to/hootenanny]
#
# The game talks to the Hootenanny rendezvous and relay server with the same message code
# the server uses: Hootenanny.Protocol, one netstandard2.0 DLL in Assets/Plugins/Hootenanny/.
# The default Hootenanny path is ../hootenanny, the folder next to this repository's.
#
# Close Unity first, for the same reason as tools/update-hindsight.sh.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOOTENANNY="${1:-$PROJECT_ROOT/../hootenanny}"
PLUGINS="$PROJECT_ROOT/HohimBrueh/Assets/Plugins/Hootenanny"
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
NAME=Hootenanny.Protocol

[ -d "$HOOTENANNY" ] || { echo "Hootenanny not found at $HOOTENANNY"; exit 1; }
[ -x "$DOTNET" ] || { echo "dotnet not found at $DOTNET"; exit 1; }

if /mnt/c/Windows/System32/tasklist.exe /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -qi "Unity.exe"; then
  echo "Unity is running. Close it, then run this again."
  exit 1
fi

# Only the protocol library. The server and its tests target net8.0, which the Windows
# .NET 6 SDK can't build.
"$DOTNET" build "$(wslpath -w "$HOOTENANNY/src/$NAME/$NAME.csproj")" -c Release --nologo -v quiet

src="$HOOTENANNY/src/$NAME/bin/Release/netstandard2.0/$NAME.dll"
[ -f "$src" ] || { echo "Build output missing: $src"; exit 1; }
mkdir -p "$PLUGINS"
cp "$src" "$PLUGINS/$NAME.dll"
echo "updated $NAME.dll ($(git -C "$HOOTENANNY" rev-parse --short HEAD))"
