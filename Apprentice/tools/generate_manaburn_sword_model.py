#!/usr/bin/env python3
"""Generate the template-matched, two-sided Manaburn longsword."""

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets/apprentice/shapes/item/2.7/manaburn-sword.json"


def faces(texture, glow=0):
    result = {}
    for side in ("north", "east", "south", "west", "up", "down"):
        face = {"texture": f"#{texture}", "uv": [0, 0, 16, 16]}
        if glow:
            face["glow"] = glow
        result[side] = face
    return result


def box(name, start, end, texture, rotation=None, glow=0):
    element = {
        "name": name,
        "from": [round(v, 4) for v in start],
        "to": [round(v, 4) for v in end],
        "faces": faces(texture, glow),
    }
    if rotation:
        element["rotationOrigin"] = [round(v, 4) for v in rotation[0]]
        element["rotationZ"] = round(rotation[1], 3)
    return element


elements = []

# Compact wrapped hilt. The template's visual mass belongs above the grip.
elements += [
    box("pommel-collar", (6.75, -2.3, 6.75), (9.25, -0.8, 9.25), "darksteel"),
    box("pommel-crystal", (7.0, -5.0, 7.0), (9.0, -2.0, 9.0), "mana", ((8, -3.2, 8), 45), 255),
    box("grip-core", (7.2, -0.9, 7.2), (8.8, 8.2, 8.8), "grip"),
]
for i, y in enumerate((-0.7, 0.8, 2.3, 3.8, 5.3, 6.8), 1):
    elements.append(box(f"grip-wrap-{i}", (6.9, y, 6.9), (9.1, y + 0.42, 9.1), "grip", ((8, y, 8), -12)))
elements += [
    box("grip-top-ring", (6.65, 7.7, 6.65), (9.35, 8.6, 9.35), "darksteel"),
    box("guard-neck", (6.25, 8.3, 6.5), (9.75, 10.0, 9.5), "darksteel"),
]

# Skull geometry is assembled first and then turned upright: crown toward the
# blade, jaw and teeth toward the grip.
# Two independent recessed sockets replace the old central diamond opening.
elements += [
    box("skull-backplate", (4.7, 8.7, 6.55), (11.3, 15.8, 9.45), "bone"),
    box("skull-crown", (5.25, 8.25, 6.2), (10.75, 11.1, 9.8), "bone"),
    box("skull-temple-left", (4.6, 9.4, 6.35), (6.5, 13.0, 9.65), "bone", ((6.2, 10.1, 8), -15)),
    box("skull-temple-right", (9.5, 9.4, 6.35), (11.4, 13.0, 9.65), "bone", ((9.8, 10.1, 8), 15)),
    box("skull-brow-left", (5.0, 10.0, 5.95), (7.8, 11.25, 10.05), "bone", ((7.55, 10.55, 8), 12)),
    box("skull-brow-right", (8.2, 10.0, 5.95), (11.0, 11.25, 10.05), "bone", ((8.45, 10.55, 8), -12)),
    box("socket-left-front", (5.45, 11.05, 5.7), (7.5, 13.25, 6.3), "darksteel", ((6.5, 12.0, 8), 8)),
    box("socket-right-front", (8.5, 11.05, 5.7), (10.55, 13.25, 6.3), "darksteel", ((9.5, 12.0, 8), -8)),
    box("socket-left-back", (5.45, 11.05, 9.7), (7.5, 13.25, 10.3), "darksteel", ((6.5, 12.0, 8), 8)),
    box("socket-right-back", (8.5, 11.05, 9.7), (10.55, 13.25, 10.3), "darksteel", ((9.5, 12.0, 8), -8)),
    box("eye-left-front", (5.95, 11.55, 5.55), (7.12, 12.68, 6.05), "brightmana", glow=255),
    box("eye-right-front", (8.88, 11.55, 5.55), (10.05, 12.68, 6.05), "brightmana", glow=255),
    box("eye-left-back", (5.95, 11.55, 9.95), (7.12, 12.68, 10.45), "brightmana", glow=255),
    box("eye-right-back", (8.88, 11.55, 9.95), (10.05, 12.68, 10.45), "brightmana", glow=255),
    box("nose-bridge", (7.35, 11.0, 5.85), (8.65, 13.8, 10.15), "bone"),
    box("nose-cavity-front", (7.55, 12.75, 5.55), (8.45, 14.2, 6.2), "darksteel"),
    box("nose-cavity-back", (7.55, 12.75, 9.8), (8.45, 14.2, 10.45), "darksteel"),
    box("cheek-left", (5.25, 12.75, 6.4), (7.35, 15.0, 9.6), "bone", ((7.1, 13.1, 8), -20)),
    box("cheek-right", (8.65, 12.75, 6.4), (10.75, 15.0, 9.6), "bone", ((8.9, 13.1, 8), 20)),
    box("upper-jaw", (6.0, 14.0, 6.55), (10.0, 15.5, 9.45), "bone"),
    box("chin", (6.8, 15.0, 6.75), (9.2, 17.2, 9.25), "bone"),
]
for i, x in enumerate((6.2, 7.05, 7.75, 8.25, 8.95, 9.8), 1):
    lean = -7 if x < 8 else 7
    elements.append(box(f"tooth-{i}", (x - .28, 14.7, 6.15), (x + .28, 16.25, 9.85), "bone", ((x, 14.9, 8), lean)))

