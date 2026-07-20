#!/usr/bin/env python3
"""Generate clean, deterministic 3D models for Apprentice 2.7 items."""

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/apprentice/shapes/item/2.7"
REVIEWED_ROOT = ROOT / "tools/model_sources/reviewed"

MATERIALS = {
    "wood": "game:block/wood/planks/oak1",
    "darkwood": "game:block/wood/planks/walnut1",
    "iron": "game:block/metal/ingot/iron",
    "gold": "game:block/metal/ingot/gold",
    "copper": "game:block/metal/ingot/copper",
    "leather": "game:block/leather/plain",
    "bone": "game:block/creature/bone",
}

BOW_MATERIALS = {
    **MATERIALS,
    "gold": "game:block/metal/ingot/gold",
    "gripwrap": "apprentice:item/2.7/compositebow-grip-wrap",
    "laminate": "apprentice:item/2.7/compositebow-material",
    "string": "game:block/leather/plain",
}


def cuboid(name, start, end, texture="#iron", rotation=None):
    item = {
        "name": name, "from": start, "to": end,
        "faces": {face: {"texture": texture} for face in ("north", "east", "south", "west", "up", "down")},
    }
    if rotation:
        axis, angle, origin = rotation
        item[f"rotation{axis.upper()}"] = angle
        item["rotationOrigin"] = origin
    return item


def segment_xy(name, start, end, width, depth, texture, z=8, overlap=0.12):
    """Create a cuboid whose centre line joins two XY profile points.

    Vintage Story shapes do not support arbitrary triangle meshes. A sequence
    of short, joined cuboids is the engine-native equivalent of the repeated
    cross-sections used by curved OBJ samples. The small overlap prevents
    raster cracks without creating coplanar material layers.
    """
    dx = end[0] - start[0]
    dy = end[1] - start[1]
    length = math.hypot(dx, dy)
    angle = math.degrees(math.atan2(-dx, dy))
    return cuboid(
        name,
        [start[0] - width / 2, start[1], z - depth / 2],
        [start[0] + width / 2, start[1] + length + overlap, z + depth / 2],
        texture,
        ("z", round(angle, 4), [start[0], start[1], z]),
    )


