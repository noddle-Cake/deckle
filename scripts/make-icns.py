#!/usr/bin/env python3
"""Builds packaging/macos/Deckle.icns and Assets/deckle.{png,ico} from packaging/macos/icon.svg.

Needs rsvg-convert (librsvg2-tools / librsvg) and Pillow. The icons are committed, so this only runs when the icon changes.
An .icns file is a header plus one PNG per size; macOS 10.7+ reads PNG data for every type used here.
"""
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "packaging/macos/icon.svg"
TARGET = ROOT / "packaging/macos/Deckle.icns"
ASSETS = ROOT / "src/Deckle.App/Assets"

# (type, pixel size): 16, 32, 128, 256, 512 at @1x and @2x.
TYPES = [
    (b"icp4", 16), (b"ic11", 32),
    (b"icp5", 32), (b"ic12", 64),
    (b"ic07", 128), (b"ic13", 256),
    (b"ic08", 256), (b"ic14", 512),
    (b"ic09", 512), (b"ic10", 1024),
]


def render(size: int, out: Path) -> bytes:
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", str(out), str(SOURCE)], check=True)
    return out.read_bytes()


def write_app_icons() -> None:
    """The Windows and Linux icons: the same artwork without the macOS grid's margin and shadow."""
    from PIL import Image

    with tempfile.TemporaryDirectory() as tmp:
        render(1024, Path(tmp) / "icon.png")
        art = Image.open(Path(tmp) / "icon.png").convert("RGBA").crop((100, 100, 924, 924))
        art.load()
    art = art.resize((256, 256), Image.LANCZOS)
    art.save(ASSETS / "deckle.png")
    art.save(ASSETS / "deckle.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])


def main() -> int:
    chunks = []
    with tempfile.TemporaryDirectory() as tmp:
        for kind, size in TYPES:
            png = render(size, Path(tmp) / f"{size}.png")
            chunks.append(kind + struct.pack(">I", len(png) + 8) + png)
    body = b"".join(chunks)
    TARGET.write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + body)
    print(f"wrote {TARGET.relative_to(ROOT)} ({TARGET.stat().st_size // 1024} KB)")
    write_app_icons()
    return 0


if __name__ == "__main__":
    sys.exit(main())
