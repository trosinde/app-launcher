"""Builds AppStart.ico from appstart.svg (32 px and larger) and appstart-small.svg (16-24 px).

Needs Python 3 with cairosvg and Pillow (pip install cairosvg pillow). Run from any folder:
    python assets/icon/make-icon.py
Writes assets/icon/AppStart.ico and assets/icon/appstart-256.png (README, manual).
"""
import io
from pathlib import Path

import cairosvg
from PIL import Image

HERE = Path(__file__).resolve().parent
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SMALL_UNTIL = 24


def render(svg: Path, size: int) -> Image.Image:
    png = cairosvg.svg2png(url=str(svg), output_width=size, output_height=size)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def main() -> None:
    frames = [render(HERE / ("appstart-small.svg" if s <= SMALL_UNTIL else "appstart.svg"), s) for s in SIZES]
    largest = frames[-1]
    largest.save(HERE / "AppStart.ico", format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    largest.save(HERE / "appstart-256.png")
    check = Image.open(HERE / "AppStart.ico")
    print("AppStart.ico:", sorted(check.info["sizes"]))


if __name__ == "__main__":
    main()
