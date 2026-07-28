#!/usr/bin/env python3
"""Validate Apprentice's persisted concentric-realm configuration contract."""

from __future__ import annotations

import json
import math
import random
import re
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "config"
    / "content-2.7.json"
)
WORLDGEN_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ConcentricRealmWorldgenSystem.cs"
)
MAP_RESTRICTION_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "EndlessForestMapRestrictionSystem.cs"
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


def adjusted_temperature(
    temperature_celsius: int,
    y: int,
    sea_level: int = 110,
) -> int:
    unscaled = int((temperature_celsius + 20) * 4.25)
    return max(
        -20,
        min(40, int((unscaled - (y - sea_level) / 1.5) / 4.25) - 20),
    )


def adjusted_rainfall(
    rainfall: int,
    y: int,
    sea_level: int = 110,
) -> int:
    lowland_bonus = 5 * max(0, min(8, 8 + sea_level - y))
    return max(
        0,
        min(255, rainfall + (y - sea_level) // 2 + lowland_bonus),
    )


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

    worldgen_source = WORLDGEN_SOURCE_PATH.read_text(encoding="utf-8")
    map_restriction_source = MAP_RESTRICTION_SOURCE_PATH.read_text(
        encoding="utf-8"
    )
    require(
        re.search(
            r"ChunkColumnGeneration\s*\(\s*"
            r"SculptDeepSeaBeforeVegetation\s*,\s*"
            r"EnumWorldGenPass\.Vegetation",
            worldgen_source,
        )
        is not None,
        (
            "Deep Sea must be sculpted in the Vegetation pass before "
            "vanilla vegetation and sunlight generation"
        ),
    )
    require(
        "originalColumnTop + 1" in worldgen_source,
        (
            "Deep Sea cleanup must remove the one-block surface vegetation "
            "created during the Terrain pass"
        ),
    )
    require(
        ".SetBlockUnsafe(" not in worldgen_source,
        (
            "Deep Sea must use allocation-safe solid block writes because "
            "Vegetation-pass chunk layers may not have a writable palette"
        ),
    )
    require(
        "floorChunkData[floorIndex] = rockId;" in worldgen_source
        and "chunkData[index] = 0;" in worldgen_source,
        (
            "Deep Sea floor and air writes must use the allocation-safe "
            "IChunkBlocks indexer"
        ),
    )
    require(
        "private const int EndlessForestLevel = 3;" in worldgen_source,
        "Endless Forest must remain Level 3",
    )
    require(
        "private const int EndlessForestDensity = 255;"
        in worldgen_source
        and "private const int EndlessForestShrubDensity = 224;"
        in worldgen_source,
        "Endless Forest must retain maximum tree density and dense shrubs",
    )
    require(
        "private const int EndlessForestTemperatureCelsius = 14;"
        in worldgen_source
        and "private const int EndlessForestRainfall = 100;"
        in worldgen_source,
        (
            "Endless Forest must compensate for Vintage Story's altitude "
            "temperature/rainfall adjustment"
        ),
    )
    valley_temperature = adjusted_temperature(14, 110)
    valley_rainfall = adjusted_rainfall(100, 110)
    mid_temperature = adjusted_temperature(14, 190)
    mid_rainfall = adjusted_rainfall(100, 190)
    high_temperature = adjusted_temperature(14, 304)
    high_rainfall = adjusted_rainfall(100, 304)
    require(
        2 <= valley_temperature <= 22
        and 95 <= valley_rainfall <= 170,
        "Level 3 valleys must remain compatible with broadleaf trees",
    )
    require(
        -14 <= mid_temperature <= 12
        and 50 <= mid_rainfall <= 150,
        "Level 3 middle slopes must remain compatible with pine/fir",
    )
    require(
        -17 <= high_temperature <= -3
        and 100 <= high_rainfall <= 255,
        "Level 3 high plateaus must remain compatible with larch",
    )
    require(
        "private const int EndlessForestUpheaval = 255;"
        in worldgen_source
        and '"cliffy rolling hills"' in worldgen_source,
        "Endless Forest must retain its difficult terrain contract",
    )
    require(
        re.search(
            r"RewriteEndlessForestMaps\s*\(.*?"
            r"mapRegion\.ForestMap.*?"
            r"mapRegion\.ShrubMap.*?"
            r"mapRegion\.OceanMap.*?"
            r"mapRegion\.UpheavelMap.*?"
            r"mapRegion\.LandformMap",
            worldgen_source,
            re.DOTALL,
        )
        is not None,
        (
            "Endless Forest must rewrite forest, shrub, ocean, upheaval, "
            "and landform maps together"
        ),
    )
    require(
        "insideEndlessForest" in worldgen_source
        and "handling = EnumHandling.PreventSubsequent;"
        in worldgen_source,
        (
            "Endless Forest must suppress non-tree block patches without "
            "disabling vanilla tree generation"
        ),
    )
    prevent_placement_body = re.search(
        r"public bool PreventPlacementAt\(.*?;"
        r"\s*public bool PreventPlacementBroadlyAt",
        worldgen_source,
        re.DOTALL,
    )
    require(
        prevent_placement_body is not None
        and "EndlessForestLevel" not in prevent_placement_body.group(0),
        (
            "Endless Forest must not be included in broad placement "
            "suppression because that would also suppress its trees"
        ),
    )
    require(
        "WorldMapManager.ToggleMap" in map_restriction_source
        and "ToggleMapPrefix" in map_restriction_source,
        "Level 3 must block both map hotkeys at WorldMapManager.ToggleMap",
    )
    require(
        "TickIntervalMilliseconds = 100" in map_restriction_source
        and "ForceMapClosed();" in map_restriction_source,
        "Level 3 map restriction must react within 100 ms of entry",
    )
    require(
        'private const string MinimapSetting = "showMinimapHud";'
        in map_restriction_source
        and "savedMinimapPreference" in map_restriction_source,
        (
            "Level 3 must disable the minimap temporarily and restore the "
            "player's preference after exit"
        ),
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
    endless_forest_inner = deep_sea_outer
    endless_forest_outer = base_radius + ring_width * 3
    for radius, expected in (
        (endless_forest_inner + epsilon, 3),
        ((endless_forest_inner + endless_forest_outer) / 2, 3),
        (endless_forest_outer, 3),
        (endless_forest_outer + epsilon, 4),
    ):
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
            (
                f"Endless Forest radius {radius} resolved to level "
                f"{actual}, expected {expected}"
            ),
        )

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
        "2,160 angular samples, Level 3 valley/slope/plateau forest climate, "
        "difficult-terrain/map restriction contract, 20,000 deterministic "
        "samples."
    )


if __name__ == "__main__":
    main()