models = {
    "grandmaster-spear": [
        cuboid("walnut-shaft", [-8, 7.4, 7.4], [31, 8.6, 8.6], "#darkwood"),
        cuboid("gold-butt-cap", [-10, 6.85, 6.85], [-7, 9.15, 9.15], "#gold"),
        cuboid("rear-grip", [1, 6.75, 6.75], [11, 9.25, 9.25], "#leather"),
        cuboid("grip-band-a", [1, 6.5, 6.5], [2, 9.5, 9.5], "#gold"),
        cuboid("grip-band-b", [10, 6.5, 6.5], [11, 9.5, 9.5], "#gold"),
        cuboid("gold-head-socket", [28, 6.45, 6.45], [33, 9.55, 9.55], "#gold"),
        cuboid("sunlance-blade-root", [32, 4.4, 6.55], [37, 11.6, 9.45], "#iron"),
        cuboid("sunlance-blade-mid", [36.5, 5.4, 6.75], [41, 10.6, 9.25], "#iron"),
        cuboid("sunlance-blade-tip", [40.5, 6.5, 7.0], [45, 9.5, 9.0], "#iron"),
        cuboid("sun-near-core", [33.8, 6.7, 6.22], [36.8, 9.3, 6.52], "#gold", ("z", 45, [35.3, 8, 6.37])),
        cuboid("sun-far-core", [33.8, 6.7, 9.48], [36.8, 9.3, 9.78], "#gold", ("z", 45, [35.3, 8, 9.63])),
        cuboid("sun-near-ray-n", [34.9, 9.1, 6.18], [35.7, 11.0, 6.56], "#gold"),
        cuboid("sun-near-ray-s", [34.9, 5.0, 6.18], [35.7, 6.9, 6.56], "#gold"),
        cuboid("sun-near-ray-e", [36.5, 7.6, 6.18], [38.4, 8.4, 6.56], "#gold"),
        cuboid("sun-near-ray-w", [32.2, 7.6, 6.18], [34.1, 8.4, 6.56], "#gold"),
        cuboid("sun-far-ray-n", [34.9, 9.1, 9.44], [35.7, 11.0, 9.82], "#gold"),
        cuboid("sun-far-ray-s", [34.9, 5.0, 9.44], [35.7, 6.9, 9.82], "#gold"),
        cuboid("sun-far-ray-e", [36.5, 7.6, 9.44], [38.4, 8.4, 9.82], "#gold"),
        cuboid("sun-far-ray-w", [32.2, 7.6, 9.44], [34.1, 8.4, 9.82], "#gold"),
    ],
    "kit-trap": [
        cuboid("frame-left", [1, 2, 7], [2.2, 14, 9], "#iron"),
        cuboid("frame-right", [13.8, 2, 7], [15, 14, 9], "#iron"),
        cuboid("frame-back", [2.2, 12.8, 7], [13.8, 14, 9], "#iron"),
        cuboid("spring-left", [2.3, 6.8, 6.5], [4.6, 9.2, 9.5], "#darkwood"),
        cuboid("spring-right", [11.4, 6.8, 6.5], [13.7, 9.2, 9.5], "#darkwood"),
        cuboid("trigger-plate", [5.2, 5.2, 6.3], [10.8, 10.8, 7.0], "#iron"),
        cuboid("jaw-front", [2, 2.6, 6.6], [14, 4.1, 9.4], "#iron"),
        cuboid("jaw-back", [2, 11.9, 6.6], [14, 13.4, 9.4], "#iron"),
        cuboid("front-tooth-1", [2.5, 4.0, 6.8], [3.8, 7.0, 9.2], "#iron"),
        cuboid("front-tooth-2", [5.9, 4.0, 6.8], [7.2, 7.5, 9.2], "#iron"),
        cuboid("front-tooth-3", [9.3, 4.0, 6.8], [10.6, 7.5, 9.2], "#iron"),
        cuboid("front-tooth-4", [12.2, 4.0, 6.8], [13.5, 7.0, 9.2], "#iron"),
        cuboid("back-tooth-1", [2.5, 9.0, 6.8], [3.8, 12.0, 9.2], "#iron"),
        cuboid("back-tooth-2", [5.9, 8.5, 6.8], [7.2, 12.0, 9.2], "#iron"),
        cuboid("back-tooth-3", [9.3, 8.5, 6.8], [10.6, 12.0, 9.2], "#iron"),
        cuboid("back-tooth-4", [12.2, 9.0, 6.8], [13.5, 12.0, 9.2], "#iron"),
    ],
    "kit-survey": [
        cuboid("compass-case", [2, 2, 5], [14, 14, 11], "#copper"), cuboid("compass-face", [3, 3, 4.4], [13, 13, 5.2], "#bone"),
        cuboid("north-needle", [7.4, 7, 3.8], [8.6, 13, 4.8], "#iron"), cuboid("south-needle", [7.4, 3, 3.8], [8.6, 8, 4.8], "#darkwood"),
        cuboid("sight", [7, 14, 6], [9, 16, 10], "#iron"),
    ],
    "kit-sewing": [
        cuboid("thread-spool", [2, 4, 5], [7, 12, 11], "#bone"), cuboid("spool-top", [1, 11, 4], [8, 13, 12], "#wood"),
        cuboid("spool-bottom", [1, 3, 4], [8, 5, 12], "#wood"),
        cuboid("large-needle", [10, 2, 7], [11, 15, 8], "#iron", ("z", 12, [10.5, 8, 7.5])),
        cuboid("thread-tail", [7, 7, 7], [13, 8, 8], "#leather"),
    ],
    "material-blister": [
        cuboid("steel-billet", [2, 5, 5], [14, 10, 11]),
        cuboid("carbon-blister-a", [3, 10, 6], [6, 11, 10], "#copper"),
        cuboid("carbon-blister-b", [8, 10, 6], [12, 11.25, 10], "#copper"),
    ],
    "chain-links": [
        cuboid("link-a-top", [2, 11, 7], [8, 12.5, 9]), cuboid("link-a-bottom", [2, 5.5, 7], [8, 7, 9]),
        cuboid("link-a-left", [2, 7, 7], [3.5, 11, 9]), cuboid("link-a-right", [6.5, 7, 7], [8, 11, 9]),
        cuboid("link-b-top", [8, 9, 5], [14, 10.5, 11]), cuboid("link-b-bottom", [8, 5.5, 5], [14, 7, 11]),
        cuboid("link-b-left", [8, 7, 5], [9.5, 9, 11]), cuboid("link-b-right", [12.5, 7, 5], [14, 9, 11]),
    ],
    "organic-cluster": [
        cuboid("tissue-core", [4, 4, 5], [12, 11, 11], "#leather"),
        cuboid("bone-spine", [7, 3, 4], [9, 14, 6], "#bone", ("z", -18, [8, 8, 5])),
        cuboid("tissue-lobe-a", [2, 6, 6], [6, 11, 10], "#leather"),
        cuboid("tissue-lobe-b", [10, 7, 6], [14, 13, 10], "#leather"),
    ],
    "gloamcap": [
        cuboid("stem", [7, 2, 7], [9, 10, 9], "#bone"),
        cuboid("cap-brim", [3, 9, 4], [13, 11, 12], "#leather"),
        cuboid("cap-crown", [5, 11, 5], [11, 14, 11], "#darkwood"),
    ],
    "refractory-liner": [
        cuboid("base", [3, 3, 3], [13, 5, 13], "#bone"),
        cuboid("north-wall", [3, 5, 3], [13, 12, 5], "#bone"), cuboid("south-wall", [3, 5, 11], [13, 12, 13], "#bone"),
        cuboid("west-wall", [3, 5, 5], [5, 12, 11], "#bone"), cuboid("east-wall", [11, 5, 5], [13, 12, 11], "#bone"),
        cuboid("iron-band", [2.5, 7, 2.5], [13.5, 8, 13.5]),
    ],
    "rolled-plans": [
        cuboid("parchment-roll", [3, 6, 6], [13, 10, 10], "#bone"),
        cuboid("left-scroll", [2, 5, 5], [4, 11, 11], "#bone"), cuboid("right-scroll", [12, 5, 5], [14, 11, 11], "#bone"),
        cuboid("leather-binding", [7, 5.5, 5.5], [9, 10.5, 10.5], "#leather"),
    ],
    "metal-plate": [
        cuboid("forged-plate", [3, 3, 7], [13, 13, 9]),
        cuboid("raised-spine", [7.25, 4, 6.5], [8.75, 12, 9.5]),
        cuboid("top-flange", [3, 12, 6.5], [13, 13, 9.5]), cuboid("bottom-flange", [3, 3, 6.5], [13, 4, 9.5]),
    ],
    "scale-stack": [
        cuboid("scale-back", [3, 4, 8], [9, 11, 9.5]), cuboid("scale-left", [2, 7, 6], [8, 14, 7.5]),
        cuboid("scale-center", [5, 5, 5], [11, 12, 6.5]), cuboid("scale-right", [8, 7, 6], [14, 14, 7.5]),
        cuboid("scale-front", [7, 3, 4], [13, 10, 5.5]),
    ],
    "berry-cluster": [
        cuboid("berry-a", [3, 5, 6], [7.5, 9.5, 10.5], "#leather"), cuboid("berry-b", [8.5, 5, 6], [13, 9.5, 10.5], "#leather"),
        cuboid("berry-c", [5.5, 9, 5], [10.5, 14, 10], "#darkwood"), cuboid("stem", [7.4, 13, 7], [8.6, 16, 9], "#wood"),
    ],
    "tower-shield": [
        cuboid("main-face", [4.15, 0.8, 7.975], [11.85, 15.2, 8.75], "#wood"),
        cuboid("upper-face", [4.15, 15.2, 7.975], [11.85, 16.6, 8.75], "#darkwood"),
        cuboid("lower-face", [4.15, -0.6, 7.975], [11.85, 0.8, 8.75], "#darkwood"),
        cuboid("flat-frame-left", [2.8, -2.0, 6.45], [4.15, 18.0, 9.25], "#iron"),
        cuboid("flat-frame-right", [11.85, -2.0, 6.45], [13.2, 18.0, 9.25], "#iron"),
        cuboid("flat-frame-top", [4.15, 16.6, 6.45], [11.85, 18.0, 9.25], "#iron"),
        cuboid("flat-frame-bottom", [4.15, -2.0, 6.45], [11.85, -0.6, 9.25], "#iron"),
        cuboid("upper-left-diagonal", [7.45, 9.2, 6.1], [8.55, 17.0, 6.45], "#iron", ("z", -22, [8.0, 8.0, 6.275])),
        cuboid("lower-right-diagonal", [7.45, -1.0, 6.1], [8.55, 6.8, 6.45], "#iron", ("z", -22, [8.0, 8.0, 6.275])),
        cuboid("upper-right-diagonal", [7.45, 9.2, 6.1], [8.55, 17.0, 6.45], "#iron", ("z", 22, [8.0, 8.0, 6.275])),
        cuboid("lower-left-diagonal", [7.45, -1.0, 6.1], [8.55, 6.8, 6.45], "#iron", ("z", 22, [8.0, 8.0, 6.275])),
        cuboid("diamond-boss", [6.35, 6.35, 5.75], [9.65, 9.65, 6.1], "#iron", ("z", 45, [8.0, 8.0, 5.925])),
        cuboid("gold-core", [7.1, 7.1, 5.4], [8.9, 8.9, 5.75], "#gold", ("z", 45, [8.0, 8.0, 5.575])),
        cuboid("gold-rivet-1", [4.2, -0.25, 5.9], [4.9, 0.45, 6.1], "#gold"),
        cuboid("gold-rivet-2", [11.1, -0.25, 5.9], [11.8, 0.45, 6.1], "#gold"),
        cuboid("gold-rivet-3", [4.2, 15.55, 5.9], [4.9, 16.25, 6.1], "#gold"),
        cuboid("gold-rivet-4", [11.1, 15.55, 5.9], [11.8, 16.25, 6.1], "#gold"),
        cuboid("rear-upper-mount", [6.75, 9.85, 8.7], [9.25, 11.0, 9.55], "#iron"),
        cuboid("rear-lower-mount", [6.75, 5.0, 8.7], [9.25, 6.15, 9.55], "#iron"),
        cuboid("rear-hand-grip", [7.25, 5.3, 9.3], [8.75, 10.7, 10.2], "#leather"),
        cuboid("forearm-strap-upper", [9.35, 8.85, 8.7], [11.65, 9.65, 9.75], "#leather"),
        cuboid("forearm-strap-lower", [9.35, 6.35, 8.7], [11.65, 7.15, 9.75], "#leather"),
    ],
    "master-fishing-rod": [
        cuboid("gold-butt-cap", [6.7, -2, 6.7], [9.3, 0, 9.3], "#gold"),
        cuboid("wrapped-grip", [6.8, 0, 6.8], [9.2, 7, 9.2], "#leather"),
        cuboid("grip-gold-strip-1", [6.55, 0.7, 6.55], [9.45, 1.2, 9.45], "#gold"),
        cuboid("grip-gold-strip-2", [6.55, 2.4, 6.55], [9.45, 2.9, 9.45], "#gold"),
        cuboid("grip-gold-strip-3", [6.55, 4.1, 6.55], [9.45, 4.6, 9.45], "#gold"),
        cuboid("grip-gold-strip-4", [6.55, 5.8, 6.55], [9.45, 6.3, 9.45], "#gold"),
        cuboid("rod-lower", [7.25, 6, 7.25], [8.75, 19, 8.75], "#darkwood", ("z", -8, [8, 6, 8])),
        cuboid("rod-middle", [7.5, 18, 7.5], [8.5, 28, 8.5], "#wood", ("z", -17, [8, 18, 8])),
        cuboid("rod-tip", [7.7, 27, 7.7], [8.3, 35, 8.3], "#bone", ("z", -29, [8, 27, 8])),
        cuboid("reel-spool", [4.8, 2.2, 6.4], [7.1, 5.7, 9.6], "#copper"),
        cuboid("reel-gold-spool-left", [4.45, 2.0, 6.1], [5.05, 5.9, 9.9], "#gold"),
        cuboid("reel-gold-spool-right", [6.85, 2.0, 6.1], [7.45, 5.9, 9.9], "#gold"),
        cuboid("reel-frame-top", [4.2, 5.4, 6], [7.3, 6.2, 10], "#gold"), cuboid("reel-frame-bottom", [4.2, 1.7, 6], [7.3, 2.5, 10], "#gold"),
        cuboid("reel-crank", [3.2, 3.2, 7.3], [5, 4, 8.7], "#gold"), cuboid("crank-knob", [2.5, 2.5, 6.8], [3.7, 4.7, 9.2], "#leather"),
        cuboid("guide-one", [5.8, 16, 7.2], [7.1, 17.5, 8.8]), cuboid("guide-two", [4.2, 24, 7.35], [5.4, 25.3, 8.65]),
        cuboid("guide-tip", [1.2, 31.5, 7.5], [2.2, 32.7, 8.5]),
    ],
}


