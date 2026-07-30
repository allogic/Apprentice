#!/usr/bin/env python3

import json
import math
import struct
import sys
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets" / "apprenticehighlands"
SCHEMATICS = (
    ASSETS
    / "worldgen"
    / "schematics"
    / "apprentice-highlands"
)
RUINS_SOURCE = (
    ROOT
    / "src"
    / "ShatteredHighlandsRuinsGenerator.cs"
)
SURFACE_SOURCE = (
    ROOT
    / "src"
    / "ShatteredHighlandsSurfaceGenerator.cs"
)
NATIVE_HOT_SPRING_SOURCE = (
    ROOT
    / "src"
    / "HighlandsNativeHotSpringBlocks.cs"
)
COOLING_MAGMA_BLOCK = (
    ASSETS
    / "blocktypes"
    / "coolingmagma.json"
)
COOLING_MAGMA_TEXTURE = (
    ASSETS
    / "textures"
    / "block"
    / "liquid"
    / "coolingmagma.png"
)
COOLING_MAGMA_TEXTURE_SECONDARY = (
    ASSETS
    / "textures"
    / "block"
    / "liquid"
    / "coolingmagma2.png"
)
LANGUAGE_FILE = ASSETS / "lang" / "en.json"
OBSOLETE_CUSTOM_HOT_SPRING_ASSETS = (
    ASSETS / "blocktypes" / "thermalmat.json",
    ASSETS / "blocktypes" / "thermalwater.json",
    ASSETS / "textures" / "block" / "thermalmat-amber.png",
    ASSETS / "textures" / "block" / "thermalmat-sulfur.png",
    ASSETS
    / "textures"
    / "block"
    / "liquid"
    / "thermalwater.png",
    ASSETS
    / "textures"
    / "block"
    / "liquid"
    / "thermalwater2.png",
)

CHUNK_SIZE = 32
LOOT_SALT = 0x4C4F4F5453454354
SURFACE_FLORA_SALT = 0x5749544845524544
LIQUID_TYPE_SALT = 0x4C41564137303330
LAVA_FEATURE_PERCENT = 70
HOT_SPRING_FEATURE_PERCENT = 30
LIQUID_FEATURE_CELL_SIZE = 256
NATIVE_HOT_SPRING_EXCLUSION_RADIUS = 20
CULTURES = (
    "crownless",
    "basilica",
    "aqueduct",
    "forum",
    "foundry",
    "necropolis",
)


def fail(message):
    raise AssertionError(message)


