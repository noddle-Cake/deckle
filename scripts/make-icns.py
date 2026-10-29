#!/usr/bin/env python3
"""Builds packaging/macos/Scanwise.icns from packaging/macos/icon.svg.

Needs rsvg-convert (librsvg2-tools / librsvg). The .icns is committed, so this only runs when the icon changes.
An .icns file is a header plus one PNG per size; macOS 10.7+ reads PNG data for every type used here.
"""
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "packaging/macos/icon.svg"
TARGET = ROOT / "packaging/macos/Scanwise.icns"

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


def main() -> int:
    chunks = []
    with tempfile.TemporaryDirectory() as tmp:
        for kind, size in TYPES:
            png = render(size, Path(tmp) / f"{size}.png")
            chunks.append(kind + struct.pack(">I", len(png) + 8) + png)
    body = b"".join(chunks)
    TARGET.write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + body)
    print(f"wrote {TARGET.relative_to(ROOT)} ({TARGET.stat().st_size // 1024} KB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