def upgrade_case(icon):
    elements = [
        cuboid("case-body", [2.5, 2.5, 6], [13.5, 12.5, 10], "#darkwood"),
        cuboid("silver-front-plate", [3.2, 3.2, 5.35], [12.8, 11.8, 6], "#iron"),
        cuboid("gold-border-top", [3.2, 11.2, 5.05], [12.8, 11.8, 5.35], "#gold"),
        cuboid("gold-border-bottom", [3.2, 3.2, 5.05], [12.8, 3.8, 5.35], "#gold"),
        cuboid("gold-border-left", [3.2, 3.8, 5.05], [3.8, 11.2, 5.35], "#gold"),
        cuboid("gold-border-right", [12.2, 3.8, 5.05], [12.8, 11.2, 5.35], "#gold"),
        cuboid("left-hinge", [4, 11.9, 9.6], [5.5, 13.1, 10.5], "#iron"),
        cuboid("right-hinge", [10.5, 11.9, 9.6], [12, 13.1, 10.5], "#iron"),
        cuboid("left-handle-post", [5, 12.5, 7.1], [6, 15, 8.9], "#gold"),
        cuboid("right-handle-post", [10, 12.5, 7.1], [11, 15, 8.9], "#gold"),
        cuboid("handle", [5, 14.3, 7.1], [11, 15.3, 8.9], "#leather"),
        cuboid("left-clasp", [4.3, 7, 4.75], [5.5, 9, 5.35], "#gold"),
        cuboid("right-clasp", [10.5, 7, 4.75], [11.7, 9, 5.35], "#gold"),
    ]

    if icon == "shield":
        elements.extend([
            cuboid("gold-shield-crown", [6.1, 8.8, 4.65], [9.9, 9.7, 5.05], "#gold"),
            cuboid("gold-shield-body", [6.5, 5.5, 4.65], [9.5, 9.1, 5.05], "#gold"),
            cuboid("gold-shield-left-tip", [5.9, 6.3, 4.65], [7.2, 8.8, 5.05], "#gold", ("z", -18, [7.1, 8.7, 4.85])),
            cuboid("gold-shield-right-tip", [8.8, 6.3, 4.65], [10.1, 8.8, 5.05], "#gold", ("z", 18, [8.9, 8.7, 4.85])),
            cuboid("gold-shield-point", [7.3, 4.6, 4.65], [8.7, 6.2, 5.05], "#gold", ("z", 45, [8, 5.5, 4.85])),
        ])
    elif icon == "weapons":
        elements.extend([
            segment_xy("gold-sword-a", (5.2, 5.1), (10.8, 10.7), 0.62, 0.4, "#gold", z=4.85, overlap=0),
            segment_xy("gold-sword-b", (10.8, 5.1), (5.2, 10.7), 0.62, 0.4, "#gold", z=4.85, overlap=0),
            cuboid("gold-sword-a-guard", [4.5, 5.6, 4.58], [6.3, 6.2, 5.12], "#gold", ("z", -45, [5.4, 5.9, 4.85])),
            cuboid("gold-sword-b-guard", [9.7, 5.6, 4.58], [11.5, 6.2, 5.12], "#gold", ("z", 45, [10.6, 5.9, 4.85])),
        ])
    elif icon == "tools":
        elements.extend([
            segment_xy("gold-pick-handle", (5.2, 5.0), (10.5, 10.3), 0.58, 0.4, "#gold", z=4.85, overlap=0),
            segment_xy("gold-hammer-handle", (10.8, 5.0), (6.1, 9.7), 0.62, 0.4, "#gold", z=4.85, overlap=0),
            cuboid("gold-pick-head", [8.8, 9.6, 4.58], [12.2, 10.4, 5.12], "#gold", ("z", 45, [10.5, 10.0, 4.85])),
            cuboid("gold-hammer-head", [4.4, 9.0, 4.58], [7.7, 10.2, 5.12], "#gold", ("z", -45, [6.05, 9.6, 4.85])),
        ])
    return elements


