#!/usr/bin/env python3
"""Generate compact pixel-art materials for the Manaburn Sword."""

from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/apprentice/textures/item/2.7"
SIZE = 16


def texture(name, base, highlights, shadow, seed):
    image = Image.new("RGBA", (SIZE, SIZE))
    pixels = image.load()
    for y in range(SIZE):
        for x in range(SIZE):
            noise = ((x * 17 + y * 31 + seed * 13) % 9) - 4
            diagonal = 1 if (x + y + seed) % 11 == 0 else 0
            value = tuple(max(0, min(255, channel + noise + diagonal * 8)) for channel in base)
            pixels[x, y] = (*value, 255)
    for x in range(SIZE):
        pixels[x, 1] = (*highlights, 255)
        pixels[x, 14] = (*shadow, 255)
    image.save(OUTPUT / name)


OUTPUT.mkdir(parents=True, exist_ok=True)
texture("manaburn-darkmetal.png", (43, 49, 54), (84, 94, 101), (20, 24, 28), 1)
texture("manaburn-blade.png", (65, 75, 80), (128, 143, 150), (30, 37, 42), 2)
texture("manaburn-edge.png", (150, 165, 171), (225, 235, 238), (79, 92, 99), 3)
texture("manaburn-horn.png", (46, 40, 37), (88, 77, 69), (22, 19, 18), 4)
texture("manaburn-grip.png", (58, 62, 61), (104, 109, 105), (28, 31, 31), 5)
