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
ICE_SPIKE_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "FrozenExpanseIceSpikeGenerator.cs"
)
POISON_MIRE_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ConcentricRealmWorldgenSystem.PoisonMire.cs"
)
POISON_MIRE_ENVIRONMENT_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "PoisonMireEnvironmentGenerator.cs"
)
SHATTERED_HIGHLANDS_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ConcentricRealmWorldgenSystem.ShatteredHighlands.cs"
)
SHATTERED_HIGHLANDS_SURFACE_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ShatteredHighlandsSurfaceGenerator.cs"
)
SHATTERED_HIGHLANDS_RUINS_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ShatteredHighlandsRuinsGenerator.cs"
)
SHATTERED_HIGHLANDS_STRUCTURES_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "worldgen"
    / "structures.json"
)
SHATTERED_HIGHLANDS_CITIES_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "worldgen"
    / "cities.json"
)
SHATTERED_HIGHLANDS_SCHEMATIC_ROOT = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "worldgen"
    / "schematics"
    / "apprentice-highlands"
)
SHATTERED_HIGHLANDS_GENERATOR_PATH = (
    REPOSITORY_ROOT
    / "tools"
    / "generate_highlands_ruins.py"
)
SHATTERED_HIGHLANDS_ASHEN_TEXTURE_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "textures"
    / "block"
    / "ashenweed.png"
)
SHATTERED_HIGHLANDS_THORN_TEXTURE_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "textures"
    / "block"
    / "wraiththorn.png"
)
SHATTERED_HIGHLANDS_ASHEN_BLOCK_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "blocktypes"
    / "ashenweed.json"
)
SHATTERED_HIGHLANDS_THORN_BLOCK_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticehighlands"
    / "blocktypes"
    / "wraiththorn.json"
)
POISON_MIRE_MIST_BLOCK_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticemire"
    / "blocktypes"
    / "miremist.json"
)
POISON_MIRE_MIST_LANGUAGE_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticemire"
    / "lang"
    / "en.json"
)
POISON_MIRE_TOXIC_WATER_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprenticemire"
    / "blocktypes"
    / "toxicwater.json"
)
REALM_PROGRESSION_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "config"
    / "realm-progression.json"
)
REALM_PROGRESSION_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "RealmProgressionSystem.cs"
)
MAP_RESTRICTION_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "EndlessForestMapRestrictionSystem.cs"
)
COMMAND_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ItemCalibrationSystem.cs"
)
COMMAND_REGISTRATION_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "ApprenticeServerCommandRegistration.cs"
)
ECOLOGY_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "EcologyWorldgenSystem.cs"
)
DANGER_SOURCE_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "DangerTierSystem.cs"
)
PROJECT_PATH = REPOSITORY_ROOT / "Apprentice.csproj"
LANGUAGE_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "lang"
    / "en.json"
)
POISON_RECIPE_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "recipes"
    / "barrel"
    / "poison-brewing.json"
)
VENOMBERRY_PLANT_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "blocktypes"
    / "2.7"
    / "venomberryplant.json"
)
GLOAMCAP_PLANT_PATH = (
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "blocktypes"
    / "2.7"
    / "gloamcapplant.json"
)
LEGACY_VENOMBERRY_PATHS = [
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "patches"
    / "2.7"
    / "venomberry-fruitingbush.json",
    REPOSITORY_ROOT
    / "assets"
    / "apprentice"
    / "shapes"
    / "block"
    / "2.7"
    / "venomberry-fruitingbush.json",
]

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