models["kit-armor-upgrade"] = upgrade_case("shield")
models["kit-weapon-upgrade"] = upgrade_case("weapons")
models["kit-tool-upgrade"] = upgrade_case("tools")
models["kit-first-aid"] = [
    cuboid("first-aid-case", [3, 3, 6], [13, 12, 10], "#leather"),
    cuboid("first-aid-lid", [3, 10.8, 5.7], [13, 13, 10.3], "#darkwood"),
    cuboid("first-aid-handle-left", [5, 12.5, 7], [6, 15, 9], "#iron"),
    cuboid("first-aid-handle-right", [10, 12.5, 7], [11, 15, 9], "#iron"),
    cuboid("first-aid-handle", [5, 14.2, 7], [11, 15.2, 9], "#leather"),
    cuboid("first-aid-cross-vertical", [7.1, 5.0, 5.25], [8.9, 10.0, 5.7], "#bone"),
    cuboid("first-aid-cross-horizontal", [5.5, 6.6, 5.25], [10.5, 8.4, 5.7], "#bone"),
]


def bow_elements(draw):
    """Build an original Apprentice composite recurve and its draw state."""
    # Preserve the four already-approved states exactly, then add two later
    # overdraw frames. The extra limb movement is deliberately smaller than
    # the nock travel: most of the visual change comes from drawing the string
    # and arrow toward the hand, while the composite frame flexes with it.
    draw_fractions = [0, 1 / 3, 2 / 3, 1, 1.12, 1.24]
    # The last state places the player-side end of the shaft at the native
    # recurve full-draw hand depth (about engine Z=21.5 after conversion).
    nock_pulls = [0, 1.60, 3.35, 5.25, 7.65, 9.90]
    if not 0 <= draw < len(draw_fractions):
        raise ValueError(f"composite-bow: unsupported draw state {draw}")
    draw_fraction = draw_fractions[draw]
    arrow_y = 9.55
    arrow_half_height = 0.14
    arrow_z = 6.55
    arrow_half_depth = 0.14
    grip_bottom = 5.95
    grip_top = 8.95
    grip_near_side = 7.20
    if not (
        arrow_y - arrow_half_height > grip_top
        and arrow_z + arrow_half_depth < grip_near_side
    ):
        raise ValueError("composite-bow: arrow shaft intersects the hand grip")
    # Authored in the same readable orientation as the archery references:
    # target/arrow direction is +X, archer/string direction is -X.
    base_upper = [
        (9.40, 10.00),
        (10.60, 11.65),
        (11.70, 13.45),
        (12.40, 15.35),
        (11.90, 17.00),
        (10.20, 18.45),
        (7.90, 19.60),
        (6.40, 20.25),
    ]
    # Limbs flatten toward the archer under load while the riser stays fixed.
    flex = [0, -0.25, -0.55, -0.90, -1.05, -0.85, -0.45, -0.30]
    upper = [
        (x + flex[index] * draw_fraction, y)
        for index, (x, y) in enumerate(base_upper)
    ]
    lower = [(x, 16 - y) for x, y in upper]
    # The lower limb begins below the grip. A narrow composite neck bridges
    # the distance; allowing the wide angled limb to start at Y=6 made its
    # upper corner cut into the wrapped grip.
    lower[0] = (9.40, 5.10)
    widths = [1.75, 1.60, 1.42, 1.22, 1.02, 0.78, 0.58]

    lower_dx = lower[1][0] - lower[0][0]
    lower_dy = lower[1][1] - lower[0][1]
    lower_angle = math.atan2(-lower_dx, lower_dy)
    lower_limb_high_edge = lower[0][1] + abs(math.sin(lower_angle)) * widths[0] / 2
    lower_neck_start_y = 6.25
    upper_neck_start_y = 8.62
    if not (
        lower_limb_high_edge < grip_bottom - 0.05
        and grip_bottom < lower_neck_start_y < grip_top
        and grip_bottom < upper_neck_start_y < grip_top
    ):
        raise ValueError("composite-bow: riser necks do not join cleanly through the grip")

    elems = [
        # The wrapped grip is the visible central riser. Short composite necks
        # overlap it and the limb roots, keeping one connected silhouette with
        # no nested core cuboid that can leak through in item rendering.
        segment_xy(
            "lower-riser-neck", (9.50, lower_neck_start_y), lower[0],
            0.78, 1.24, "#laminate", overlap=0.16,
        ),
        segment_xy(
            "upper-riser-neck", (9.50, upper_neck_start_y), (9.40, 10.08),
            0.78, 1.24, "#laminate", overlap=0.16,
        ),
        # The cord pattern is painted onto this single clean 3D rod. Separate
        # wrap cuboids create protruding fins and coplanar seams in-game.
        cuboid("leather-grip", [8.72, 5.95, 7.22], [10.28, 8.95, 8.78], "#gripwrap"),
        cuboid("lower-grip-band", [8.65, 5.85, 7.15], [10.35, 6.10, 8.85], "#copper"),
        cuboid("upper-grip-band", [8.65, 8.80, 7.15], [10.35, 9.05, 8.85], "#copper"),
        cuboid("arrow-rest", [8.45, 9.15, 6.65], [9.05, 9.32, 7.30], "#gold"),
    ]

    for side, points in (("upper", upper), ("lower", lower)):
        for index, (start, end) in enumerate(zip(points, points[1:])):
            main_texture = "#gold" if index == len(widths) - 1 else "#laminate"
            elems.append(segment_xy(
                f"{side}-limb-{index + 1}", start, end,
                widths[index], 1.48, main_texture,
            ))
            if index < len(widths) - 1:
                elems.append(segment_xy(
                    f"{side}-horn-facing-{index + 1}", start, end,
                    max(0.34, widths[index] * 0.34), 0.24, "#gold",
                    z=8.86, overlap=0.10,
                ))

    # Small gold points decorate both faces without masking the laminate.
    for side, point in (("upper", upper[2]), ("lower", lower[2])):
        for face, z_start, z_end in (("near", 7.08, 7.28), ("far", 8.72, 8.92)):
            elems.append(cuboid(
                f"{side}-{face}-gold-point",
                [point[0] - 0.30, point[1] - 0.30, z_start],
                [point[0] + 0.30, point[1] + 0.30, z_end],
                "#gold",
                ("z", 45, [point[0], point[1], (z_start + z_end) / 2]),
            ))

    upper_tip = upper[-1]
    lower_tip = lower[-1]
    nock_x = upper_tip[0] - nock_pulls[draw]
    nock = (nock_x, arrow_y)

    # Check centre-line geometry plus the limb half-width at every joint.
    # The string line is on the archer side (-X in this authored orientation).
    for side, points in (("upper", upper), ("lower", lower)):
        tip = points[-1]
        for index, point in enumerate(points[:-1]):
            adjacent_widths = widths[max(0, index - 1):min(len(widths), index + 1)]
            half_width = max(adjacent_widths) / 2
            vertical_fraction = (tip[1] - point[1]) / (tip[1] - nock[1])
            string_x = tip[0] + (nock[0] - tip[0]) * vertical_fraction
            if point[0] - half_width <= string_x + 0.05:
                raise ValueError(
                    f"composite-bow charge {draw}: string intersects {side} limb near point {index}"
                )

    elems.extend([
        segment_xy("upper-string", upper_tip, nock, 0.16, 0.18, "#string", z=arrow_z, overlap=0),
        segment_xy("lower-string", lower_tip, nock, 0.16, 0.18, "#string", z=arrow_z, overlap=0),
        cuboid("upper-copper-nock", [upper_tip[0] - 0.32, upper_tip[1] - 0.25, 6.35], [upper_tip[0] + 0.32, upper_tip[1] + 0.25, 8.45], "#copper"),
        cuboid("lower-copper-nock", [lower_tip[0] - 0.32, lower_tip[1] - 0.25, 6.35], [lower_tip[0] + 0.32, lower_tip[1] + 0.25, 8.45], "#copper"),
    ])

    if draw:
        arrow_front = nock_x + 13.75
        elems.extend([
            cuboid("arrow-shaft", [nock_x - 0.25, arrow_y - arrow_half_height, arrow_z - arrow_half_depth], [arrow_front, arrow_y + arrow_half_height, arrow_z + arrow_half_depth], "#darkwood"),
            cuboid("arrowhead-root", [arrow_front - 0.15, 9.10, 6.10], [arrow_front + 0.55, 10.00, 7.00], "#iron"),
            cuboid("arrowhead-point", [arrow_front + 0.50, 9.37, 6.37], [arrow_front + 1.05, 9.73, 6.73], "#iron"),
            cuboid("upper-fletching", [nock_x + 0.45, 9.68, 6.10], [nock_x + 1.55, 10.10, 7.00], "#leather"),
            cuboid("lower-fletching", [nock_x + 0.45, 9.00, 6.10], [nock_x + 1.55, 9.42, 7.00], "#leather"),
        ])

    return elems


