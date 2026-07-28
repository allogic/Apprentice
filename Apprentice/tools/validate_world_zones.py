#!/usr/bin/env python3
"""Validate Apprentice's persisted concentric-realm configuration contract."""

from __future__ import annotations

import json
import math
import random
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "config"
    / "content-2.7.json"
)

EXPECTED_REALMS = [
    "Homeland",
    "Barren Desert",
    "Deep Sea",
    "Endless Forest",
    "Shadow Forest",
    "Frozen Expanse",
    "Poison Mire",
    "Shattered Highlands",
    "Crystal Labyrinth",
    "Ashen March",
    "Hell",
]


def level_at(
    x: float,
    z: float,
    anchor_x: float,
    anchor_z: float,
    base_radius: float,
    ring_width: float,
    maximum_level: int,
) -> int:
    distance = math.hypot(x - anchor_x, z - anchor_z)
    rings = math.ceil((distance - base_radius) / ring_width)
    return max(0, min(rings, maximum_level))


def is_inside_level_core(
    x: float,
    z: float,
    anchor_x: float,
    anchor_z: float,
    base_radius: float,
    ring_width: float,
    maximum_level: int,
    level: int,
    inset: float,
) -> bool:
    if level_at(
        x,
        z,
        anchor_x,
        anchor_z,
        base_radius,
        ring_width,
        maximum_level,
    ) != level:
        return False

    distance = math.hypot(x - anchor_x, z - anchor_z)
    inner = base_radius + ring_width * (level - 1)
    outer = base_radius + ring_width * level
    return distance > inner + inset and distance <= outer - inset


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    danger = config["Danger"]
    base_radius = float(danger["BaseRadius"])
    ring_width = float(danger["RingWidth"])
    maximum_level = int(danger["MaximumTier"])
    deep_sea_depth = int(danger["DeepSeaDepth"])
    deep_sea_shore_width = float(danger["DeepSeaShoreWidth"])

    require(
        danger["WorldgenProfile"] == "concentric-realms-v1",
        "WorldgenProfile must be concentric-realms-v1",
    )
    require(
        danger["RealmWorldgenEnabled"] is True,
        "RealmWorldgenEnabled must be true",
    )
    require(base_radius == 10_000, "Level 0 radius must be 10,000")
    require(ring_width == 5_000, "Realm ring width must be 5,000")
    require(maximum_level == 10, "Maximum level must be 10")
    require(
        deep_sea_depth == 48,
        "Deep Sea must be 48 blocks deep before seabed variation",
    )
    require(
        deep_sea_shore_width == 128,
        "Deep Sea must retain 128-block natural shore transitions",
    )
    require(
        deep_sea_shore_width * 2 < ring_width,
        "Deep Sea shore transitions leave no continuous ocean core",
    )
    require(
        danger["RealmNames"] == EXPECTED_REALMS,
        "RealmNames do not match the approved level 0-10 design",
    )
    require(
        len(danger["Palette"]) == len(EXPECTED_REALMS),
        "Every realm needs exactly one heatmap color",
    )

    anchor_x = 500_000.0
    anchor_z = 500_000.0
    epsilon = 0.125
    boundary_cases = [
        (0.0, 0),
        (base_radius, 0),
        (base_radius + epsilon, 1),
        (base_radius + ring_width, 1),
        (base_radius + ring_width + epsilon, 2),
        (base_radius + ring_width * 2, 2),
        (base_radius + ring_width * 2 + epsilon, 3),
        (base_radius + ring_width * 9, 9),
        (base_radius + ring_width * 9 + epsilon, 10),
        (base_radius + ring_width * 100, 10),
    ]
    for radius, expected in boundary_cases:
        actual = level_at(
            anchor_x + radius,
            anchor_z,
            anchor_x,
            anchor_z,
            base_radius,
            ring_width,
            maximum_level,
        )
        require(
            actual == expected,
            f"radius {radius} resolved to level {actual}, expected {expected}",
        )

    deep_sea_inner = base_radius + ring_width
    deep_sea_outer = base_radius + ring_width * 2
    deep_sea_core_radii = [
        deep_sea_inner + deep_sea_shore_width + 1,
        (deep_sea_inner + deep_sea_outer) / 2,
        deep_sea_outer - deep_sea_shore_width - 1,
    ]
    for radius in deep_sea_core_radii:
        for sample in range(720):
            angle = math.tau * sample / 720
            x = anchor_x + math.cos(angle) * radius
            z = anchor_z + math.sin(angle) * radius
            require(
                is_inside_level_core(
                    x,
                    z,
                    anchor_x,
                    anchor_z,
                    base_radius,
                    ring_width,
                    maximum_level,
                    2,
                    deep_sea_shore_width,
                ),
                (
                    "Deep Sea core is discontinuous at "
                    f"radius={radius}, angle={angle}"
                ),
            )

    generator = random.Random(0xA77E17)
    samples = [
        (
            generator.uniform(-80_000, 80_000),
            generator.uniform(-80_000, 80_000),
        )
        for _ in range(20_000)
    ]
    first = [
        level_at(
            anchor_x + x,
            anchor_z + z,
            anchor_x,
            anchor_z,
            base_radius,
            ring_width,
            maximum_level,
        )
        for x, z in samples
    ]
    second = [
        level_at(
            anchor_x + x,
            anchor_z + z,
            anchor_x,
            anchor_z,
            base_radius,
            ring_width,
            maximum_level,
        )
        for x, z in samples
    ]
    require(first == second, "Repeated level sampling is not deterministic")

    print(
        "World-zone validation passed: 10,000-block Homeland, "
        "5,000-block rings, exact boundaries, continuous Level 2 core at "
        "2,160 angular samples, 20,000 deterministic samples."
    )


if __name__ == "__main__":
    main()
