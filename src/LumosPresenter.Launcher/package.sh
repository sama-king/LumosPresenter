#!/usr/bin/env bash
# Builds a LumosCast package for one platform. One script for all three, so the rules that
# make a package work are written down once instead of drifting apart per platform:
#
#   src/LumosPresenter.Launcher/package.sh <rid> [--skip-frontend] [--with-database]
#
#   rid              win-x64 | win-arm64 | osx-arm64 | osx-x64 | linux-x64 | linux-arm64
#   --skip-frontend  publish the console already in WebHost/wwwroot instead of rebuilding it
#   --with-database  ship this machine's data/lumos.db (songs, settings, api.bible key and
#                    all) instead of a clean first-run database — only for your own installs
#
# Output, under artifacts/:
#   Windows  LumosCast-<rid>/          the folder package-windows.iss installs; the installer
#                                      itself too, when Inno Setup's iscc is found
#   macOS    LumosCast.app             plus LumosCast-<version>-<rid>.zip
#   Linux    LumosCast-<rid>/          plus LumosCast-<version>-<rid>.tar.gz
#
# Runs on macOS, Linux, or Git Bash on Windows, and cross-compiles: `dotnet publish -r`
# builds any RID from any host. Two things are host-bound — the Windows installer needs iscc
# (Windows only), and a Linux or macOS package built on Windows loses the executable bit, so
# build those on a Mac or Linux machine.
#
# The translation seeds (WebHost/data/seed) and default backgrounds (assets/backgrounds) are
# committed. The speech models are not — too large — so put the default one (Speech:ModelPath
# in appsettings.json, e.g. models/whisper/ggml-tiny.en.bin) under WebHost/ before packaging.
set -euo pipefail

usage() { sed -n '4,10p' "$0" | sed 's/^# \{0,1\}//'; exit 1; }
log() { echo "==> $*"; }
warn() { echo "warning: $*" >&2; }
die() { echo "error: $*" >&2; exit 1; }

RID=""
BUILD_FRONTEND=yes
WITH_DATABASE=""
for arg in "$@"; do
  case "$arg" in
    --skip-frontend) BUILD_FRONTEND="" ;;
    --with-database) WITH_DATABASE=yes ;;
    -h|--help) usage ;;
    -*) die "unknown option $arg" ;;
    *) RID="$arg" ;;
  esac
done
[ -n "$RID" ] || usage

case "$RID" in
  win-*) OS=windows ;;
  osx-*) OS=macos ;;
  linux-*) OS=linux ;;
  *) die "unsupported RID '$RID'" ;;
esac

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
WEBHOST="$REPO/src/LumosPresenter.WebHost"
ARTIFACTS="$REPO/artifacts"
# The Launcher csproj's <Version> is the one version number; everything else reads it.
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$HERE/LumosPresenter.Launcher.csproj" | head -1)"
[ -n "$VERSION" ] || die "no <Version> in LumosPresenter.Launcher.csproj"

# Both executables go in ONE directory: ServerProcess starts the WebHost it finds beside its
# own executable, so a nested layout leaves the launcher up with no server behind it.
if [ "$OS" = macos ]; then
  APP="$ARTIFACTS/LumosCast.app"
  OUT="$APP/Contents/MacOS"
  rm -rf "$APP"
else
  OUT="$ARTIFACTS/LumosCast-$RID"
  rm -rf "$OUT"
fi
mkdir -p "$OUT"

# --- Console -------------------------------------------------------------------------------
# The WebHost serves the console from wwwroot, and publish copies whatever is there — a stale
# build would ship an older console than the server beside it.
if [ -n "$BUILD_FRONTEND" ]; then
  log "building console (frontend/)"
  (cd "$REPO/frontend" && npm ci --no-audit --no-fund && npm run build)
fi
[ -f "$WEBHOST/wwwroot/index.html" ] || die "WebHost/wwwroot has no console; run without --skip-frontend"

# --- Publish -------------------------------------------------------------------------------
log "publishing $RID (v$VERSION)"
dotnet publish "$HERE/LumosPresenter.Launcher.csproj" -c Release -r "$RID" --self-contained -o "$OUT" --nologo
dotnet publish "$WEBHOST/LumosPresenter.WebHost.csproj" -c Release -r "$RID" --self-contained -o "$OUT" --nologo