def apply_approved_editor_changes(elements, draw):
    """Propagate the owner's charge-3 Model Creator edits to every state.

    The supplied editor file is in readable design axes, so these adjustments
    happen before conversion into Vintage Story's native held-item axes. State
    differences produced by ``bow_elements`` remain intact; shared edits are
    applied as deltas instead of replacing charge 0-2 with full-draw geometry.
    """
    elements[:] = [
        element
        for element in elements
        if element.get("name") not in {
            "upper-far-gold-point",
            "lower-far-gold-point",
        }
    ]
    by_name = {element.get("name"): element for element in elements}

    # Clean lower riser connection from the supplied full-draw file.
    lower_riser = by_name["lower-riser-neck"]
    lower_riser["from"] = [9.11, 6.25, 7.38]
    lower_riser["to"] = [9.89, 7.75, 8.62]
    lower_riser["rotationOrigin"] = [9.5, 6.25, 8.0]
    lower_riser["rotationZ"] = 180.0

    # Keep each state's flex delta while carrying the owner's corrected lower
    # root and near-facing placement through the full draw sequence.
    lower_limb = by_name["lower-limb-1"]
    lower_limb["rotationOrigin"][0] += 0.1
    lower_limb["rotationZ"] = round(lower_limb["rotationZ"] - 12.7098, 4)

    lower_facing = by_name["lower-horn-facing-1"]
    lower_facing["from"][0] -= 0.0825
    lower_facing["to"][0] -= 0.0825
    lower_facing["to"][1] -= 0.110371843691019
    lower_facing["rotationOrigin"][0] += 0.1
    lower_facing["rotationZ"] = round(lower_facing["rotationZ"] - 9.7098, 4)

    # The arrow rest moved slightly down and away from the riser.
    arrow_rest = by_name["arrow-rest"]
    for endpoint in (arrow_rest["from"], arrow_rest["to"]):
        endpoint[1] -= 0.1
        endpoint[2] += 0.1

    # Both strings were moved toward the arrow's edited depth. The pivot edits
    # are copied independently because the supplied lower/upper anchors differ.
    for string_name in ("upper-string", "lower-string"):
        string = by_name[string_name]
        string["from"][2] += 0.5
        string["to"][2] += 0.5
    by_name["upper-string"]["rotationOrigin"][0] += 0.05
    by_name["upper-string"]["rotationOrigin"][1] -= 0.1
    by_name["lower-string"]["rotationOrigin"][0] += 0.3

    if draw:
        # Move the complete arrow as one rigid assembly. This preserves shaft,
        # head and fletching alignment in all three charged states.
        for name in (
            "arrow-shaft",
            "arrowhead-root",
            "arrowhead-point",
            "upper-fletching",
            "lower-fletching",
        ):
            element = by_name[name]
            for endpoint in (element["from"], element["to"]):
                endpoint[1] -= 0.2
                endpoint[2] += 0.5

    return elements


