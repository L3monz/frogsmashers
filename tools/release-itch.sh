#!/usr/bin/env bash
# Build the release player and push it to itch.io with butler.
#
# Usage: tools/release-itch.sh VERSION [--dry-run]      e.g. tools/release-itch.sh 0.1.0
#
# Refuses to run with uncommitted changes to tracked files, so the build ID on the title
# screen names a commit anyone can check out. After pushing, it prints the git tag to create.
# butler must be logged in (~/.config/itch/butler_creds); the itch page stays Restricted, so
# players need a download key.
set -euo pipefail

VERSION="${1:-}"
DRY_RUN="${2:-}"
[ -n "$VERSION" ] || { echo "Usage: tools/release-itch.sh VERSION [--dry-run]"; exit 2; }

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="blemonz/frog-smashers-online:windows"
BUTLER="${BUTLER:-$HOME/.local/bin/butler}"

[ -x "$BUTLER" ] || { echo "butler not found at $BUTLER"; exit 1; }
if ! git -C "$PROJECT_ROOT" diff --quiet HEAD --; then
  echo "Tracked files have uncommitted changes. Commit first, so the build matches a commit."
  exit 1
fi

BUILD="$PROJECT_ROOT/builds/release"
# A clean folder, so files left over from an older build never get published.
rm -rf "$BUILD"
"$PROJECT_ROOT/tools/build.sh" release

for notice in LICENSE.md THIRD-PARTY-NOTICES.md; do
  [ -f "$BUILD/$notice" ] || { echo "$notice is missing from the build; refusing to publish."; exit 1; }
done

# IL2CPP writes debug symbols to *_BackUpThisFolder_ButDontShipItWithYourGame next to the
# exe. As the name says, they stay out of what players download.
PUSH=("$BUTLER" push "$BUILD" "$TARGET" --userversion "$VERSION" --ignore "*_BackUpThisFolder_ButDontShipItWithYourGame")
[ "$DRY_RUN" = "--dry-run" ] && PUSH+=(--dry-run)
"${PUSH[@]}"

COMMIT="$(git -C "$PROJECT_ROOT" rev-parse --short HEAD)"
echo
echo "Pushed $VERSION ($COMMIT) to $TARGET."
echo "Tag it:  git tag v$VERSION $COMMIT && git push origin v$VERSION"