def shattered_highlands_cell_hash(cell_x: int, cell_z: int) -> int:
    mask = (1 << 64) - 1
    value = (
        ((cell_x & 0xFFFFFFFF) * 0x9E3779B185EBCA87)
        ^ ((cell_z & 0xFFFFFFFF) * 0xC2B2AE3D27D4EB4F)
        ^ 0x5348415454455245
    ) & mask
    value ^= value >> 30
    value = (value * 0xBF58476D1CE4E5B9) & mask
    value ^= value >> 27
    value = (value * 0x94D049BB133111EB) & mask
    value ^= value >> 31
    return value & mask


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
    ice_spike_source = ICE_SPIKE_SOURCE_PATH.read_text(encoding="utf-8")
    poison_mire_source = POISON_MIRE_SOURCE_PATH.read_text(encoding="utf-8")
    poison_mire_environment_source = (
        POISON_MIRE_ENVIRONMENT_SOURCE_PATH.read_text(encoding="utf-8")
    )
    shattered_highlands_source = (
        SHATTERED_HIGHLANDS_SOURCE_PATH.read_text(encoding="utf-8")
    )
    shattered_highlands_surface_source = (
        SHATTERED_HIGHLANDS_SURFACE_SOURCE_PATH.read_text(
            encoding="utf-8"
        )
    )
    shattered_highlands_ruins_source = (
        SHATTERED_HIGHLANDS_RUINS_SOURCE_PATH.read_text(
            encoding="utf-8"
        )
    )
    shattered_highlands_generator_source = (
        SHATTERED_HIGHLANDS_GENERATOR_PATH.read_text(
            encoding="utf-8"
        )
    )
    shattered_highlands_ashen_block_source = (
        SHATTERED_HIGHLANDS_ASHEN_BLOCK_PATH.read_text(
            encoding="utf-8"
        )
    )
    shattered_highlands_thorn_block_source = (
        SHATTERED_HIGHLANDS_THORN_BLOCK_PATH.read_text(
            encoding="utf-8"
        )
    )
    shattered_highlands_structures = json.loads(
        SHATTERED_HIGHLANDS_STRUCTURES_PATH.read_text(
            encoding="utf-8"
        )
    )
    shattered_highlands_cities = json.loads(
        SHATTERED_HIGHLANDS_CITIES_PATH.read_text(
            encoding="utf-8"
        )
    )
    poison_mire_mist_block = json.loads(
        POISON_MIRE_MIST_BLOCK_PATH.read_text(encoding="utf-8")
    )
    poison_mire_mist_language = json.loads(
        POISON_MIRE_MIST_LANGUAGE_PATH.read_text(encoding="utf-8")
    )
    poison_mire_toxic_water = json.loads(
        POISON_MIRE_TOXIC_WATER_PATH.read_text(encoding="utf-8")
    )
    realm_progression = json.loads(
        REALM_PROGRESSION_PATH.read_text(encoding="utf-8")
    )
    realm_progression_source = REALM_PROGRESSION_SOURCE_PATH.read_text(
        encoding="utf-8"
    )
    map_restriction_source = MAP_RESTRICTION_SOURCE_PATH.read_text(
        encoding="utf-8"
    )
    ecology_source = ECOLOGY_SOURCE_PATH.read_text(encoding="utf-8")
    danger_source = DANGER_SOURCE_PATH.read_text(encoding="utf-8")
    command_source = COMMAND_SOURCE_PATH.read_text(encoding="utf-8")
    command_registration_source = (
        COMMAND_REGISTRATION_SOURCE_PATH.read_text(encoding="utf-8")
    )
    project_source = PROJECT_PATH.read_text(encoding="utf-8")
    language = json.loads(LANGUAGE_PATH.read_text(encoding="utf-8"))
    poison_recipes = json.loads(
        POISON_RECIPE_PATH.read_text(encoding="utf-8")
    )
    venomberry_plant = json.loads(
        VENOMBERRY_PLANT_PATH.read_text(encoding="utf-8")
    )
    gloamcap_plant = json.loads(
        GLOAMCAP_PLANT_PATH.read_text(encoding="utf-8")
    )

    ecology = {
        entry["Id"]: entry
        for entry in config["Ecology"]
    }
    require(
        ecology["venomberry"]["AllowedLevels"] == [3, 6],
        "Venomberry must be limited to Endless Forest and Poison Mire",
    )
    require(
        ecology["gloamcap"]["AllowedLevels"] == [4, 6],
        "Gloamcap must be limited to Shadow Forest and Poison Mire",
    )
    require(
        ecology["venomberry"]["WorldgenBlockCode"]
        == "apprentice:venomberryplant",
        "Venomberry worldgen must place the Wild Venomberry bush block",
    )
    require(
        ecology["gloamcap"]["WorldgenBlockCode"]
        == "apprentice:gloamcapplant",
        "Gloamcap worldgen must place the Wild Gloamcap block",
    )
    require(
        all(not path.exists() for path in LEGACY_VENOMBERRY_PATHS),
        (
            "Obsolete Venomberry fruiting-bush patch/shape files must be "
            "deleted from the clean source tree"
        ),
    )
    require(
        project_source.count(
            "assets\\apprentice\\patches\\2.7\\"
            "venomberry-fruitingbush.json"
        ) >= 2
        and project_source.count(
            "assets\\apprentice\\shapes\\block\\2.7\\"
            "venomberry-fruitingbush.json"
        ) >= 2,
        (
            "The build must exclude both retired Venomberry assets when a "
            "current update is extracted over an older source tree"
        ),
    )
    require(
        not any(
            "fruitingbush" in key and "venomberry" in key
            for key in language
        ),
        "Obsolete vanilla-style Venomberry localization must be absent",
    )
    require(
        venomberry_plant["code"] == "venomberryplant"
        and venomberry_plant["drops"][0]["code"]
        == "apprentice:venomberry",
        (
            "Wild Venomberry bush must drop only the harvested Venomberry "
            "ingredient"
        ),
    )
    require(
        gloamcap_plant["code"] == "gloamcapplant"
        and gloamcap_plant["drops"][0]["code"]
        == "apprentice:gloamcap",
        (
            "Wild Gloamcap must drop only the harvested Gloamcap ingredient"
        ),
    )
    recipe_ingredient_codes = {
        ingredient.get("code")
        for recipe in poison_recipes
        for ingredient in recipe.get("ingredients", [])
    }
    require(
        {
            "apprentice:venomberry",
            "apprentice:gloamcap",
        }.issubset(recipe_ingredient_codes),
        (
            "Both harvested plant ingredients must remain connected to "
            "poison recipes"
        ),
    )
    require(
        ecology["dangerous-tissue"]["AllowedLevels"]
        == [4, 5, 6, 7, 8, 9, 10],
        "Dangerous Tissue must be limited to hostile drops at Levels 4-10",
    )
    require(
        ecology["venomberry"]["EntityDropEnabled"] is False
        and ecology["venomberry"]["ChancePerTier"] == 0,
        "Venomberry must be worldgen/harvest only, never generic entity loot",
    )
    require(
        ecology["gloamcap"]["EntityDropEnabled"] is False
        and ecology["gloamcap"]["ChancePerTier"] == 0,
        "Gloamcap must be worldgen/harvest only, never generic entity loot",
    )
    require(
        ecology["dangerous-tissue"]["EntityDropEnabled"] is True
        and ecology["dangerous-tissue"]["ChancePerTier"] > 0,
        "Dangerous Tissue must remain enabled as plausible creature loot",
    )
    require(
        "GetAllowedLevelOrdinal(tier)" in ecology_source
        and "GetAllowedLevelOrdinal(tier)" in danger_source,
        "AllowedLevels must guard both plant generation and hostile drops",
    )
    require(
        "if (!loot.EntityDropEnabled) continue;" in danger_source,
        (
            "Generic creature deaths must reject ecology entries that do not "
            "explicitly opt in to entity drops"
        ),
    )
    require(
        "api.WorldManager.PeekChunkColumn(" in ecology_source
        and "new ChunkPeekOptions" in ecology_source
        and "ScanGeneratedColumn(" in ecology_source,
        (
            "Ecology diagnostics must run the real worldgen pipeline on "
            "temporary PeekChunkColumn output and scan the completed blocks"
        ),
    )
    require(
        '"Wild Venomberry bush"' in ecology_source
        and '"Wild Gloamcap"' in ecology_source
        and "result.WorldgenBlockCode" in ecology_source
        and "wild blocks found" in ecology_source,
        (
            "Ecology diagnostics must distinguish exact wild plant blocks "
            "from their harvested ingredient items"
        ),
    )
    require(
        '"log-grown-"' in ecology_source
        and '"leaves"' in ecology_source
        and "Shadow Forest trunks" in ecology_source
        and "tree-covered columns" in ecology_source,
        (
            "The ecology probe must measure real Shadow Forest trunks and "
            "canopy coverage so performance tuning cannot silently make "
            "Level 4 sparse"
        ),
    )
    require(
        "ProbeCandidatesPerDefinition = 12" in ecology_source
        and "HasPredictedRoll(" in ecology_source
        and "trace.RollHits++" in ecology_source,
        (
            "Ecology diagnostics must preselect deterministic successful "
            "rolls and compare prediction with the live worldgen trace"
        ),
    )
    require(
        "data[index] = blockId;" in ecology_source
        and "SetBlockUnsafe(index, blockId)" not in ecology_source,
        (
            "Ecology placement must allocate an empty chunk-section palette "
            "safely when the surface is on a section boundary"
        ),
    )
    require(
        'BeginSubCommand("ecology")' in command_source
        and 'BeginSubCommand("probe")' in command_source
        and "ecologyWorldgenSystem.StartWorldgenProbe" in command_source,
        (
            "The non-destructive ecology probe must be available through "
            "/apprentice ecology probe"
        ),
    )
    require(
        "RequiresPrivilege(Privilege.controlserver)" in command_source
        and "RequiresPrivilege(Privilege.chat)" in command_source
        and "RequiresPlayer()" in command_source
        and "no saved or loaded chunks are modified"
        in command_source.lower(),
        (
            "The ecology probe must support the server console, remain "
            "administrator-only, preserve player-only commands, and "
            "document its non-destructive contract"
        ),
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
    require(
        "private const int ShadowForestLevel = 4;" in worldgen_source,
        "Shadow Forest must remain Level 4",
    )
    require(
        "private const int ShadowForestDensity = 192;"
        in worldgen_source
        and "private const int ShadowForestShrubDensity = 196;"
        in worldgen_source,
        (
            "Shadow Forest must retain its dense canopy at the performance "
            "budget and keep restrained shrubs"
        ),
    )
    require(
        "private const int ShadowForestUpheaval = 176;"
        in worldgen_source
        and '"flathillvalley"' in worldgen_source,
        "Shadow Forest must use lower-relief valley terrain than Level 3",
    )
    require(
        "private const int ShadowForestOuterTransitionWidth = 128;"
        in worldgen_source
        and "GetShadowForestOuterBlend" in worldgen_source
        and "ApplyShadowForestLandformMap" in worldgen_source
        and "ApplyShadowForestUpheavalMap" in worldgen_source,
        (
            "Shadow Forest must retain a 128-block outer terrain transition "
            "into the Frozen Expanse boundary"
        ),
    )
    require(
        "GetShadowForestDensity" in worldgen_source
        and "StableCellHash" in worldgen_source,
        "Shadow Forest clearings must be deterministic and seam-free",
    )
    require(
        re.search(
            r"RewriteShadowForestMaps\s*\(.*?"
            r"mapRegion\.ClimateMap.*?"
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
            "Shadow Forest must rewrite climate, forest, shrub, ocean, "
            "upheaval and landform maps together"
        ),
    )
    require(
        "insideShadowForest" in worldgen_source,
        "Shadow Forest must suppress ordinary non-tree surface patches",
    )
    require(
        "private const int FrozenExpanseLevel = 5;" in worldgen_source,
        "Frozen Expanse must remain Level 5",
    )
    require(
        "private const int FrozenExpanseTemperatureCelsius = -20;"
        in worldgen_source
        and "private const int FrozenExpanseRainfall = 96;"
        in worldgen_source,
        (
            "Frozen Expanse must select Vintage Story's snow and glacier "
            "block layers at every tested altitude"
        ),
    )
    require(
        adjusted_temperature(-20, 110) <= -16
        and adjusted_temperature(-20, 304) <= -16,
        "Frozen Expanse climate must stay below the snow-block threshold",
    )
    require(
        "private const int FrozenExpanseUpheaval = 128;"
        in worldgen_source
        and '"cold glaciers"' in worldgen_source,
        (
            "Frozen Expanse must use the real cold-glaciers landform with "
            "a moderate ridge budget"
        ),
    )
    require(
        "private const int FrozenExpanseTransitionWidth = 192;"
        in worldgen_source
        and "GetFrozenExpanseBlend" in worldgen_source
        and "ApplyFrozenExpanseClimateMap" in worldgen_source
        and "ApplyFrozenExpanseLandformMap" in worldgen_source,
        (
            "Frozen Expanse must ease climate and terrain over 192 blocks "
            "at both ring boundaries"
        ),
    )
    require(
        re.search(
            r"RewriteFrozenExpanseMaps\s*\(.*?"
            r"mapRegion\.ClimateMap.*?"
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
            "Frozen Expanse must rewrite climate, forest, shrub, ocean, "
            "upheaval and landform maps together"
        ),
    )
    require(
        "insideFrozenExpanse" in worldgen_source,
        "Frozen Expanse must suppress ordinary surface clutter",
    )
    require(
        "StartFrozenExpanseProbe" in worldgen_source
        and "PeekChunkColumn" in worldgen_source
        and "FrozenSurfaceColumns" in worldgen_source
        and "OpenWaterSurfaceColumns" in worldgen_source
        and "CaveColumns" in worldgen_source
        and 'BeginSubCommand("frozen")' in command_registration_source
        and 'BeginSubCommand("probe")' in command_registration_source,
        (
            "Frozen Expanse needs an administrator-only real-worldgen probe "
            "that measures the completed scratch chunks"
        ),
    )
    require(
        "private const int PoisonMireLevel = 6;"
        in poison_mire_source,
        "Poison Mire must remain Level 6",
    )
    require(
        "private const int PoisonMireTemperatureCelsius = 20;"
        in poison_mire_source
        and "private const int PoisonMireRainfall = 210;"
        in poison_mire_source,
        "Poison Mire must remain warm and wet without a bright tropical tint",
    )
    require(
        "private const int PoisonMireForestDensity = 0;"
        in poison_mire_source
        and "private const int PoisonMireShrubDensity = 0;"
        in poison_mire_source,
        (
            "Poison Mire must prevent vanilla forests and shrubs from "
            "reintroducing living green flora"
        ),
    )
    require(
        "private const int PoisonMireUpheaval = 32;"
        in poison_mire_source
        and 'PoisonMireLandformCode = "marsh";'
        in poison_mire_source,
        (
            "Poison Mire must use low-relief vanilla marsh terrain "
            "instead of a custom per-tick terrain simulation"
        ),
    )
    require(
        "private const int PoisonMireTransitionWidth = 192;"
        in poison_mire_source
        and "GetPoisonMireBlend" in poison_mire_source
        and "ApplyPoisonMireClimateMap" in poison_mire_source
        and "ApplyPoisonMireLandformMap" in poison_mire_source,
        (
            "Poison Mire must ease climate and terrain over 192 blocks at "
            "both ring boundaries"
        ),
    )
    require(
        re.search(
            r"RewritePoisonMireMaps\s*\(.*?"
            r"mapRegion\.ClimateMap.*?"
            r"mapRegion\.ForestMap.*?"
            r"mapRegion\.ShrubMap.*?"
            r"mapRegion\.OceanMap.*?"
            r"mapRegion\.UpheavelMap.*?"
            r"mapRegion\.LandformMap",
            poison_mire_source,
            re.DOTALL,
        )
        is not None,
        (
            "Poison Mire must rewrite climate, forest, shrub, ocean, "
            "upheaval and landform maps together"
        ),
    )
    require(
        "ClearLevelMap(" in poison_mire_source
        and "mapRegion.OceanMap" in poison_mire_source
        and "IsFreshWater" in poison_mire_source
        and "IsSaltWater" in poison_mire_source,
        (
            "Poison Mire must clear oceanicity and explicitly distinguish "
            "fresh water from salt water in its runtime probe"
        ),
    )
    require(
        "insidePoisonMire" in worldgen_source
        and "PoisonMireLevel" in worldgen_source,
        (
            "Poison Mire must suppress ordinary non-tree surface clutter "
            "and vanilla structures during terrain approval"
        ),
    )
    require(
        "StartPoisonMireProbe" in poison_mire_source
        and "PeekChunkColumn" in poison_mire_source
        and "DryLandColumns" in poison_mire_source
        and "ShallowFreshWaterColumns" in poison_mire_source
        and "TraversableDryColumns" in poison_mire_source
        and 'BeginSubCommand("mire")' in command_registration_source
        and "/apprentice mire probe" in command_registration_source,
        (
            "Poison Mire needs an administrator-only real-worldgen probe for "
            "bog islands, routes and shallow fresh water"
        ),
    )
    require(
        "RegisterGameTickListener" not in poison_mire_source
        and "RegisterRenderer" not in poison_mire_source
        and "SetBlock(" not in poison_mire_source,
        (
            "Poison Mire terrain must remain one-time map worldgen with no "
            "runtime loop or custom block sculpting"
        ),
    )
    require(
        "internal sealed class PoisonMireEnvironmentGenerator"
        in poison_mire_environment_source
        and "poisonMireEnvironmentGenerator.OnChunkColumnGeneration"
        in worldgen_source
        and "EnumWorldGenPass.NeighbourSunLightFlood"
        in worldgen_source,
        (
            "Poison Mire conversion must run after vanilla vegetation as an "
            "isolated one-time new-chunk environment pass"
        ),
    )
    require(
        "BoundaryExclusionWidth = 192"
        in poison_mire_environment_source
        and "WorldZoneLayout.IsInsideLevelCore("
        in poison_mire_environment_source
        and "PoisonMireLevel" in poison_mire_environment_source,
        (
            "Poison Mire environment landmarks must stay outside both "
            "192-block realm transitions"
        ),
    )
    require(
        "DeadTreeFieldCellSize = 384"
        in poison_mire_environment_source
        and "DeadTreeFieldMinimumRadius = 96"
        in poison_mire_environment_source
        and "DeadTreeFieldMaximumRadius = 138"
        in poison_mire_environment_source
        and "IsInDeadTreeField" in poison_mire_environment_source
        and 'TryResolveDeadLogPalette(\n                    "rotten"'
        in poison_mire_environment_source
        and 'TryResolveDeadLogPalette(\n                    "veryrotten"'
        in poison_mire_environment_source,
        (
            "Dead-tree zones must be deterministic clustered rotten-log "
            "landmarks rather than pale uniform per-chunk clutter"
        ),
    )
    require(
        '"apprenticemire:mirepeat"'
        in poison_mire_environment_source
        and '"apprenticemire:miremud"'
        in poison_mire_environment_source
        and '"apprenticemire:mireash"'
        in poison_mire_environment_source
        and '"apprenticemire:miresulfur"'
        in poison_mire_environment_source
        and "ConvertWaterAndGround" in poison_mire_environment_source
        and "SetFluidBlock" in poison_mire_environment_source
        and "apprenticemire:miremist" in poison_mire_environment_source,
        (
            "Every core surface and fresh-water block must be converted to "
            "the fixed non-green wasteland palette"
        ),
    )
    require(
        '"apprenticemire:deadgrass"'
        in poison_mire_environment_source
        and '"apprenticemire:deadreeds"'
        in poison_mire_environment_source
        and '"apprenticemire:thornbush"'
        in poison_mire_environment_source
        and '"apprenticemire:rottedstump"'
        in poison_mire_environment_source
        and '"apprenticemire:fallenbranch"'
        in poison_mire_environment_source
        and '"apprenticemire:fungalcrust"'
        in poison_mire_environment_source
        and "RemoveLivingFlora" in poison_mire_environment_source
        and "GenerateDeadPlantsAndMist"
        in poison_mire_environment_source,
        "Poison Mire must replace living flora with several dead-plant forms",
    )
    require(
        "StableHash(" in poison_mire_environment_source
        and "Random" not in poison_mire_environment_source
        and "BlockAccessor.SetBlock" not in poison_mire_environment_source
        and "RegisterGameTickListener"
        not in poison_mire_environment_source
        and "RegisterRenderer" not in poison_mire_environment_source,
        (
            "Poison Mire environment generation must be deterministic, "
            "chunk-local and free of ongoing runtime work"
        ),
    )
    require(
        "EnvironmentGeneratorMilliseconds" in poison_mire_source
        and "GeneratedDeadTrees" in poison_mire_source
        and "GeneratedMirePlantBlocks" in poison_mire_source
        and "GeneratedToxicFloorColumns" in poison_mire_source
        and "GeneratedToxicWaterBlocks" in poison_mire_source
        and "ScannedLivingFloraBlocks" in poison_mire_source
        and "ScannedHealthyGrassSurfaces" in poison_mire_source
        and "VanillaFreshWaterColumns" in poison_mire_source
        and "GeneratedMistEmitters" in poison_mire_source
        and "TryTakeProbeTrace" in poison_mire_source,
        (
            "/apprentice mire probe must validate the environment layer and "
            "measure its generation time on real scratch chunks"
        ),
    )
    require(
        poison_mire_mist_block["code"] == "miremist"
        and poison_mire_mist_block["collisionbox"] is None
        and poison_mire_mist_block["selectionbox"] is None
        and poison_mire_mist_block["replaceable"] == 10000
        and len(poison_mire_mist_block["particleProperties"]) == 1
        and "attributes" not in poison_mire_mist_block
        and "behaviors" not in poison_mire_mist_block
        and poison_mire_mist_language.get("block-miremist")
        == "Mire Mist",
        (
            "Mire mist must be a pass-through visual emitter with no damage, "
            "interaction or gameplay behavior"
        ),
    )
    require(
        poison_mire_toxic_water["code"] == "toxicwater"
        and poison_mire_toxic_water["liquidCode"] == "toxicwater"
        and "climateColorMap" not in poison_mire_toxic_water
        and poison_mire_toxic_water["classByType"]["toxicwater-still-*"]
        == "ApprenticeToxicWater"
        and poison_mire_toxic_water["attributes"]["apprenticePoison"]
        == "toxicwater",
        (
            "Toxicwater must be an untinted custom liquid with its own "
            "interaction class and shared poison identity"
        ),
    )
    toxic_poison = next(
        poison
        for poison in config["Poisons"]
        if poison["Id"] == "toxicwater"
    )
    require(
        toxic_poison["DamagePerSecond"] == 0.6
        and toxic_poison["DurationSeconds"] == 24
        and toxic_poison["MaximumDurationSeconds"] == 36
        and toxic_poison["ArrowCode"]
        == "apprentice:arrow-poison-toxicwater",
        "Toxicwater must be the exact tier above Grandmaster Poison",
    )
    require(
        realm_progression["SchemaVersion"] == 1
        and [entry["Level"] for entry in realm_progression["Levels"]]
        == list(range(1, 11))
        and realm_progression["Levels"][5]["Name"] == "Poison Mire"
        and realm_progression["Levels"][5]["RecipeIds"]
        == ["apprentice:coat-arrows-toxicwater"],
        (
            "Realm discovery configuration must cover levels 1-10 and assign "
            "the Toxicwater arrow interaction to Poison Mire"
        ),
    )
    require(
        "ModSystemSurvivalHandbook" in realm_progression_source
        and "OnInitCustomPages" in realm_progression_source
        and "handbook-realm-locked-text" in realm_progression_source
        and "RealmProgressionRuntime.Discover" in realm_progression_source
        and "WatchedAttributes.MarkPathDirty" in realm_progression_source
        and 'ApplyPoison(\n                        entity,\n                        "toxicwater"'
        in realm_progression_source
        and "BlockApprenticeToxicWater" in realm_progression_source,
        (
            "Discovery must persist per player, unlock existing Survival "
            "Handbook guides and share Toxicwater poison between contact and arrows"
        ),
    )
    expected_ice_field_coverage = (
        math.pi * ((150 + 205) / 2) ** 2 / 640**2
    )
    require(
        0.20 <= expected_ice_field_coverage <= 0.30,
        "Ice-Spike Fields must occupy roughly 20-30% of Level 5",
    )
    require(
        "internal const int FieldCellSize = 640;"
        in ice_spike_source
        and "internal const int FieldMinimumRadius = 150;"
        in ice_spike_source
        and "internal const int FieldMaximumRadius = 205;"
        in ice_spike_source
        and "ExpectedFieldCoverageFraction" in ice_spike_source,
        "Ice-Spike Fields must retain the approved large-region distribution",
    )
    require(
        "internal const int MainSpikeMinimumHeight = 35;"
        in ice_spike_source
        and "internal const int MainSpikeMaximumHeight = 60;"
        in ice_spike_source
        and "internal const int MediumSpikeMinimumHeight = 22;"
        in ice_spike_source
        and "internal const int MediumSpikeMaximumHeight = 36;"
        in ice_spike_source
        and "internal const int SmallSpikeMinimumHeight = 8;"
        in ice_spike_source
        and "internal const int SmallSpikeMaximumHeight = 20;"
        in ice_spike_source
        and "int mainCount = 1 +" in ice_spike_source
        and "int mediumCount = 3 +" in ice_spike_source
        and "int smallCount = 7 +" in ice_spike_source,
        "Ice-Spike Fields must keep dominant, medium and satellite peaks",
    )
    require(
        "game:glacierice" in ice_spike_source
        and "game:packedglacierice" in ice_spike_source
        and "data[blockIndex] = blockId;" in ice_spike_source
        and "data.SetFluid(blockIndex, 0);" in ice_spike_source,
        "Ice spikes must use resolved vanilla glacier blocks",
    )
    require(
        "BoundaryExclusionWidth = 192" in ice_spike_source
        and "FieldFitsRealmCore" in ice_spike_source
        and "BoundaryExclusionWidth +" in ice_spike_source,
        "Ice-Spike Fields must remain outside both Level 5 transitions",
    )
    require(
        "StableHash(" in ice_spike_source
        and "GetIntersectingSpikes(" in ice_spike_source
        and "NeighbourCellPadding" in ice_spike_source
        and "spikes.Sort(" in ice_spike_source
        and "Random" not in ice_spike_source
        and "BlockAccessor.SetBlock" not in ice_spike_source,
        (
            "Ice spikes must be deterministic, chunk-local and independent "
            "of neighboring generation order"
        ),
    )
    require(
        "WorldGenTerrainHeightMap" in ice_spike_source
        and "RainHeightMap" in ice_spike_source
        and "mapChunk.YMax = yMax;" in ice_spike_source,
        "Ice spikes must update all terrain height contracts",
    )
    require(
        "OnChunkColumnGeneration" in ice_spike_source
        and "EnumWorldGenPass.Vegetation" in worldgen_source
        and "RegisterGameTickListener" not in ice_spike_source
        and "RegisterRenderer" not in ice_spike_source,
        "Ice spikes must be one-time world generation with no runtime loop",
    )
    require(
        "LocateNearestIceSpikeField" in worldgen_source
        and "StartIceSpikeProbe" in worldgen_source
        and 'BeginSubCommand("spikes")'
        in command_registration_source
        and 'BeginSubCommand("locate")'
        in command_registration_source
        and "/apprentice frozen spikes locate"
        in command_registration_source
        and "/apprentice frozen spikes probe"
        in command_registration_source,
        (
            "Ice-Spike Fields need locate and non-destructive shape, "
            "continuity, navigation and timing diagnostics"
        ),
    )
    require(
        "private const int ShatteredHighlandsLevel = 7;"
        in shattered_highlands_source,
        "Shattered Highlands must remain Level 7",
    )
    require(
        "private const int ShatteredHighlandsTemperatureCelsius = 8;"
        in shattered_highlands_source
        and "private const int ShatteredHighlandsRainfall = 72;"
        in shattered_highlands_source
        and "private const int ShatteredHighlandsForestDensity = 0;"
        in shattered_highlands_source
        and "private const int ShatteredHighlandsShrubDensity = 0;"
        in shattered_highlands_source,
        (
            "Shattered Highlands must remain cold, exposed and free of "
            "ordinary forest/shrub cover during terrain approval"
        ),
    )
    require(
        "private const int ShatteredHighlandsUpheaval = 255;"
        in shattered_highlands_source
        and 'ShatteredHighlandsPlateauLandformCode =\n'
        '            "realisticmountains-quintupleledged";'
        in shattered_highlands_source
        and 'ShatteredHighlandsRiftLandformCode =\n'
        '            "steppedsinkholes";'
        in shattered_highlands_source,
        (
            "Shattered Highlands must combine maximum upheaval, ledged "
            "plateaus and stepped rifts without floating-island terrain"
        ),
    )
    require(
        "private const int ShatteredHighlandsTransitionWidth = 192;"
        in shattered_highlands_source
        and "GetShatteredHighlandsBlend" in shattered_highlands_source
        and "ApplyShatteredHighlandsClimateMap"
        in shattered_highlands_source
        and "ApplyShatteredHighlandsLandformMap"
        in shattered_highlands_source,
        (
            "Shattered Highlands must ease climate and terrain over 192 "
            "blocks at both realm boundaries"
        ),
    )
    require(
        re.search(
            r"RewriteShatteredHighlandsMaps\s*\(.*?"
            r"mapRegion\.ClimateMap.*?"
            r"mapRegion\.ForestMap.*?"
            r"mapRegion\.ShrubMap.*?"
            r"mapRegion\.OceanMap.*?"
            r"mapRegion\.UpheavelMap.*?"
            r"mapRegion\.LandformMap",
            shattered_highlands_source,
            re.DOTALL,
        )
        is not None,
        (
            "Shattered Highlands must rewrite climate, forest, shrub, ocean, "
            "upheaval and landform maps together"
        ),
    )
    require(
        "StableShatteredHighlandsCellHash"
        in shattered_highlands_source
        and "ShatteredHighlandsLandformCellSize = 768"
        in shattered_highlands_source
        and "ShatteredHighlandsRiftPercent = 34"
        in shattered_highlands_source
        and "Random" not in shattered_highlands_source,
        (
            "Shattered Highlands plateau/rift selection must be deterministic "
            "and chunk-generation-order independent"
        ),
    )
    rift_cells = sum(
        1
        for cell_z in range(-100, 100)
        for cell_x in range(-100, 100)
        if shattered_highlands_cell_hash(cell_x, cell_z) % 100 < 34
    )
    rift_fraction = rift_cells / 40_000
    require(
        0.32 <= rift_fraction <= 0.36,
        (
            "Shattered Highlands deterministic rift coverage drifted "
            f"outside 32%-36%: {rift_fraction:.3%}"
        ),
    )
    require(
        "StartShatteredHighlandsProbe" in shattered_highlands_source
        and "PeekChunkColumn" in shattered_highlands_source
        and "PlateauColumns" in shattered_highlands_source
        and "CliffEdgeColumns" in shattered_highlands_source
        and "DeepRiftColumns" in shattered_highlands_source
        and "ExposedRockColumns" in shattered_highlands_source
        and "HasGroundRouteAcrossChunk" in shattered_highlands_source
        and 'BeginSubCommand("highlands")'
        in command_registration_source
        and "/apprentice highlands probe"
        in command_registration_source,
        (
            "Shattered Highlands needs an administrator-only real-worldgen "
            "probe for plateaus, rifts, cliffs, exposed rock and ground routes"
        ),
    )
    require(
        "RegisterGameTickListener" not in shattered_highlands_source
        and "RegisterRenderer" not in shattered_highlands_source
        and "SetBlock(" not in shattered_highlands_source,
        (
            "Shattered Highlands terrain must remain one-time map worldgen "
            "with no runtime loop or detached-terrain sculpting"
        ),
    )
    require(
        "internal sealed class ShatteredHighlandsSurfaceGenerator"
        in shattered_highlands_surface_source
        and "shatteredHighlandsSurfaceGenerator"
        in worldgen_source
        and "EnumWorldGenPass.NeighbourSunLightFlood"
        in worldgen_source,
        (
            "Shattered Highlands must reveal native rock in an isolated "
            "one-time post-vegetation surface pass"
        ),
    )
    require(
        "BoundaryTransitionWidth = 192"
        in shattered_highlands_surface_source
        and "GetRealmStrength(" in shattered_highlands_surface_source
        and "WorldGenTerrainHeightMap"
        in shattered_highlands_surface_source
        and "TransformSurfaceColumn("
        in shattered_highlands_surface_source
        and "SelectSurfaceBlock("
        in shattered_highlands_surface_source
        and "heights[mapIndex] =" not in shattered_highlands_surface_source,
        (
            "The realm-wide Highlands curse must preserve both transition "
            "zones and the approved terrain-height map"
        ),
    )
    require(
        "SurfaceDepth = 4" in shattered_highlands_surface_source
        and "ashenWeedId" in shattered_highlands_surface_source
        and "wraithThornId" in shattered_highlands_surface_source
        and "wraithWoodId" in shattered_highlands_surface_source
        and "TryBuildWraithTree(" in shattered_highlands_surface_source
        and "TransformOrdinaryVegetation("
        in shattered_highlands_surface_source
        and "CliffSlopeThreshold = 3"
        in shattered_highlands_surface_source
        and "StableHash(" in shattered_highlands_surface_source
        and "Random" not in shattered_highlands_surface_source
        and "RegisterGameTickListener"
        not in shattered_highlands_surface_source
        and "RegisterRenderer"
        not in shattered_highlands_surface_source,
        (
            "Every Highlands landscape needs deterministic cursed ground, "
            "dead flora and twisted trees without ongoing runtime work"
        ),
    )
    highlands_plant_textures = [
        SHATTERED_HIGHLANDS_ASHEN_TEXTURE_PATH.read_bytes(),
        SHATTERED_HIGHLANDS_THORN_TEXTURE_PATH.read_bytes(),
    ]
    require(
        all(
            texture.startswith(b"\x89PNG\r\n\x1a\n")
            and int.from_bytes(texture[16:20], "big") == 32
            and int.from_bytes(texture[20:24], "big") == 32
            and texture[25] in (4, 6)
            for texture in highlands_plant_textures
        )
        and "apprenticehighlands:block/ashenweed"
        in shattered_highlands_ashen_block_source
        and "apprenticehighlands:block/wraiththorn"
        in shattered_highlands_thorn_block_source
        and "apprenticemire:" not in shattered_highlands_ashen_block_source
        and "apprenticemire:" not in shattered_highlands_thorn_block_source
        and "(hash >> 32) % 32 == 0"
        in shattered_highlands_surface_source
        and "roll < 19 * realmStrength"
        in shattered_highlands_surface_source
        and "(hash >> 44) % 1536 == 0"
        in shattered_highlands_surface_source,
        (
            "Highlands ecology must use its own dark 32x32 alpha textures "
            "and sparse deterministic coverage instead of pale plant spam"
        ),
    )
    highlands_cultures = {
        "crownless",
        "basilica",
        "aqueduct",
        "forum",
        "foundry",
        "necropolis",
    }
    highlands_city_types = shattered_highlands_cities.get(
        "villageTypes",
        [],
    )
    schematic_paths = sorted(
        SHATTERED_HIGHLANDS_SCHEMATIC_ROOT.rglob("*.json")
    )
    require(
        len(highlands_city_types) == 24
        and {
            entry["code"]
            .removeprefix("highlands-city-")
            .rsplit("-", 1)[0]
            for entry in highlands_city_types
        }
        == highlands_cultures
        and all(
            entry.get("group") == "apprentice-highlands-city"
            and entry.get("rockTypeRemapGroup") == "highlands"
            and (
                entry.get("minGroupDistance") == 3600
                if entry["code"].endswith("-landmark")
                else entry.get("minGroupDistance") == 0
            )
            for entry in highlands_city_types
        ),
        (
            "Shattered Highlands needs four native placement pools for each "
            "of six original, widely separated city cultures"
        ),
    )
    require(
        all(
            len(entry.get("schematics", [])) == 1
            and all(
                schematic.get("minQuantity") == 1
                and schematic.get("maxQuantity") == 1
                for schematic in entry["schematics"]
            )
            for entry in highlands_city_types
        ),
        (
            "Every distributed city sector must place one weighted native "
            "landmark, district, road or remnant schematic"
        ),
    )
    schematic_counts = {
        culture: {
            family: len(
                list(
                    (
                        SHATTERED_HIGHLANDS_SCHEMATIC_ROOT
                        / culture
                        / family
                    ).glob("*.json")
                )
            )
            for family in (
                "landmarks",
                "districts",
                "infrastructure",
                "remnants",
            )
        }
        for culture in highlands_cultures
    }
    require(
        len(schematic_paths) == 126
        and len(
            shattered_highlands_structures.get(
                "schematicYOffsets",
                {},
            )
        )
        == 126
        and "highlands"
        in shattered_highlands_structures.get(
            "rocktypeRemapGroups",
            {},
        )
        and all(
            {
                "GameVersion",
                "SizeX",
                "SizeY",
                "SizeZ",
                "BlockCodes",
                "Indices",
                "BlockIds",
                "ReplaceMode",
            }
            <= set(json.loads(path.read_text(encoding="utf-8")))
            for path in schematic_paths
        ),
        (
            "The expanded 126-piece Highlands schematic kit must stay "
            "complete, offset-controlled and native-format"
        ),
    )
    unsupported_schematics = []
    for path in schematic_paths:
        schematic = json.loads(path.read_text(encoding="utf-8"))
        blocks = {
            (
                index & 1023,
                (index >> 20) & 1023,
                (index >> 10) & 1023,
            )
            for index in schematic["Indices"]
        }
        supported = {
            position
            for position in blocks
            if position[1] == 0
        }
        frontier = list(supported)
        while frontier:
            x, y, z = frontier.pop()
            for neighbour in (
                (x + 1, y, z),
                (x - 1, y, z),
                (x, y + 1, z),
                (x, y - 1, z),
                (x, y, z + 1),
                (x, y, z - 1),
            ):
                if neighbour in blocks and neighbour not in supported:
                    supported.add(neighbour)
                    frontier.append(neighbour)
        if supported != blocks:
            unsupported_schematics.append(path)
    require(
        not unsupported_schematics
        and "remove_unsupported_components"
        in shattered_highlands_generator_source,
        (
            "Every Highlands schematic voxel must remain connected to its "
            "Y=0 foundation; floating age fragments are forbidden"
        ),
    )
    require(
        all(
            counts
            == {
                "landmarks": 4,
                "districts": 6,
                "infrastructure": 5,
                "remnants": 6,
            }
            for counts in schematic_counts.values()
        )
        and all(
            "granite" not in path.read_text(encoding="utf-8")
            and "quartz" not in path.read_text(encoding="utf-8")
            for path in schematic_paths
        ),
        (
            "Every culture needs a full, dark architectural family without "
            "the old pale granite/quartz placeholder palette"
        ),
    )
    require(
        all(
            json.loads(path.read_text(encoding="utf-8"))["SizeX"] <= 31
            and json.loads(path.read_text(encoding="utf-8"))["SizeZ"] <= 31
            for path in schematic_paths
        )
        and all(
            json.loads(path.read_text(encoding="utf-8"))["SizeY"] >= 47
            for path in schematic_paths
            if path.parent.name == "landmarks"
        )
        and "Every road sector is rotation-independent"
        in shattered_highlands_generator_source
        and "model.floor(0, 0, width - 1, depth - 1"
        not in shattered_highlands_generator_source,
        (
            "Dense city sectors must fit their 32-block plan, roads must "
            "connect in every rotation, and districts must not be giant "
            "rectangular foundation pads"
        ),
    )
    require(
        "WorldGenVillage" in shattered_highlands_ruins_source
        and "TryPlaceSupportedCityPart("
        in shattered_highlands_ruins_source
        and "PlaceRespectingBlockLayers("
        in shattered_highlands_ruins_source
        and ".TryGenerate(" not in shattered_highlands_ruins_source
        and "ResolveRemaps(" in shattered_highlands_ruins_source
        and '"worldgen/cities.json"' in shattered_highlands_ruins_source
        and not (
            REPOSITORY_ROOT
            / "assets"
            / "apprenticehighlands"
            / "worldgen"
            / "villages.json"
        ).exists(),
        (
            "Highlands cities must load Vintage Story's native weighted "
            "schematic pools but route every placement through the supported "
            "Level 7 foundation path"
        ),
    )
    require(
        "CultureCount = 6" in shattered_highlands_ruins_source
        and "StableHash(" in shattered_highlands_ruins_source
        and "signature.ToString(\"x16\")"
        in shattered_highlands_ruins_source
        and "System.Random" not in shattered_highlands_ruins_source
        and "CityGridSize = 4096"
        in shattered_highlands_ruins_source
        and "SelectPlannedCityPart("
        in shattered_highlands_ruins_source,
        (
            "City culture, layout signature and corruption must be "
            "deterministic while producing distinct valley compositions"
        ),
    )
    require(
        "BoundaryExclusionWidth = 192"
        in shattered_highlands_ruins_source
        and "BoundaryExclusionWidth +"
        in shattered_highlands_ruins_source
        and "CorruptionRadius"
        in shattered_highlands_ruins_source
        and "WorldZoneLayout.IsInsideLevelCore("
        in shattered_highlands_ruins_source
        and "TryCreatePlannedCity("
        in shattered_highlands_ruins_source
        and "IsRiftLandformCell("
        in shattered_highlands_ruins_source
        and "RiftLandformPercent = 34"
        in shattered_highlands_ruins_source
        and "LandformCellSize = 768"
        in shattered_highlands_ruins_source,
        (
            "Ruined cities must use the approved stepped-rift valley cells, "
            "remain wholly inside Level 7 and avoid both transition zones"
        ),
    )
    require(
        "MinimumCitySpacing = 3600"
        in shattered_highlands_ruins_source
        and "CityFootprintRadius = 224"
        in shattered_highlands_ruins_source
        and "MaximumTerraceRelief = 40"
        in shattered_highlands_ruins_source
        and "BuildTerraceFoundation("
        in shattered_highlands_ruins_source
        and "TryMeasureTerrainRelief("
        in shattered_highlands_ruins_source
        and "bool pier =" in shattered_highlands_ruins_source
        and "bool buttress =" in shattered_highlands_ruins_source
        and "int[] offsets = { 0 };"
        in shattered_highlands_ruins_source
        and "SectorPrefix"
        in shattered_highlands_ruins_source
        and "spacing violations="
        in shattered_highlands_ruins_source
        and "[ThreadStatic]"
        not in shattered_highlands_ruins_source
        and "worldgenBlockAccessor"
        in shattered_highlands_ruins_source,
        (
            "Highlands cities need a large distributed footprint, measured "
            "pier-and-retaining support, fixed sector anchors, idempotent "
            "registration and the native worldgen accessor"
        ),
    )
    require(
        "IsPrimaryCityChunk(" in shattered_highlands_ruins_source
        and "chunkRadiusSquared <= 16"
        in shattered_highlands_ruins_source
        and "chunkRadiusSquared > 49"
        in shattered_highlands_ruins_source
        and "chunkRadiusSquared == 4"
        in shattered_highlands_ruins_source
        and "return roll < 88"
        in shattered_highlands_ruins_source,
        (
            "The city planner must create a dense urban core, five-part "
            "monumental skyline, connected culture roads and sparse outer "
            "ruins instead of a 64-block checkerboard"
        ),
    )
    require(
        "GetCulturePattern(" in shattered_highlands_ruins_source
        and "SelectCorruptionBlock(" in shattered_highlands_ruins_source
        and "IsLivingFlora(" in shattered_highlands_ruins_source
        and "blackVeinId" in shattered_highlands_ruins_source
        and "gloomId" in shattered_highlands_ruins_source
        and "WorldGenTerrainHeightMap"
        in shattered_highlands_ruins_source
        and "heights[mapIndex] =" not in shattered_highlands_ruins_source,
        (
            "Each city culture needs a distinct dark landscape pattern "
            "without altering approved Highlands terrain heights"
        ),
    )
    require(
        "StartShatteredHighlandsRuinsProbe"
        in shattered_highlands_source
        and 'BeginSubCommand("ruins")'
        in command_registration_source
        and "/apprentice highlands ruins probe"
        in command_registration_source
        and "unique signatures="
        in shattered_highlands_ruins_source
        and "landmarkModules >= anchors.Count * 5"
        in shattered_highlands_ruins_source
        and "modules.Count >= anchors.Count * 60"
        in shattered_highlands_ruins_source
        and "realm leaks="
        in shattered_highlands_ruins_source
        and "border leaks="
        in shattered_highlands_ruins_source,
        (
            "Highlands ruined cities need culture, uniqueness, five-piece "
            "landmark and dense-module gates, plus boundary, corruption and "
            "flora diagnostics"
        ),
    )
    require(
        "insideShatteredHighlands" in worldgen_source
        and "ShatteredHighlandsLevel" in worldgen_source,
        (
            "Shattered Highlands must suppress ordinary surface clutter and "
            "vanilla structures during terrain approval"
        ),
    )
    require(
        'RealmMapsPerformanceMarker' in worldgen_source
        and "HasCurrentRealmMaps(mapRegion)" in worldgen_source
        and "HasAcceptedLevelOneThroughFourMaps" in worldgen_source
        and "mapRegion.GetModdata(FrozenExpanseMapMarker)" in worldgen_source
        and "mapRegion.GetModdata(PoisonMireMapMarker)" in worldgen_source
        and "mapRegion.GetModdata(ShatteredHighlandsMapMarker)"
        in worldgen_source
        and "concentricRealmsMapsV5" in worldgen_source,
        (
            "Accepted map regions must receive only missing Level 5/6/7 "
            "transforms before promotion to the current performance marker"
        ),
    )
    require(
        "WorldZoneLayout.RectangleIntersectsLevel(" in worldgen_source
        and "distanceSquared = dx * dx + dz * dz;" in worldgen_source
        and "WorldZoneLayout.IsLevelAt(" not in re.search(
            r"private static int TransformLevelMap\(.*?"
            r"private static int GetShadowForestDensity",
            worldgen_source,
            re.DOTALL,
        ).group(0),
        (
            "Realm map transforms must reject non-intersecting map surfaces "
            "before scanning and use squared-distance cell checks"
        ),
    )
    broad_placement_body = re.search(
        r"public bool PreventPlacementAt\(.*?;"
        r"\s*public bool PreventPlacementBroadlyAt",
        worldgen_source,
        re.DOTALL,
    )
    require(
        broad_placement_body is not None
        and "ShadowForestLevel" not in broad_placement_body.group(0),
        (
            "Shadow Forest must not use broad placement suppression because "
            "that would also remove its trees"
        ),
    )
    require(
        broad_placement_body is not None
        and "FrozenExpanseLevel" not in broad_placement_body.group(0),
        (
            "Frozen Expanse must not use broad placement suppression because "
            "it would bypass the normal glacier surface pipeline"
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
    shadow_forest_inner = endless_forest_outer
    shadow_forest_outer = base_radius + ring_width * 4
    frozen_expanse_inner = shadow_forest_outer
    frozen_expanse_outer = base_radius + ring_width * 5
    poison_mire_inner = frozen_expanse_outer
    poison_mire_outer = base_radius + ring_width * 6
    shattered_highlands_inner = poison_mire_outer
    shattered_highlands_outer = base_radius + ring_width * 7
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

    for radius, expected in (
        (poison_mire_inner + epsilon, 6),
        ((poison_mire_inner + poison_mire_outer) / 2, 6),
        (poison_mire_outer, 6),
        (poison_mire_outer + epsilon, 7),
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
                f"Poison Mire radius {radius} resolved to level "
                f"{actual}, expected {expected}"
            ),
        )

    for radius, expected in (
        (shattered_highlands_inner + epsilon, 7),
        (
            (
                shattered_highlands_inner
                + shattered_highlands_outer
            )
            / 2,
            7,
        ),
        (shattered_highlands_outer, 7),
        (shattered_highlands_outer + epsilon, 8),
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
                f"Shattered Highlands radius {radius} resolved to level "
                f"{actual}, expected {expected}"
            ),
        )

    for radius, expected in (
        (shadow_forest_inner + epsilon, 4),
        ((shadow_forest_inner + shadow_forest_outer) / 2, 4),
        (shadow_forest_outer, 4),
        (shadow_forest_outer + epsilon, 5),
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
                f"Shadow Forest radius {radius} resolved to level "
                f"{actual}, expected {expected}"
            ),
        )

    for radius, expected in (
        (frozen_expanse_inner + epsilon, 5),
        ((frozen_expanse_inner + frozen_expanse_outer) / 2, 5),
        (frozen_expanse_outer, 5),
        (frozen_expanse_outer + epsilon, 6),
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
                f"Frozen Expanse radius {radius} resolved to level "
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
        "difficult-terrain/map restriction contract, Level 4 lower-relief "
        "valley/clearing contract, Level 5 real-glacier climate/terrain and "
        "two-edge transition contract, deterministic 24.2% Ice-Spike Fields "
        "with chunk-border and performance diagnostics, Level 6 approved "
        "marsh geometry with shallow-water and route diagnostics, post-"
        "vegetation zero-green conversion, 100% custom Toxicwater, fixed "
        "corrupted ground, several dead-flora forms, dead-tree zones and "
        "localized mist, Level 7 deterministic ledged plateaus/stepped "
        f"rifts ({rift_fraction:.1%}) with two-edge transitions and "
        "real-worldgen route diagnostics, realm-wide cursed ground/dead "
        "flora/wraith trees, six unique ruined-city cultures, 126 native "
        "schematics, culture-specific corruption, exact "
        "strongest-poison tier, "
        "persistent realm "
        "discoveries and locked Survival Handbook guides, realm ecology "
        "allowlists, clean wild-plant "
        "to harvested-ingredient chain, non-destructive real-worldgen ecology "
        "probe, 20,000 deterministic samples."
    )


if __name__ == "__main__":
    main()