# Development-only settings would switch a packaged server to developer behaviour if anyone
# set ASPNETCORE_ENVIRONMENT; they have no business in a package.
rm -f "$OUT/appsettings.Development.json"

# --- Default backgrounds -------------------------------------------------------------------
# The WebHost csproj links assets/backgrounds into publish output as backgrounds/, and
# BundledBackgroundSeeder registers each file on first start. Check the whole set arrived:
# a package without them opens to an empty background picker and nothing says why.
expected="$(find "$REPO/assets/backgrounds" -maxdepth 1 -type f \
  \( -name '*.jpg' -o -name '*.jpeg' -o -name '*.png' -o -name '*.webp' -o -name '*.gif' \
     -o -name '*.mp4' -o -name '*.webm' -o -name '*.mov' \) | wc -l | tr -d ' ')"
shipped="$(find "$OUT/backgrounds" -maxdepth 1 -type f 2>/dev/null | wc -l | tr -d ' ')"
[ "$shipped" = "$expected" ] || die "shipped $shipped of $expected default backgrounds"
log "default backgrounds: $shipped"

# --- Speech models -------------------------------------------------------------------------
# Content, not build output, so publish does not carry them. Two Whisper models ship: the
# console switches model without a rebuild, so the one an operator picks must already be
# here. tiny.en is the default (it runs on the weakest machine we support) and base.en the
# step up; small.en and medium.en are ~2 GB together and stay out.
DEFAULT_MODEL="$(sed -n 's/.*"ModelPath": "\(.*\)".*/\1/p' "$WEBHOST/appsettings.json" | head -1)"
[ -n "$DEFAULT_MODEL" ] || die "no Speech ModelPath in appsettings.json"
for model in "$DEFAULT_MODEL" models/whisper/ggml-base.en.bin; do
  [ -f "$OUT/$model" ] && continue # the default may already be base.en
  if [ ! -f "$WEBHOST/$model" ]; then
    # The default is loaded at startup; without it listening never starts.
    [ "$model" = "$DEFAULT_MODEL" ] && die "default speech model '$model' not found in WebHost/"
    warn "speech model '$model' not found; operators will not be able to switch to it"
    continue
  fi
  mkdir -p "$OUT/$(dirname "$model")"
  cp "$WEBHOST/$model" "$OUT/$model"
  # Whisper.net's CoreML path looks for a compiled encoder beside the model on macOS.
  encoder="${model%.bin}-encoder.mlmodelc"
  if [ "$OS" = macos ] && [ -d "$WEBHOST/$encoder" ]; then
    cp -R "$WEBHOST/$encoder" "$OUT/$encoder"
  fi
  log "speech model: $model"
done
# sherpa-onnx is the alternative engine and small enough to ship.
if [ -d "$WEBHOST/models/sherpa-onnx" ]; then
  mkdir -p "$OUT/models"
  cp -R "$WEBHOST/models/sherpa-onnx" "$OUT/models/"
  log "speech model: sherpa-onnx"
fi

# --- Natives -------------------------------------------------------------------------------
# Whisper.net and friends ship every platform's natives and a RID publish does not prune
# them. Plain RID folders (runtimes/linux-arm, runtimes/osx-x64…) go unless they are this
# build's; variant folders (runtimes/vulkan, runtimes/coreml) keep only this build's
# subfolder, so Windows and Linux keep their Vulkan GPU path and macOS its CoreML one.
# Whisper.net names macOS folders macos-*, not osx-*.
case "$OS" in
  windows) KEEP="$RID win" ;;
  macos) KEEP="$RID ${RID/osx-/macos-} osx unix" ;;
  linux) KEEP="$RID linux unix" ;;
esac
keep() { case " $KEEP " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }
is_platform() {
  case "$1" in
    *-*|win|osx|linux|unix|android|ios|browser|maccatalyst|tvos|freebsd|illumos|solaris) return 0 ;;
    *) return 1 ;;
  esac
}
if [ -d "$OUT/runtimes" ]; then
  for dir in "$OUT/runtimes"/*/; do
    name="$(basename "$dir")"
    keep "$name" && continue
    if is_platform "$name"; then
      rm -rf "$dir"
      continue
    fi
    for sub in "$dir"*/; do
      [ -d "$sub" ] || continue
      keep "$(basename "$sub")" || rm -rf "$sub"
    done
    rmdir "$dir" 2>/dev/null || true
  done
  log "natives kept: $(cd "$OUT/runtimes" && find . -mindepth 1 -maxdepth 2 -type d | sed 's:^\./::' | sort | tr '\n' ' ')"
