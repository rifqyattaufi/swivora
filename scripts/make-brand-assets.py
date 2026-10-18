"""Draws the Swivora mark and writes the app icons.

Needs Pillow and numpy:  python scripts/make-brand-assets.py
Writes src/Swivora.App/Assets/icon.png, icon.ico and assets/brand/swivora-mark.png.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# three leaning bars, in a 780 x 660 design space; the middle one is tallest and notched where the right bar tucks in
BARS = {
    "left": [(62, 245), (230, 170), (230, 492), (62, 555)],
    "middle": [(280, 150), (527, 45), (527, 162), (480, 195), (480, 525), (280, 582)],
    "right": [(520, 190), (703, 125), (703, 590), (520, 545)],
}
# top colour, bottom colour of each bar (brand blue #3B82F6 into violet #8B5CF6, with a cyan lift on the left)
FILLS = {
    "left": ((34, 211, 238), (59, 130, 246)),
    "middle": ((24, 214, 255), (110, 86, 246)),
    "right": ((132, 104, 252), (168, 56, 246)),
}
SPACE = (780, 660)
CORNER = 27  # corner rounding, in design-space pixels


def bar_layer(name, scale):
    """One bar as an RGBA image the size of the design space times scale."""
    w, h = SPACE[0] * scale, SPACE[1] * scale
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).polygon([(x * scale, y * scale) for x, y in BARS[name]], fill=255)
    # blur then threshold rounds the corners
    mask = mask.filter(ImageFilter.GaussianBlur(CORNER * scale * 0.45)).point(lambda v: 255 if v > 128 else 0)
    mask = mask.filter(ImageFilter.GaussianBlur(scale * 0.6))  # soft edge
    ys = np.linspace(0, 1, h)[:, None]
    top, bottom = (np.array(c, dtype=float) for c in FILLS[name])
    rgb = (top[None, None, :] * (1 - ys[:, :, None]) + bottom[None, None, :] * ys[:, :, None])
    rgb = np.broadcast_to(rgb, (h, w, 3)).astype(np.uint8)
    layer = Image.fromarray(rgb, "RGB").convert("RGBA")
    layer.putalpha(mask)
    return layer


def mark(scale=2):
    """The mark alone, cropped to its bars."""
    w, h = SPACE[0] * scale, SPACE[1] * scale
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for name in ("left", "middle", "right"):
        img.alpha_composite(bar_layer(name, scale))
    return img.crop(img.getbbox())


def fit(image, side, fraction):
    """The image scaled to `fraction` of a square side and centred on a transparent canvas."""
    box = int(side * fraction)
    ratio = min(box / image.width, box / image.height)
    small = image.resize((max(1, round(image.width * ratio)), max(1, round(image.height * ratio))), Image.LANCZOS)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.alpha_composite(small, ((side - small.width) // 2, (side - small.height) // 2))
    return canvas


def app_icon(side=1024):
    """The mark on a deep indigo rounded tile with a soft glow."""
    radius = int(side * 0.225)
    yy, xx = np.mgrid[0:side, 0:side]
    d = np.sqrt((xx - side * 0.5) ** 2 + (yy - side * 0.42) ** 2) / (side * 0.75)
    d = np.clip(d, 0, 1)[:, :, None]
    inner, outer = np.array([22, 26, 74], float), np.array([8, 10, 30], float)
    tile = (inner * (1 - d) + outer * d).astype(np.uint8)
    tile = Image.fromarray(np.broadcast_to(tile, (side, side, 3)), "RGB").convert("RGBA")
    clip = Image.new("L", (side, side), 0)
    ImageDraw.Draw(clip).rounded_rectangle((0, 0, side - 1, side - 1), radius, fill=255)
    tile.putalpha(clip)

    m = fit(mark(), side, 0.62)
    glow = m.filter(ImageFilter.GaussianBlur(side * 0.035))
    glow.putalpha(glow.getchannel("A").point(lambda v: int(v * 0.55)))
    tile.alpha_composite(glow)
    tile.alpha_composite(m)

    border = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle((2, 2, side - 3, side - 3), radius, outline=(91, 110, 255, 110), width=max(2, side // 170))
    tile.alpha_composite(border)
    return tile


def save(image, *parts):
    path = os.path.join(ROOT, *parts)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.save(path)
    print("wrote", os.path.relpath(path, ROOT))
    return path


if __name__ == "__main__":
    icon = app_icon(1024)
    save(icon.resize((256, 256), Image.LANCZOS), "src", "Swivora.App", "Assets", "icon.png")
    ico = os.path.join(ROOT, "src", "Swivora.App", "Assets", "icon.ico")
    icon.save(ico, sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    print("wrote", os.path.relpath(ico, ROOT))
    save(fit(mark(4), 512, 0.9), "assets", "brand", "swivora-mark.png")
    save(icon.resize((512, 512), Image.LANCZOS), "assets", "brand", "swivora-icon.png")
