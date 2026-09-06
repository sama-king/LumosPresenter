#!/usr/bin/env python3
"""Derives every shipped icon from the brand art in ``source/``.

The source renders are flat RGB with no alpha: the mark sits on a field of pure
white or pure black. Two operations get us from there to usable assets.

*Unmatte* recovers real transparency. Art composited over black is premultiplied,
so ``alpha = max(R,G,B)`` inverts it exactly, antialiased edges included; over
white the mirror ``alpha = 255 - min(R,G,B)`` does the same. This is why a naive
colour-key fails here — the beam fades to near-white in the light renders and to
near-black in the dark ones, so any threshold punches holes through it. The same
property means an unmatted mark is only faithful on the ground it was drawn for,
which is why the light and dark variants both ship.

*Black-point lift* builds the opaque icon tiles. Mapping 0 to the brand navy and
leaving 255 alone recolours the field without touching the mark, and the beam
melting into navy is exactly what the brand sheet's app icon shows.

Run from anywhere; writes into this directory's ``out/`` plus the app trees:

    python3 assets/brand/generate.py
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

# Sampled from the app-icon tile on brand-sheet.png.
NAVY = (4, 28, 62)

# The launcher window paints its Stop-listening button this red; the tray badge
# matches it so the two surfaces read as the same state.
LISTENING = (240, 68, 56)

# Apple draws macOS icons inside a 824/1024 box, leaving the rest as margin;
# favicons and Windows icons are full-bleed instead.
MACOS_TILE = 824 / 1024
MACOS_RADIUS = 0.225  # of the tile
FLAT_RADIUS = 0.2237  # of the canvas

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "source"
OUT = ROOT / "out"
REPO = ROOT.parent.parent
LAUNCHER_ASSETS = REPO / "src" / "LumosPresenter.Launcher" / "Assets"
FRONTEND_PUBLIC = REPO / "frontend" / "public"


def unmatte(path: Path, ground: str) -> Image.Image:
    """Recovers the RGBA art from a render flattened onto white or black."""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.int32)
    if ground == "black":
        alpha = rgb.max(axis=2)
        straight = rgb * 255
    else:
        alpha = 255 - rgb.min(axis=2)
        straight = (rgb - (255 - alpha)[..., None]) * 255
    safe = np.maximum(alpha, 1)[..., None]
    out = np.zeros((*alpha.shape, 4), dtype=np.uint8)
    out[..., :3] = np.clip(straight // safe, 0, 255)
    out[..., 3] = alpha
    return Image.fromarray(out, "RGBA")


def lift_black_point(path: Path, navy: tuple[int, int, int]) -> Image.Image:
    """Recolours the black field to navy, leaving white at white."""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float64)
    base = np.array(navy, dtype=np.float64)
    out = base + rgb * (1.0 - base / 255.0)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")


def trim(image: Image.Image, pad: int = 0) -> Image.Image:
    """Crops to the visible art, optionally leaving a margin."""
    box = image.getbbox()
    if box is None:
        return image
    left, top, right, bottom = box
    return image.crop(
        (
            max(0, left - pad),
            max(0, top - pad),
            min(image.width, right + pad),
            min(image.height, bottom + pad),
        )
    )


def content_box(path: Path, ground: str) -> tuple[int, int, int, int]:
    """The mark's bounding box in a flattened render."""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.int32)
    mask = rgb.max(axis=2) > 6 if ground == "black" else rgb.min(axis=2) < 250
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def rounded_mask(size: int, radius: float, inset: int = 0) -> Image.Image:
    """An antialiased rounded-square alpha mask, drawn 4x and downsampled."""
    scale = 4
    mask = Image.new("L", (size * scale, size * scale), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (inset * scale, inset * scale, (size - inset) * scale - 1, (size - inset) * scale - 1),
        radius=radius * scale,
        fill=255,
    )
    return mask.resize((size, size), Image.LANCZOS)