# The source layout above is convenient for layering. Mirror only the skull
# assembly around its horizontal centre so the rendered face is anatomically
# upright. This explicitly prevents the earlier teeth-up orientation.
skull_prefixes = ("skull-", "socket-", "eye-", "nose-", "cheek-", "upper-jaw", "chin", "tooth-")
for element in elements:
    if not element["name"].startswith(skull_prefixes):
        continue
    old_from_y = element["from"][1]
    old_to_y = element["to"][1]
    element["from"][1] = round(24 - old_to_y, 4)
    element["to"][1] = round(24 - old_from_y, 4)
    if "rotationOrigin" in element:
        element["rotationOrigin"][1] = round(24 - element["rotationOrigin"][1], 4)
        element["rotationZ"] = -element["rotationZ"]


def chain(name, x, y, direction, lengths, angles, widths, texture="horn", depth=2.7):
    """Continuous tapered curved chain, used by horns and forged hooks."""
    px, py = x, y
    for i, (length, angle, width) in enumerate(zip(lengths, angles, widths), 1):
        absolute = angle if direction > 0 else 180 - angle
        elements.append(box(
            f"{name}-{i}",
            (px, py - width / 2, 8 - depth / 2),
            (px + length, py + width / 2, 8 + depth / 2),
            texture,
            ((px, py, 8), absolute),
        ))
        radians = math.radians(absolute)
        px += length * math.cos(radians)
        py += length * math.sin(radians)


# Four thick hooked horns framing the upright skull, not thin straight rods.
chain("horn-crown-right", 9.9, 14.2, 1, (3.8, 3.3, 2.7), (-16, -39, -67), (2.0, 1.55, 1.05))
chain("horn-crown-left", 6.1, 14.2, -1, (3.8, 3.3, 2.7), (-16, -39, -67), (2.0, 1.55, 1.05))
chain("horn-jaw-right", 10.1, 10.0, 1, (3.6, 3.0, 2.5), (12, 38, 69), (1.9, 1.45, .95))
chain("horn-jaw-left", 5.9, 10.0, -1, (3.6, 3.0, 2.5), (12, 38, 69), (1.9, 1.45, .95))