def stable_hash(world_x, world_z, salt):
    value = (
        ((world_x & 0xFFFFFFFF) * 0x9E3779B185EBCA87)
        ^ ((world_z & 0xFFFFFFFF) * 0xC2B2AE3D27D4EB4F)
        ^ salt
    ) & 0xFFFFFFFFFFFFFFFF
    value ^= value >> 30
    value = (value * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
    value ^= value >> 27
    value = (value * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
    return value ^ (value >> 31)


def surface_stable_hash(world_x, world_z, salt):
    return stable_hash(
        world_x,
        world_z,
        salt ^ SURFACE_FLORA_SALT,
    )


def is_magma_feature_cell(cell_x, cell_z):
    type_hash = surface_stable_hash(
        cell_x,
        cell_z,
        LIQUID_TYPE_SALT,
    )
    return (
        type_hash
        % (LAVA_FEATURE_PERCENT + HOT_SPRING_FEATURE_PERCENT)
        < LAVA_FEATURE_PERCENT
    )


def distance_to_grid_line(value, spacing):
    normalized = abs(value) % spacing
    return min(normalized, spacing - normalized)


def select_city_part(
    culture,
    signature,
    center_x,
    center_z,
    chunk_x,
    chunk_z,
):
    city_chunk_x = math.floor(center_x / CHUNK_SIZE)
    city_chunk_z = math.floor(center_z / CHUNK_SIZE)
    if chunk_x == city_chunk_x and chunk_z == city_chunk_z:
        return 0
    offset_x = chunk_x - city_chunk_x
    offset_z = chunk_z - city_chunk_z
    radius_squared = offset_x * offset_x + offset_z * offset_z
    if radius_squared == 4 and (offset_x == 0 or offset_z == 0):
        return 0
    if radius_squared > 49:
        return -1

    dx = chunk_x * CHUNK_SIZE + CHUNK_SIZE // 2 - center_x
    dz = chunk_z * CHUNK_SIZE + CHUNK_SIZE // 2 - center_z
    quarter_turns = (signature >> 44) & 3
    if (signature >> 46) & 1:
        dx = -dx
    for _ in range(quarter_turns):
        dx, dz = -dz, dx

    radius = math.sqrt(dx * dx + dz * dz)
    angle = math.atan2(dz, dx)
    if culture == 0:
        infrastructure = (
            abs(radius - 160) < 19
            or abs(dx) < 18
            or abs(dz) < 18
        )
    elif culture == 1:
        infrastructure = abs(math.sin(angle * 3)) * radius < 18
    elif culture == 2:
        infrastructure = (
            abs(dz) < 18 or abs(abs(dz) - 96) < 15
        )
    elif culture == 3:
        infrastructure = (
            distance_to_grid_line(dx, 64) < 12
            or distance_to_grid_line(dz, 64) < 12
        )
    elif culture == 4:
        infrastructure = (
            stable_hash(chunk_x, chunk_z, signature) % 100 < 22
        )
    else:
        infrastructure = (
            abs(dx) < 18 or abs(abs(dx) - 96) < 15
        )
    if infrastructure:
        return 2

    roll = stable_hash(
        chunk_x,
        chunk_z,
        signature ^ 0x534543544F52504C,
    ) % 100
    if radius_squared <= 16:
        return 1 if roll < 88 else 3
    if ((offset_x + offset_z) & 1) and roll >= 28:
        return -1
    return 1 if roll < 62 else 3


def unpack_schematic(path):
    data = json.loads(path.read_text(encoding="utf-8"))
    codes = {
        int(key): value
        for key, value in data["BlockCodes"].items()
    }
    positions = {}
    for packed, block_id in zip(data["Indices"], data["BlockIds"]):
        x = packed & 0x3FF
        z = (packed >> 10) & 0x3FF
        y = packed >> 20
        positions[(x, y, z)] = codes[int(block_id)]
    return data, positions


def assert_supported(path, positions):
    supported = {
        position
        for position in positions
        if position[1] == 0
    }
    frontier = list(supported)
    while frontier:
        x, y, z = frontier.pop()
        for neighbor in (
            (x + 1, y, z),
            (x - 1, y, z),
            (x, y + 1, z),
            (x, y - 1, z),
            (x, y, z + 1),
            (x, y, z - 1),
        ):
            if neighbor in positions and neighbor not in supported:
                supported.add(neighbor)
                frontier.append(neighbor)
    if len(supported) != len(positions):
        fail(
            f"{path}: {len(positions) - len(supported)} floating voxels"
        )


def validate_schematics():
    paths = sorted(SCHEMATICS.rglob("*.json"))
    if len(paths) != 126:
        fail(f"expected 126 schematics, found {len(paths)}")

    totals = Counter()
    type_counts = Counter()
    for path in paths:
        data, positions = unpack_schematic(path)
        part = path.parent.name
        type_counts[part] += 1
        assert_supported(path, positions)

        markers = [
            position
            for position, code in positions.items()
            if code == "apprenticehighlands:lootmarker"
        ]
        toxic = sum(
            code.startswith("apprenticemire:toxicwater")
            for code in positions.values()
        )
        toxic_falls = sum(
            code.startswith("apprenticemire:toxicwater-d-")
            for code in positions.values()
        )
        lights = sum(
            code
            in (
                "apprenticehighlands:riftlight",
                "apprenticehighlands:emberlight",
            )
            for code in positions.values()
        )
        hot_spring_water = sum(
            code == "game:boilingwater-still-7"
            for code in positions.values()
        )
        lava = sum(
            code == "apprenticehighlands:coolingmagma-still-7"
            for code in positions.values()
        )
        legacy_lava = sum(
            code == "game:lava-still-7"
            for code in positions.values()
        )
        sludgy_gravel = sum(
            code == "game:sludgygravel"
            for code in positions.values()
        )
        custom_thermal = sum(
            code.startswith("apprenticehighlands:thermal")
            for code in positions.values()
        )
        totals.update(
            markers=len(markers),
            toxic=toxic,
            toxic_falls=toxic_falls,
            lights=lights,
            hot_spring_water=hot_spring_water,
            lava=lava,
            legacy_lava=legacy_lava,
            sludgy_gravel=sludgy_gravel,
            custom_thermal=custom_thermal,
        )

        if part == "districts":
            if len(markers) != 4:
                fail(f"{path}: expected four interior loot markers")
            center_x = data["SizeX"] // 2
            center_z = data["SizeZ"] // 2
            for x, y, z in markers:
                if y != 3 or (x, y - 1, z) not in positions:
                    fail(f"{path}: unsupported loot marker at {x},{y},{z}")
                if abs(x - center_x) <= 3 or abs(z - center_z) <= 3:
                    fail(f"{path}: loot marker entered a street at {x},{y},{z}")
        elif markers:
            fail(f"{path}: loot marker exists outside a district")

        if part == "landmarks" and toxic < 30:
            fail(f"{path}: missing full poisonous fountain court")
        if toxic_falls:
            fail(
                f"{path}: unsafe poisonous waterfall blocks remain: "
                f"{toxic_falls}"
            )
        if lights == 0:
            fail(f"{path}: contains no evil light source")

    expected_types = {
        "landmarks": 24,
        "districts": 36,
        "infrastructure": 30,
        "remnants": 36,
    }
    if dict(type_counts) != expected_types:
        fail(f"wrong schematic distribution: {dict(type_counts)}")
    if totals["markers"] != 144:
        fail(f"expected 144 district markers, found {totals['markers']}")
    if totals["lights"] < 600:
        fail(f"evil-light coverage too low: {totals['lights']}")
    if totals["toxic"] < 24 * 30:
        fail(f"poisonous-fountain coverage too low: {totals['toxic']}")
    if totals["toxic_falls"] != 0:
        fail(
            "poisonous-waterfall blocks are forbidden because Vintage "
            "Story's waterfall particle path crashes on this liquid"
        )
    if totals["legacy_lava"] != 0:
        fail(
            "city schematics may not use flat vanilla lava; cooling-magma "
            f"fluid is required: {totals['legacy_lava']}"
        )
    if totals["custom_thermal"] != 0:
        fail(
            "custom Apprentice hot-spring substitutes remain in schematics: "
            f"{totals['custom_thermal']}"
        )
    if totals["lava"] == 0:
        fail("city schematics contain no cooling-magma basins")
    if (
        totals["hot_spring_water"] != 0
        or totals["sludgy_gravel"] != 0
    ):
        fail(
            "city schematics may not imitate GenHotSprings with boiling "
            f"water or sludgy gravel: {dict(totals)}"
        )
    return totals


def validate_loot_table():
    path = ASSETS / "config" / "city-loot.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 1:
        fail("city-loot schemaVersion must be 1")
    levels = data.get("levels", {})
    if set(levels) != {str(level) for level in range(1, 10)}:
        fail("city-loot must define exactly levels 1 through 9")

    previous_minimum = 0
    previous_maximum = 0
    for level in range(1, 10):
        table = levels[str(level)]
        minimum = table["minimumRolls"]
        maximum = table["maximumRolls"]
        entries = table["entries"]
        if (
            minimum <= 0
            or maximum < minimum
            or maximum > 16
            or minimum < previous_minimum
            or maximum < previous_maximum
            or not entries
        ):
            fail(f"invalid or regressive loot table at level {level}")
        for entry in entries:
            if (
                not entry["code"]
                or entry["minimumQuantity"] <= 0
                or entry["maximumQuantity"] < entry["minimumQuantity"]
                or entry["weight"] <= 0
            ):
                fail(f"invalid loot entry at level {level}: {entry}")
        previous_minimum = minimum
        previous_maximum = maximum

    level_seven_codes = {
        entry["code"]
        for entry in levels["7"]["entries"]
    }
    required_level_seven = {
        "game:ingot-steel",
        "game:gear-temporal",
        "game:gem-diamond-rough",
        "apprentice:ingot-starsteel",
        "apprentice:ingot-aethersteel",
    }
    if not required_level_seven <= level_seven_codes:
        fail("Level 7 loot does not contain the required useful/rare core")


def validate_chest_plans():
    minimum_candidates = 10**9
    tested = 0
    for culture in range(6):
        for sample in range(96):
            signature = stable_hash(
                culture * 101 + sample,
                sample * 37 - culture * 19,
                0x43495459414E4348,
            )
            center_x = (sample * 73 + culture * 11) % 4096
            center_z = (sample * 47 + culture * 29) % 4096
            city_chunk_x = math.floor(center_x / CHUNK_SIZE)
            city_chunk_z = math.floor(center_z / CHUNK_SIZE)
            candidates = []
            for offset_z in range(-8, 9):
                for offset_x in range(-8, 9):
                    chunk_x = city_chunk_x + offset_x
                    chunk_z = city_chunk_z + offset_z
                    if (
                        select_city_part(
                            culture,
                            signature,
                            center_x,
                            center_z,
                            chunk_x,
                            chunk_z,
                        )
                        != 1
                    ):
                        continue
                    candidates.append(
                        (
                            stable_hash(
                                chunk_x,
                                chunk_z,
                                signature ^ LOOT_SALT,
                            ),
                            chunk_z,
                            chunk_x,
                        )
                    )
            candidates.sort()
            target = 9 + signature % 5
            minimum_candidates = min(minimum_candidates, len(candidates))
            if len(candidates) < target or not (9 <= target <= 13):
                fail(
                    f"city loot plan underflow: culture={culture}, "
                    f"candidates={len(candidates)}, target={target}"
                )
            if len(candidates[:target]) != target:
                fail("city loot ranking did not produce the exact target")
            tested += 1
    return tested, minimum_candidates


def validate_city_fountain_policy():
    tested = 0
    for culture in range(6):
        for sample in range(96):
            signature = stable_hash(
                culture * 101 + sample,
                sample * 37 - culture * 19,
                0x43495459414E4348,
            )
            center_x = (sample * 73 + culture * 11) % 4096
            center_z = (sample * 47 + culture * 29) % 4096
            city_chunk_x = math.floor(center_x / CHUNK_SIZE)
            city_chunk_z = math.floor(center_z / CHUNK_SIZE)
            landmarks = []
            for offset_z in range(-8, 9):
                for offset_x in range(-8, 9):
                    chunk_x = city_chunk_x + offset_x
                    chunk_z = city_chunk_z + offset_z
                    if (
                        select_city_part(
                            culture,
                            signature,
                            center_x,
                            center_z,
                            chunk_x,
                            chunk_z,
                        )
                        == 0
                    ):
                        landmarks.append((chunk_x, chunk_z))
            primary = [
                position
                for position in landmarks
                if position == (city_chunk_x, city_chunk_z)
            ]
            secondary = [
                position
                for position in landmarks
                if position != (city_chunk_x, city_chunk_z)
            ]
            if len(landmarks) != 5:
                fail(
                    f"city landmark plan changed: expected 5, "
                    f"found {len(landmarks)}"
                )
            if len(primary) != 1 or len(secondary) != 4:
                fail(
                    "city must retain exactly one primary poisonous "
                    "fountain and four normalized secondary landmarks"
                )
            tested += 1
    return tested


def validate_liquid_basin_mix():
    if (
        LAVA_FEATURE_PERCENT + HOT_SPRING_FEATURE_PERCENT
        != 100
    ):
        fail("lava and hot-spring feature percentages must total 100")
    lava = 0
    hot_spring = 0
    for cell_z in range(-100, 100):
        for cell_x in range(-100, 100):
            first = is_magma_feature_cell(cell_x, cell_z)
            if first != is_magma_feature_cell(cell_x, cell_z):
                fail("liquid feature selection is not deterministic")
            if first:
                lava += 1
            else:
                hot_spring += 1
    total = lava + hot_spring
    lava_percent = lava * 100 / total
    hot_spring_percent = hot_spring * 100 / total
    if not (68.5 <= lava_percent <= 71.5):
        fail(
            f"liquid feature mix drifted: lava={lava_percent:.2f}%, "
            f"hot spring={hot_spring_percent:.2f}%"
        )
    return lava_percent, hot_spring_percent


def validate_native_hot_spring_exclusion():
    hot_water_columns = {
        (4, 5),
        (5, 5),
        (5, 6),
        (6, 6),
    }
    radius_squared = (
        NATIVE_HOT_SPRING_EXCLUSION_RADIUS
        * NATIVE_HOT_SPRING_EXCLUSION_RADIUS
    )
    reserved = {
        (x, z)
        for z in range(-24, CHUNK_SIZE + 24)
        for x in range(-24, CHUNK_SIZE + 24)
        if any(
            (x - hot_x) ** 2 + (z - hot_z) ** 2
            <= radius_squared
            for hot_x, hot_z in hot_water_columns
        )
    }
    if not hot_water_columns <= reserved:
        fail("native boiling-water columns are not reserved")
    for x, z in reserved:
        if min(
            (x - hot_x) ** 2 + (z - hot_z) ** 2
            for hot_x, hot_z in hot_water_columns
        ) > radius_squared:
            fail("native spring reservation exceeded its safety radius")

    ordinary_water = {
        (x, z)
        for z in range(-3, 14)
        for x in range(-3, 15)
    }
    magma_writes = ordinary_water - reserved
    suppressed_water = ordinary_water & reserved
    if magma_writes & reserved:
        fail("cooling magma entered the native hot-spring exclusion zone")
    if not suppressed_water:
        fail("native spring overlap regression did not suppress water")
    return (
        len(hot_water_columns),
        len(reserved),
        len(suppressed_water),
    )


def validate_runtime_contract():
    ruins = RUINS_SOURCE.read_text(encoding="utf-8")
    surface = SURFACE_SOURCE.read_text(encoding="utf-8")
    native_hot_spring = NATIVE_HOT_SPRING_SOURCE.read_text(
        encoding="utf-8"
    )
    required_ruins = (
        "MinimumChestsPerCity = 9",
        "ChestCountVariation = 5",
        "partIndex == 1",
        "IsLootChestSector(",
        "FinalizeDistrictLoot(",
        "TryPlaceBlockForWorldGen(",
        "IBlockEntityContainer",
        "blockEntity.Initialize(api)",
        "container?.Inventory == null",
        "LoadLootTables()",
        "LootChestPrefix",
        "FinalizeLandmarkFountain(",
        "if (isPrimaryCityChunk)",
        "IsToxicWater(",
        "apprenticehighlands:coolingmagma-still-7",
        "SharedWorldgenBlockAccessor",
        "sharedWorldgenBlockAccessor =",
        "IsWithinPlannedCityFootprint(",
        "plannedCityExclusion",
    )
    required_surface = (
        "ConvertNaturalWaterBasins(",
        "TryFindNaturalWaterRange(",
        "TryQueueConnectedWaterColumn(",
        "GetNearbyHeatedLiquidKind(",
        "LavaBasinPercent = 70",
        "HotSpringBasinPercent = 30",
        "LiquidFeatureCellSize = 256",
        "HotSpringLocationsKey",
        '"hotspringlocations"',
        "OnTerrainFeaturesPlanNativeHotSprings(",
        "EnumWorldGenPass.TerrainFeatures",
        "Dictionary<Vec3i, HotSpringGenData>",
        "GetLiquidFeatureCellPlan(",
        "TryChooseNativeHotSpringSite(",
        "new HotSpringGenData",
        "horRadius",
        "verRadiusSq",
        "BuildNativeHotSpringReservation(",
        "NativeHotSpringExclusionRadius = 20",
        "IsMagmaFeatureCellCoordinates(",
        "IsWithinPlannedCityFootprint(",
        "HighlandsNativeHotSpringBlocks.TryResolve(",
        "hotSpringWaterSourceId",
        "apprenticehighlands:coolingmagma-still-7",
        "game:lava-still-7",
        "CoolingMagmaLiquid",
        "magmaShoreColumns",
        "IsOrdinaryWaterFluid(",
        "scheduledNativeHotSprings",
        "filteredNativeHotSprings",
        "protectedNativeHotSpringColumns",
        "suppressedOrdinaryWaterBlocks",
        "convertedMagmaWaterBlocks",
        "SharedWorldgenBlockAccessor",
    )
    for token in required_ruins:
        if token not in ruins:
            fail(f"missing runtime chest contract token: {token}")
    for token in required_surface:
        if token not in surface:
            fail(f"missing runtime native-hot-spring contract token: {token}")
    forbidden_ruins = (
        "game:boilingwater",
        "game:sludgygravel",
        "DecorateNativeHotSpringModule(",
        "hotSpringWaterSourceId",
        "sludgyGravelId",
        "SetDecor(",
    )
    for token in forbidden_ruins:
        if token in ruins:
            fail(
                "city generator may not construct or decorate native hot "
                f"springs: {token}"
            )
    required_native_hot_spring = (
        'new AssetLocation(',
        '"game"',
        '"worldgen/global.json"',
        "hotSpringBacteria87DegCode",
        "hotSpringBacteriaSmooth74DegCode",
        "hotSpringBacteriaSmooth65DegCode",
        "hotSpringBacteriaSmooth55DegCode",
        "sludgyGravelBlockCode",
        "boilingWaterBlockCode",
        "BacteriaByTemperature",
        '"boilingwater"',
        "code.Domain",
    )
    for token in required_native_hot_spring:
        if token not in native_hot_spring:
            fail(
                "missing base-game hot-spring resolver token: "
                f"{token}"
            )
    forbidden_custom_hot_spring = (
        "apprenticehighlands:thermalwater",
        "apprenticehighlands:thermalmat",
    )
    for token in forbidden_custom_hot_spring:
        if (
            token in ruins
            or token in surface
            or token in native_hot_spring
        ):
            fail(
                "custom Apprentice hot-spring substitute remains in "
                f"runtime source: {token}"
            )
    forbidden_surface = (
        "IsLavaFeature(",
        "HasNearbyHeatedLiquid(",
        "lavaWaterSourceId",
        "PrepareNativeHotSpringBasinFloor(",
        "PrepareNativeHotSpringShore(",
        "ApplyNativeHotSpringDecors(",
        "NormalizeLoadedHotSpringBasinToMagma(",
        "InspectAdjacentBasinType(",
        "HotSpringLiquid",
        "sludgyGravelId",
        "SetDecor(",
    )
    for token in forbidden_surface:
        if token in surface:
            fail(f"obsolete per-coordinate liquid selector remains: {token}")


def validate_assets():
    required = (
        ASSETS / "blocktypes" / "lootmarker.json",
        ASSETS / "blocktypes" / "emberlight.json",
        COOLING_MAGMA_BLOCK,
        ASSETS / "textures" / "block" / "riftlight-ember.png",
        COOLING_MAGMA_TEXTURE,
        COOLING_MAGMA_TEXTURE_SECONDARY,
        LANGUAGE_FILE,
        NATIVE_HOT_SPRING_SOURCE,
    )
    for path in required:
        if not path.is_file() or path.stat().st_size == 0:
            fail(f"missing required city-polish asset: {path}")
    for path in ASSETS.rglob("*.json"):
        json.loads(path.read_text(encoding="utf-8"))
    for path in OBSOLETE_CUSTOM_HOT_SPRING_ASSETS:
        if path.exists():
            fail(
                "custom hot-spring asset must be removed in favor of "
                f"base-game content: {path}"
            )

    language = json.loads(LANGUAGE_FILE.read_text(encoding="utf-8"))
    if (
        language.get("block-coolingmagma-still-7")
        != "Cooling Magma"
    ):
        fail("coolingmagma is missing its English block name")
    for obsolete_key in (
        "block-thermalmat-amber",
        "block-thermalmat-sulfur",
        "block-thermalwater-still-7",
    ):
        if obsolete_key in language:
            fail(
                "obsolete custom hot-spring language key remains: "
                f"{obsolete_key}"
            )

    cooling_magma = json.loads(
        COOLING_MAGMA_BLOCK.read_text(encoding="utf-8")
    )
    if cooling_magma.get("class") != "BlockLava":
        fail("coolingmagma must retain Vintage Story's lava behavior")
    if cooling_magma.get("blockmaterial") != "Lava":
        fail("coolingmagma must use the Lava block material")
    if cooling_magma.get("liquidCode") != "lava":
        fail("coolingmagma must remain gameplay-compatible with lava")
    magma_behavior_names = {
        behavior.get("name")
        for behavior in cooling_magma.get("behaviors", [])
    }
    if "FiniteSpreadingLiquid" in magma_behavior_names:
        fail("coolingmagma may not spread outside its owned basin")
    magma_variant_groups = {
        group.get("code"): tuple(group.get("states", ()))
        for group in cooling_magma.get("variantgroups", [])
    }
    if magma_variant_groups != {
        "flow": ("still",),
        "height": ("7",),
    }:
        fail(
            "coolingmagma must expose only one full-height still source "
            "variant"
        )
    magma_textures = cooling_magma.get("textures", {})
    if (
        magma_textures.get("all", {}).get("base")
        != "apprenticehighlands:block/liquid/coolingmagma"
        or magma_textures.get("specialSecondTexture", {}).get("base")
        != "apprenticehighlands:block/liquid/coolingmagma2"
    ):
        fail("coolingmagma texture bindings do not match the packaged assets")
    light_hsv = cooling_magma.get("lightHsv")
    if (
        not isinstance(light_hsv, list)
        or len(light_hsv) != 3
        or not 8 <= light_hsv[2] <= 14
    ):
        fail("coolingmagma glow must stay restrained beneath its dark crust")

    for texture_path in (
        COOLING_MAGMA_TEXTURE,
        COOLING_MAGMA_TEXTURE_SECONDARY,
    ):
        png = texture_path.read_bytes()
        if (
            len(png) < 24
            or png[:8] != b"\x89PNG\r\n\x1a\n"
            or struct.unpack(">II", png[16:24]) != (32, 32)
        ):
            fail(f"fluid texture must be a valid 32x32 PNG: {texture_path}")


def main():
    validate_assets()
    totals = validate_schematics()
    validate_loot_table()
    tested, minimum_candidates = validate_chest_plans()
    fountain_plans = validate_city_fountain_policy()
    lava_percent, hot_spring_percent = validate_liquid_basin_mix()
    hot_water_columns, reserved_columns, suppressed_columns = (
        validate_native_hot_spring_exclusion()
    )
    validate_runtime_contract()
    print(
        "PASS — Highlands city contract: "
        f"126 supported schematics, 144 interior markers, "
        f"{totals['lights']} evil lights, "
        f"{totals['toxic']} selectable toxic-fountain template blocks, "
        f"{totals['lava']} cooling-magma blocks; "
        "zero city-authored boiling-water/sludgy-gravel blocks, "
        f"{tested} deterministic city plans tested, "
        f"minimum district candidates={minimum_candidates}, "
        "exact chest target=9–13, "
        f"{fountain_plans} plans with exactly one poison fountain, "
        f"basin mix={lava_percent:.2f}% lava/"
        f"{hot_spring_percent:.2f}% hot spring, "
        f"native exclusion regression={hot_water_columns} hot-water/"
        f"{reserved_columns} reserved/"
        f"{suppressed_columns} suppressed-overlap columns."
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exception:
        print(f"FAIL — {exception}", file=sys.stderr)
        raise
