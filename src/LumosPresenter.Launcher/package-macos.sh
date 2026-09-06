#!/usr/bin/env bash
# Assembles LumosPresenter.app so macOS shows the dock icon and the app name.
#
# The dock icon comes from the bundle's CFBundleIconFile — there is no runtime API for
# it — so `dotnet run` on the launcher will always show the generic .NET icon. Only the
# bundle produced here carries the brand.
#
# ServerProcess looks for the WebHost executable beside its own, so both projects publish
# into the same Contents/MacOS directory.
#
#   src/LumosPresenter.Launcher/package-macos.sh [osx-arm64|osx-x64]
set -euo pipefail

RID="${1:-osx-arm64}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
APP="$REPO/artifacts/LumosPresenter.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$HERE/LumosPresenter.Launcher.csproj" -c Release -r "$RID" --self-contained \
  -o "$APP/Contents/MacOS"
dotnet publish "$REPO/src/LumosPresenter.WebHost/LumosPresenter.WebHost.csproj" -c Release -r "$RID" \
  --self-contained -o "$APP/Contents/MacOS"

cp "$HERE/Info.plist" "$APP/Contents/Info.plist"
cp "$HERE/Assets/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"

# The speech models are content, not build output, so publish does not carry them. The
# server resolves them relative to its content root — which ServerProcess anchors to the
# bundle — so without this copy the engine has nothing to load and listening never starts.
if [ -d "$REPO/src/LumosPresenter.WebHost/models" ]; then
  rsync -a "$REPO/src/LumosPresenter.WebHost/models/" "$APP/Contents/MacOS/models/"
  echo "copied speech models"
else
  echo "warning: no models/ directory — listening will not start" >&2
fi

# Likewise the verse database: a bundle without it starts on an empty schema and shows
# only the remote translations. Copied via sqlite3 so a live WAL cannot tear the copy.
SRC_DB="$REPO/src/LumosPresenter.WebHost/data/lumos.db"
if [ -f "$SRC_DB" ]; then
  mkdir -p "$APP/Contents/MacOS/data"
  sqlite3 "$SRC_DB" ".backup '$APP/Contents/MacOS/data/lumos.db'"
  echo "copied verse database"
fi

# Finder and the Dock cache icons aggressively; touching the bundle invalidates it.
touch "$APP"

echo "built $APP"