fi

# --- Database ------------------------------------------------------------------------------
# By default a package carries the translation seed files, and DatabaseInitializer builds a
# clean database from them on first start. The build machine's own database is the operator's
# library — songs, settings, the api.bible key, gallery paths on this disk — and is opt-in.
mkdir -p "$OUT/data/seed"
seeds=0
for seed in "$WEBHOST"/data/seed/*.db; do
  [ -f "$seed" ] || continue
  cp "$seed" "$OUT/data/seed/"
  seeds=$((seeds + 1))
done
[ "$seeds" -gt 0 ] || die "no translation seeds in WebHost/data/seed (they are committed; restore them from git)"
log "translation seeds: $seeds"

if [ -n "$WITH_DATABASE" ]; then
  SRC_DB="$WEBHOST/data/lumos.db"
  [ -f "$SRC_DB" ] || die "--with-database: no $SRC_DB"
  command -v sqlite3 >/dev/null || die "--with-database needs sqlite3 (a plain copy can tear a live WAL)"
  sqlite3 "$SRC_DB" ".backup '$OUT/data/lumos.db'"
  warn "shipping this machine's database, including its songs and api.bible key"
fi

# --- Platform finish -----------------------------------------------------------------------
case "$OS" in
  macos)
    # The dock icon comes only from the bundle's CFBundleIconFile — no runtime API sets it.
    mkdir -p "$APP/Contents/Resources"
    cp "$HERE/Info.plist" "$APP/Contents/Info.plist"
    cp "$HERE/Assets/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
    if [ -x /usr/libexec/PlistBuddy ]; then
      /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" \
        -c "Set :CFBundleVersion $VERSION" "$APP/Contents/Info.plist"
    fi
    # Finder and the Dock cache icons aggressively; touching the bundle invalidates it.
    touch "$APP"
    ARCHIVE="$ARTIFACTS/LumosCast-$VERSION-$RID.zip"
    rm -f "$ARCHIVE"
    if command -v ditto >/dev/null; then
      ditto -c -k --keepParent "$APP" "$ARCHIVE"
      log "archive: $ARCHIVE"
    fi
    log "built $APP ($(du -sh "$APP" | cut -f1))"
    ;;
  linux)
    # A desktop entry and icon for whoever installs it; Exec is relative to wherever the
    # folder is unpacked, so the installer (or the operator) points it at the real path.
    cp "$HERE/Assets/logo.png" "$OUT/lumoscast.png"
    cat > "$OUT/lumoscast.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=LumosCast
Comment=Live scripture and lyrics projection
Exec=LumosPresenter.Launcher
Icon=lumoscast
Categories=AudioVideo;Presentation;
Terminal=false
EOF
    ARCHIVE="$ARTIFACTS/LumosCast-$VERSION-$RID.tar.gz"
    tar -czf "$ARCHIVE" -C "$ARTIFACTS" "LumosCast-$RID"
    log "archive: $ARCHIVE"
    log "built $OUT ($(du -sh "$OUT" | cut -f1))"
    echo "note: Add from Disk uses the system file dialog through zenity or kdialog"
    ;;
  windows)
    log "built $OUT ($(du -sh "$OUT" | cut -f1))"
    ISCC="$(command -v iscc || command -v ISCC.exe || true)"
    for candidate in "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" "/c/Program Files/Inno Setup 6/ISCC.exe"; do
      [ -z "$ISCC" ] && [ -x "$candidate" ] && ISCC="$candidate"
    done
    if [ -n "$ISCC" ]; then
      "$ISCC" -Q "-DAppVersion=$VERSION" "-DRid=$RID" "$HERE/package-windows.iss"
      log "installer: $ARTIFACTS/LumosCast-$VERSION-$RID-setup.exe"
    else
      echo "installer: on Windows with Inno Setup 6, run"
      echo "  iscc /DAppVersion=$VERSION /DRid=$RID src\\LumosPresenter.Launcher\\package-windows.iss"
    fi
    ;;
esac