# Smooth progressive blade silhouette. Thirty narrow overlapping stages hide
# the cuboid stair-step effect: narrow at the spear tip, massive at the skull.
blade_bottom = 16.0
blade_top = 54.0
sections = 30
for i in range(sections):
    t0 = i / sections
    t1 = (i + 1) / sections
    y0 = blade_bottom + (blade_top - blade_bottom) * t0
    y1 = blade_bottom + (blade_top - blade_bottom) * t1 + 0.10
    # Broad at the skull, continuously tapering toward the point.
    half = 6.05 * (1 - t0) ** 0.72 + 0.28
    next_half = 6.05 * (1 - t1) ** 0.72 + 0.28
    visible_half = max(half, next_half)
    depth = 1.55 - 0.82 * t0
    elements.append(box(f"blade-face-{i+1:02}", (8-visible_half, y0, 8-depth/2), (8+visible_half, y1, 8+depth/2), "blade"))
    edge_width = .30
    elements.append(box(f"blade-edge-left-{i+1:02}", (8-visible_half-edge_width, y0, 7.78), (8-visible_half+.12, y1, 8.22), "edge"))
    elements.append(box(f"blade-edge-right-{i+1:02}", (8+visible_half-.12, y0, 7.78), (8+visible_half+edge_width, y1, 8.22), "edge"))

# Layered spear point instead of a blunt cap.
for i in range(7):
    y0 = 53.2 + i * .72
    half = max(.10, .72 - i * .10)
    elements.append(box(f"spear-point-{i+1}", (8-half, y0, 7.75), (8+half, y0+.82, 8.25), "edge"))

# Three extremely heavy swept-back hooks on each edge. Roots overlap deeply
# into the blade so the silhouette reads as one forged weapon.
hook_rows = (21.0, 31.0, 41.0)
for row, y in enumerate(hook_rows, 1):
    t = (y - blade_bottom) / (blade_top - blade_bottom)
    half = 6.05 * (1 - t) ** 0.72 + 0.28
    # Start 0.9 units inside the blade: no floating roots, no visible gap.
    chain(f"hook-right-{row}", 8 + half - .9, y, 1, (3.7, 3.25, 2.5), (-8, -38, -72), (2.65, 2.05, 1.15), "darksteel", 1.75)
    chain(f"hook-left-{row}", 8 - half + .9, y, -1, (3.7, 3.25, 2.5), (-8, -38, -72), (2.65, 2.05, 1.15), "darksteel", 1.75)

# Recessed blue channel and denser asymmetric runes on both faces. A black
# channel bed makes the light read as inset, never like a floating cyan rod.
for suffix, front in (("front", True), ("back", False)):
    if front:
        bed_z, mana_z = (7.10, 7.48), (7.02, 7.30)
    else:
        bed_z, mana_z = (8.52, 8.90), (8.70, 8.98)
    elements.append(box(f"channel-bed-{suffix}", (6.65, 17.2, bed_z[0]), (9.35, 48.5, bed_z[1]), "darksteel"))
    elements.append(box(f"mana-core-{suffix}", (7.15, 17.6, mana_z[0]), (8.85, 49.0, mana_z[1]), "mana", glow=220))
    for i, y in enumerate((20.0, 23.8, 27.6, 31.4, 35.2, 39.0, 42.8, 46.2), 1):
        angle = 34 if i % 2 else -34
        elements.append(box(f"rune-{suffix}-{i}-a", (5.9, y, mana_z[0]-.08), (7.55, y+.40, mana_z[1]+.08), "brightmana", ((7.35, y, 8), angle), 255))
        elements.append(box(f"rune-{suffix}-{i}-b", (8.45, y+.25, mana_z[0]-.08), (10.1, y+.65, mana_z[1]+.08), "brightmana", ((8.65, y+.25, 8), -angle), 255))

shape = {
    "editor": {"allAngles": True, "entityTextureMode": False},
    "textureWidth": 16,
    "textureHeight": 16,
    "textures": {
        "darksteel": "apprentice:item/2.7/manaburn-darkmetal",
        "blade": "apprentice:item/2.7/manaburn-blade",
        "edge": "apprentice:item/2.7/manaburn-edge",
        "bone": "game:block/creature/bone",
        "horn": "apprentice:item/2.7/manaburn-horn",
        "grip": "apprentice:item/2.7/manaburn-grip",
        "mana": "apprentice:item/2.7/ingot-aethersteel",
        "brightmana": "apprentice:item/2.7/scales-aethersteel",
    },
    "elements": elements,
}

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(json.dumps(shape, indent=2) + "\n", encoding="utf-8")
print(f"Generated {OUTPUT} with {len(elements)} elements")