def tile(size: int, *, macos: bool, mark_scale: float, rounded: bool = True) -> Image.Image:
    """A navy app-icon tile with the lifted mark centred on it.

    ``mark_scale`` is the mark's height as a fraction of the tile, and is raised
    for the small sizes: at 16px the ring and star otherwise dissolve.
    """
    lifted = lift_black_point(SOURCE / "mark-dark-colour.png", NAVY)
    mark = lifted.crop(content_box(SOURCE / "mark-dark-colour.png", "black"))

    inset = round(size * (1 - MACOS_TILE) / 2) if macos else 0
    tile_size = size - inset * 2
    radius = tile_size * MACOS_RADIUS if macos else size * FLAT_RADIUS

    target_h = max(1, round(tile_size * mark_scale))
    target_w = max(1, round(mark.width * target_h / mark.height))
    mark = mark.resize((target_w, target_h), Image.LANCZOS)

    canvas = Image.new("RGB", (size, size), NAVY)
    canvas.paste(mark, ((size - target_w) // 2, (size - target_h) // 2))

    out = canvas.convert("RGBA")
    if rounded:
        out.putalpha(rounded_mask(size, radius, inset))
    return out


def badged(tile_image: Image.Image) -> Image.Image:
    """The same tray tile with a listening dot in the lower-right corner.

    Windows has no badge API for a notification-area icon — the only lever is
    swapping the icon itself — so "listening" is drawn into the art. The dot is
    deliberately large (a third of the canvas) with a light ring around it:
    at 16px in a taskbar anything subtler simply disappears, and the ring keeps
    it legible where the red would otherwise sit on the navy tile's edge.
    """
    out = tile_image.copy()
    size = out.width
    diameter = size * 0.36
    ring = max(1.0, size * 0.055)
    right, bottom = size - size * 0.06, size - size * 0.06
    box = (right - diameter, bottom - diameter, right, bottom)

    # Draw supersampled: these tiles go down to 16px, where a directly-drawn
    # circle is visibly polygonal.
    scale = 8
    layer = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    draw.ellipse([v * scale for v in box], fill=LISTENING + (255,),
                 outline=(255, 255, 255, 255), width=round(ring * scale))
    layer = layer.resize((size, size), Image.LANCZOS)
    out.alpha_composite(layer)
    return out


def mark_scale_for(size: int) -> float:
    if size <= 24:
        return 0.80
    if size <= 48:
        return 0.72
    return 0.62


def write(image: Image.Image, *paths: Path) -> None:
    for path in paths:
        path.parent.mkdir(parents=True, exist_ok=True)
        image.save(path)
        print(f"  {path.relative_to(REPO)}")


def build_icns(destination: Path) -> None:
    """macOS bundles read .icns; iconutil insists on Apple's exact filenames."""
    if not shutil.which("iconutil"):
        print("  (skipped .icns — iconutil is macOS-only)")
        return
    iconset = OUT / "AppIcon.iconset"
    if iconset.exists():
        shutil.rmtree(iconset)
    iconset.mkdir(parents=True)
    for base in (16, 32, 128, 256, 512):
        tile(base, macos=True, mark_scale=mark_scale_for(base)).save(
            iconset / f"icon_{base}x{base}.png"
        )
        tile(base * 2, macos=True, mark_scale=mark_scale_for(base * 2)).save(
            iconset / f"icon_{base}x{base}@2x.png"
        )
    subprocess.run(
        ["iconutil", "-c", "icns", str(iconset), "-o", str(destination)], check=True
    )
    shutil.rmtree(iconset)
    print(f"  {destination.relative_to(REPO)}")


def main() -> int:
    if not SOURCE.exists():
        print(f"missing {SOURCE}", file=sys.stderr)
        return 1
    OUT.mkdir(exist_ok=True)

    print("transparent art")
    mark_on_light = trim(unmatte(SOURCE / "mark-light-colour.png", "white"), pad=12)
    mark_on_dark = trim(unmatte(SOURCE / "mark-dark-colour.png", "black"), pad=12)
    lockup_on_light = trim(unmatte(SOURCE / "lockup-light-colour.png", "white"), pad=12)
    lockup_on_dark = trim(unmatte(SOURCE / "lockup-dark-colour.png", "black"), pad=12)

    def scaled(image: Image.Image, height: int) -> Image.Image:
        width = round(image.width * height / image.height)
        return image.resize((width, height), Image.LANCZOS)

    write(scaled(mark_on_light, 512), OUT / "mark-on-light.png")
    write(scaled(mark_on_dark, 512), OUT / "mark-on-dark.png")
    write(scaled(lockup_on_light, 320), OUT / "lockup-on-light.png")
    write(scaled(lockup_on_dark, 320), OUT / "lockup-on-dark.png")

    print("icon tiles")
    write(tile(1024, macos=False, mark_scale=0.62), OUT / "icon.png")
    write(tile(1024, macos=True, mark_scale=0.62), OUT / "icon-macos.png")

    print("launcher")
    # The launcher window is light (#fbfbfd), so its header takes the dark-ink mark.
    write(scaled(mark_on_light, 256), LAUNCHER_ASSETS / "logo.png")
    # The window header shows the full lockup rather than the mark plus a typed
    # wordmark, so the brand's own letterforms are what an operator sees.
    # Both grounds ship: the window follows the OS theme, and an unmatted lockup is only
    # faithful on the ground it was drawn for — the dark-ink wordmark disappears against
    # a dark window.
    write(scaled(lockup_on_light, 320), LAUNCHER_ASSETS / "lockup.png")
    write(scaled(lockup_on_dark, 320), LAUNCHER_ASSETS / "lockup-dark.png")
    # Avalonia gives macOS tray icons no template treatment, so a one-colour mark
    # would vanish against one menu-bar appearance or the other. The navy tile reads
    # on both. Each size also ships a badged twin for the listening state.
    for size in (32, 64, 128):
        base = tile(size, macos=False, mark_scale=mark_scale_for(size))
        write(base, LAUNCHER_ASSETS / f"tray-{size}.png")
        write(badged(base), LAUNCHER_ASSETS / f"tray-active-{size}.png")
    ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    tile(1024, macos=False, mark_scale=0.62).save(LAUNCHER_ASSETS / "app.ico", sizes=ico_sizes)
    print(f"  {(LAUNCHER_ASSETS / 'app.ico').relative_to(REPO)}")
    # Windows tray icons are .ico; the badged twin is what the launcher swaps to
    # while listening. Badged at 256 so the dot survives Pillow's downscaling to
    # the smaller frames it packs into the file.
    badged(tile(256, macos=False, mark_scale=0.62)).save(
        LAUNCHER_ASSETS / "app-active.ico", sizes=ico_sizes)
    print(f"  {(LAUNCHER_ASSETS / 'app-active.ico').relative_to(REPO)}")
    build_icns(LAUNCHER_ASSETS / "AppIcon.icns")

    print("frontend")
    for size in (16, 32, 48, 180):
        # apple-touch-icon must be an opaque square: iOS applies its own mask, and
        # transparent corners come back as black.
        rounded = size != 180
        image = tile(size, macos=False, mark_scale=mark_scale_for(size), rounded=rounded)
        name = "apple-touch-icon.png" if size == 180 else f"favicon-{size}.png"
        write(image.convert("RGB") if size == 180 else image, FRONTEND_PUBLIC / name)
    write(scaled(mark_on_light, 128), FRONTEND_PUBLIC / "mark-light.png")
    write(scaled(mark_on_dark, 128), FRONTEND_PUBLIC / "mark-dark.png")
    # The app header shows the full lockup rather than the mark plus a typed
    # wordmark, so the brand's own letterforms are the app name on screen. It
    # renders ~36px tall, so 128 is ample even on a 3x display; both grounds ship
    # because the header follows the app theme.
    write(scaled(lockup_on_light, 128), FRONTEND_PUBLIC / "lockup-light.png")
    write(scaled(lockup_on_dark, 128), FRONTEND_PUBLIC / "lockup-dark.png")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