def bow_element_to_engine_axes(element):
    """Convert the readable design axes to Vintage Story's bow axes.

    The design is easiest to inspect with the limbs vertical in XY and the
    arrow pointing along +X. Native Vintage Story bows are different: their
    limb axis is X, their thin cross-section is Y, and the arrow points toward
    -Z while the string draws toward +Z. The standard bow hand transforms
    depend on that convention. Feeding an upright XY model into the native
    84-degree hand rotation turns the bow sideways; mapping its arrow to +Z
    puts the arrowhead on the player's side of the bow.
    """
    converted = dict(element)

    def convert_point(point):
        x, y, z = point
        # Put the grip centre at the native [8, 0, 8] item-space anchor.
        # This is the prior native-axis conversion rotated 180 degrees around
        # the limb axis: target/arrow is -Z and archer/string is +Z.
        return [y + 0.55, 8.0 - z, 17.5 - x]

    if "from" in converted and "to" in converted:
        converted_from = convert_point(converted["from"])
        converted_to = convert_point(converted["to"])
        converted["from"] = [
            min(converted_from[axis], converted_to[axis])
            for axis in range(3)
        ]
        converted["to"] = [
            max(converted_from[axis], converted_to[axis])
            for axis in range(3)
        ]
    if "rotationOrigin" in converted:
        converted["rotationOrigin"] = convert_point(converted["rotationOrigin"])

    old_rotations = {
        axis: converted.pop(f"rotation{axis.upper()}", None)
        for axis in "xyz"
    }
    # Proper 180-degree facing rotation: old X -> -new Z,
    # old Y -> new X, old Z -> -new Y.
    for new_axis, old_axis, sign in (
        ("x", "y", 1),
        ("y", "z", -1),
        ("z", "x", -1),
    ):
        angle = old_rotations[old_axis]
        if angle is not None:
            converted[f"rotation{new_axis.upper()}"] = angle * sign

    faces = converted.get("faces")
    if isinstance(faces, dict):
        converted["faces"] = {
            face_name: dict(face) if isinstance(face, dict) else face
            for face_name, face in faces.items()
        }
        if converted.get("name") == "leather-grip":
            # The grip is now X-long. Explicitly map the complete 16x16 wrap
            # texture to every face; without this Vintage Story derives UVs
            # from the cuboid coordinates and samples only a plain oxblood
            # patch. Rotate the long side faces so the gold rows remain
            # separate cord bands across the rod rather than running along it.
            for face in converted["faces"].values():
                if isinstance(face, dict):
                    face["uv"] = [0, 0, 16, 16]
            for face_name in ("north", "south", "up", "down"):
                face = converted["faces"].get(face_name)
                if isinstance(face, dict):
                    face["rotation"] = 90

    if "children" in converted:
        converted["children"] = [
            bow_element_to_engine_axes(child)
            for child in converted["children"]
        ]
    return converted


