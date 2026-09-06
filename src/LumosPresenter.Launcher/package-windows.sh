#!/usr/bin/env bash
# Assembles the Windows payload: the folder an installer lays down, or that can be
# copied to a PC and run as-is.
#
# This mirrors package-macos.sh. The one structural rule is that both projects publish
# into the SAME directory: ServerProcess looks for LumosPresenter.WebHost.exe next to its
# own executable (AppContext.BaseDirectory), so a nested bin/ layout breaks server start
# with no error beyond the launcher quietly never coming up.
#
# The .exe icon comes from <ApplicationIcon> in the launcher csproj, so unlike macOS
# there is no separate icon step here — the published .exe already carries the brand.
#
# Runs on macOS or Linux: `dotnet publish -r win-x64` cross-compiles fine. What it cannot
# do off Windows is compile the installer — see package-windows.iss.
#
#   src/LumosPresenter.Launcher/package-windows.sh [win-x64|win-arm64]
set -euo pipefail

RID="${1:-win-x64}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
OUT="$REPO/artifacts/LumosCast-$RID"

rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish "$HERE/LumosPresenter.Launcher.csproj" -c Release -r "$RID" --self-contained -o "$OUT"
dotnet publish "$REPO/src/LumosPresenter.WebHost/LumosPresenter.WebHost.csproj" -c Release -r "$RID" \
  --self-contained -o "$OUT"

# Speech models are content, not build output, so publish does not carry them; without
# them the engine has nothing to load and listening never starts. Only the model the
# config actually names is shipped: models/ on a dev machine holds several GB of spare
# checkpoints, and the CoreML .mlmodelc bundles are macOS-only.
MODEL="$(sed -n 's/.*"ModelPath": "\(.*\)".*/\1/p' "$REPO/src/LumosPresenter.WebHost/appsettings.json" | head -1)"
if [ -n "$MODEL" ] && [ -f "$REPO/src/LumosPresenter.WebHost/$MODEL" ]; then
  mkdir -p "$OUT/$(dirname "$MODEL")"
  cp "$REPO/src/LumosPresenter.WebHost/$MODEL" "$OUT/$MODEL"
  echo "copied speech model: $MODEL ($(du -h "$OUT/$MODEL" | cut -f1))"
else
  echo "warning: speech model '$MODEL' not found — listening will not start" >&2
fi

# The sherpa-onnx engine is the alternative Speech:Engine, and is small enough to ship
# so switching engines does not need a re-download.
if [ -d "$REPO/src/LumosPresenter.WebHost/models/sherpa-onnx" ]; then
  rsync -a "$REPO/src/LumosPresenter.WebHost/models/sherpa-onnx/" "$OUT/models/sherpa-onnx/"
  echo "copied sherpa-onnx model"
fi

# Whisper.net.Runtime ships every platform's natives in one package and a RID publish
# does not prune them, so the payload arrives with macOS/Linux/CoreML .dylib and .so files
# that can never load on Windows. Drop every runtimes/ RID but this build's and the
# architecture-neutral ones.
if [ -d "$OUT/runtimes" ]; then
  for dir in "$OUT/runtimes"/*/; do
    name="$(basename "$dir")"
    case "$name" in
      "$RID"|win) ;;
      *) rm -rf "$dir" ;;
    esac
  done
  echo "pruned foreign-platform natives (kept runtimes/$RID)"
fi

# Likewise the verse database: without it the app starts on an empty schema and shows
# only the remote translations. Copied via sqlite3 so a live WAL cannot tear the copy.
SRC_DB="$REPO/src/LumosPresenter.WebHost/data/lumos.db"
if [ -f "$SRC_DB" ]; then
  mkdir -p "$OUT/data"
  sqlite3 "$SRC_DB" ".backup '$OUT/data/lumos.db'"
  echo "copied verse database"
fi

echo "built $OUT ($(du -sh "$OUT" | cut -f1))"
echo "installer: compile package-windows.iss with Inno Setup on Windows"
