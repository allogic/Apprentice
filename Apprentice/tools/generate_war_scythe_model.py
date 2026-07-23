#!/usr/bin/env python3
"""Generate the reference-driven three-bladed War Scythe model."""

import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/apprentice/shapes/item/2.7/war-scythe.json"


def faces(texture):
    return {
        side: {"texture": f"#{texture}", "uv": [0, 0, 16, 16]}
        for side in ("north", "east", "south", "west", "up", "down")
    }


def box(name, start, end, texture, rotation=None):
    element = {
        "name": name,
        "from": [round(value, 4) for value in start],
        "to": [round(value, 4) for value in end],
        "faces": faces(texture),
    }
    if rotation:
        element["rotationOrigin"] = [round(value, 4) for value in rotation[0]]
        element["rotationZ"] = rotation[1]
    return element


def blade(elements, index, root_y, lengths, angles):
    x = 7.2
    y = root_y
    segment_count = len(lengths)
    for segment, (length, angle) in enumerate(zip(lengths, angles), 1):
        # Match the reference silhouette: a broad scythe plate, not a thin
        # hook/tine.  Most of the visible width is red blade body; the silver
        # part is only the sharpened lower edge.
        progress = (segment - 1) / max(1, segment_count - 1)
        body_height = 3.80 - 3.22 * progress
        body_depth = 0.86 - 0.34 * progress
        spine_height = 0.48 - 0.24 * progress
        spine_depth = 1.24 - 0.52 * progress
        bevel_height = 0.30 - 0.12 * progress
        bevel_depth = 0.12 - 0.04 * progress
        tip_extension = 0.10 + 0.72 * progress
        pivot = (x, y, 8)
        # The blade is socketed through the shaft instead of merely touching
        # its near face.  Only the first/root segment crosses the complete
        # 7.1..8.9 shaft and emerges on the opposite side.  Every following
        # segment starts at the previous joint and progressively narrows.
        root_extension = 2.75 if segment == 1 else 0.30
        elements.append(box(
            f"blade-{index}-red-{segment}",
            (x - length, y - body_height * 0.58, 8 - body_depth / 2),
            (x + root_extension, y + body_height / 2, 8 + body_depth / 2),
            "redmetal",
            (pivot, angle),
        ))
        elements.append(box(
            f"blade-{index}-spine-{segment}",
            (x - length + 0.08, y + body_height / 2 - spine_height, 8 - spine_depth / 2),
            (x + (root_extension - 0.10 if segment == 1 else 0.05), y + body_height / 2, 8 + spine_depth / 2),
            "redmetal",
            (pivot, angle),
        ))
        elements.append(box(
            f"blade-{index}-edge-{segment}",
            (x - length - tip_extension, y - body_height * 0.58 - bevel_height, 8 - bevel_depth / 2),
            (x + (root_extension - 0.08 if segment == 1 else 0.18), y - body_height * 0.58, 8 + bevel_depth / 2),
            "iron",
            (pivot, angle),
        ))
        radians = math.radians(angle)
        x -= length * math.cos(radians)
        y -= length * math.sin(radians)


ASSEMBLY_OFFSET = 6.5

elements = [
    box("shaft", (7.1, -3, 7.1), (8.9, 46.0, 8.9), "redwood"),
    box("butt-cap", (6.7, -4.5, 6.7), (9.3, -2.5, 9.3), "iron"),
    # The reference has a long, close-fitting cloth grip.  Keep its core only
    # slightly wider than the shaft so it does not read as a white handle
    # block, then layer angled overlapping strips over it like wound bandages.
    box("grip-core", (6.9, -2.35, 6.9), (9.1, 12.5, 9.1), "wrap"),
    box("grip-ring-low", (6.65, -2.5, 6.65), (9.35, -1.8, 9.35), "iron"),
    box("grip-ring-high", (6.65, 12.2, 6.65), (9.35, 12.9, 9.35), "iron"),
]

# Twelve overlapping, slightly diagonal strips create an actual wrapped-cloth
# silhouette rather than relying on a flat bandage texture alone.
for number in range(12):
    y = -2.05 + number * 1.20
    elements.append(box(
        f"grip-wrap-{number + 1}",
        (6.78, y, 6.78),
        (9.22, y + 1.05, 9.22),
        "wrap",
        ((8, y + 0.525, 8), 9),
    ))

# Three silver collars and red mounting blocks follow the physical reference.
for number, y in enumerate((29.0, 36.0, 43.0), 1):
    elements.append(box(
        f"blade-collar-{number}",
        (6.25, y - 0.7, 6.25),
        (9.75, y + 0.7, 9.75),
        "iron",
    ))
    elements.append(box(
        f"blade-mount-{number}",
        (4.8, y - 1.0, 6.85),
        (7.2, y + 1.0, 9.15),
        "redmetal",
    ))

blade(elements, 1, 29.5, (5.2, 4.8, 4.2, 3.5, 2.7), (3, 8, 15, 24, 36))
blade(elements, 2, 36.5, (6.0, 5.5, 4.8, 4.0, 3.0), (3, 8, 15, 24, 36))
blade(elements, 3, 43.5, (6.8, 6.1, 5.3, 4.3, 3.2), (2, 7, 14, 23, 35))

shape = {
    "editor": {"allAngles": True, "entityTextureMode": False},
    "textureWidth": 16,
    "textureHeight": 16,
    "textures": {
        "redwood": "game:block/wood/planks/walnut1",
        "redmetal": "apprentice:item/2.7/war-scythe-redmetal",
        "iron": "game:block/metal/ingot/iron",
        "wrap": "apprentice:item/2.7/war-scythe-wrap",
    },
    "elements": elements,
}

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(json.dumps(shape, indent=2) + "\n", encoding="utf-8")
print(f"Generated {OUTPUT} with {len(elements)} elements")