for draw in range(6):
    design_elements = apply_approved_editor_changes(bow_elements(draw), draw)
    models["composite-bow" + (f"-charge{draw}" if draw else "")] = [
        bow_element_to_engine_axes(element)
        for element in design_elements
    ]

REVIEWED_MODELS = {
    "tower-shield",
    "master-fishing-rod",
    "grandmaster-spear",
    "kit-trap",
    "kit-armor-upgrade",
    "kit-weapon-upgrade",
    "kit-tool-upgrade",
    "kit-first-aid",
}

OUT.mkdir(parents=True, exist_ok=True)
for name, elements in models.items():
    if name in REVIEWED_MODELS:
        continue
    textures = BOW_MATERIALS if name.startswith("composite-bow") else MATERIALS
    document = {"textureWidth": 16, "textureHeight": 16, "textures": textures, "elements": elements}
    (OUT / f"{name}.json").write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")

for name in sorted(REVIEWED_MODELS):
    source = REVIEWED_ROOT / f"{name}.json"
    document = json.loads(source.read_text(encoding="utf-8"))
    if not isinstance(document.get("elements"), list) or not document["elements"]:
        raise ValueError(f"{name}: reviewed model has no elements")
    (OUT / f"{name}.json").write_text(
        json.dumps(document, indent=2) + "\n",
        encoding="utf-8",
    )

print(f"generated {len(models)} clean item models, including {len(REVIEWED_MODELS)} approved reviewed models")
