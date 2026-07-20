#!/usr/bin/env python3
"""Validate Apprentice assets without requiring a Vintage Story install."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import struct
import sys
from pathlib import Path
from typing import Any, Iterable


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
PROJECT_ROOT = REPOSITORY_ROOT / "Apprentice"
ASSET_ROOT = PROJECT_ROOT / "assets" / "apprentice"
SUPPORTED_VERSION = re.compile(
    r"^\d+\.\d+\.\d+(?:-(?:[a-z]|(?:rc|pre|dev)(?:\.\d+)*))?$"
)
NATIVE_HELD_GRIP_TARGET = (-0.0488, 0.0098, -0.0292)


class Validation:
    def __init__(self) -> None:
        self.errors: list[str] = []
        self.json_count = 0
        self.png_count = 0

    def check(self, condition: bool, message: str) -> None:
        if not condition:
            self.errors.append(message)

    def load_json(self, path: Path) -> Any:
        try:
            value = json.loads(path.read_text(encoding="utf-8-sig"))
            self.json_count += 1
            return value
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            self.errors.append(f"{relative(path)}: {error}")
            return None


def relative(path: Path) -> str:
    try:
        return path.relative_to(REPOSITORY_ROOT).as_posix()
    except ValueError:
        return path.as_posix()


def transformed_model_point(
    raw_point: Iterable[float],
    transform: dict[str, Any],
) -> tuple[float, float, float]:
    """Apply Vintage Story's third-person held-item ModelTransform chain.

    EntityShapeRenderer composes held items as
    ``T(origin) * S(scale) * T(attachment + translation) * R * T(-origin)``.
    The attachment-point term is intentionally omitted here so the result is
    the item's local offset from that attachment. In particular, translation
    is scaled; treating it as a final unscaled offset caused the detached
    bow, spear and shield regression in development snapshot .8.
    """
    point = [float(component) / 16 for component in raw_point]
    translation = transform.get("translation", {})
    rotation = transform.get("rotation", {})
    origin = transform.get("origin", {})
    scale = float(transform.get("scale", 1))
    tx, ty, tz = (
        float(translation.get(axis, 0)) for axis in ("x", "y", "z")
    )
    rx, ry, rz = (
        math.radians(float(rotation.get(axis, 0)))
        for axis in ("x", "y", "z")
    )
    ox, oy, oz = (
        float(origin.get(axis, 0.5)) for axis in ("x", "y", "z")
    )

    sx, cx = math.sin(rx), math.cos(rx)
    sy, cy = math.sin(ry), math.cos(ry)
    sz, cz = math.sin(rz), math.cos(rz)
    a01 = sx * sy
    a02 = -cx * sy
    matrix = (
        (cy * cz, -cy * sz, sy),
        (a01 * cz + cx * sz, cx * cz - a01 * sz, -sx * cy),
        (a02 * cz + sx * sz, sx * cz - a02 * sz, cx * cy),
    )
    local = (
        point[0] - ox,
        point[1] - oy,
        point[2] - oz,
    )
    rotated = tuple(
        sum(matrix[row][column] * local[column] for column in range(3))
        for row in range(3)
    )
    return (
        ox + scale * (tx + rotated[0]),
        oy + scale * (ty + rotated[1]),
        oz + scale * (tz + rotated[2]),
    )


def points_close(
    actual: Iterable[float],
    expected: Iterable[float],
    tolerance: float = 0.002,
) -> bool:
    return all(
        abs(float(actual_component) - float(expected_component)) <= tolerance
        for actual_component, expected_component in zip(actual, expected)
    )


def require_mapping(
    validation: Validation,
    value: Any,
    source: Path,
) -> dict[str, Any]:
    validation.check(
        isinstance(value, dict),
        f"{relative(source)} must contain a JSON object.",
    )
    return value if isinstance(value, dict) else {}


def validate_all_json(validation: Validation) -> None:
    for path in sorted(PROJECT_ROOT.rglob("*.json")):
        document = validation.load_json(path)
        forbidden_codes = {
            "game:fireclaybrick": "game:burnedbrick-fire",
            "game:ingot-ironmeteoric": "game:ingot-meteoriciron",
        }

        def check_codes(value: Any) -> None:
            if isinstance(value, dict):
                for child in value.values():
                    check_codes(child)
            elif isinstance(value, list):
                for child in value:
                    check_codes(child)
            elif isinstance(value, str) and value in forbidden_codes:
                validation.errors.append(
                    f"{relative(path)} references nonexistent '{value}'; "
                    f"use '{forbidden_codes[value]}'."
                )

        check_codes(document)


def validate_modinfo(validation: Validation) -> None:
    path = PROJECT_ROOT / "modinfo.json"
    document = require_mapping(validation, validation.load_json(path), path)

    required = ("type", "modid", "name", "authors", "description", "version")
    for key in required:
        validation.check(bool(document.get(key)), f"modinfo.json is missing '{key}'.")

    modid = str(document.get("modid", ""))
    validation.check(
        re.fullmatch(r"[a-z0-9]+", modid) is not None,
        "modinfo.json modid must contain only lowercase letters and numbers.",
    )

    version = str(document.get("version", ""))
    validation.check(
        SUPPORTED_VERSION.fullmatch(version) is not None,
        (
            f"modinfo.json version '{version}' is not supported; use a three-part "
            "release version, lettered hotfix, or rc/pre/dev prerelease."
        ),
    )

    mod_system_path = PROJECT_ROOT / "src" / "ApprenticeModSystem.cs"
    try:
        mod_system_source = mod_system_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"Build identity sources: {error}")
        mod_system_source = ""
    validation.check(
        f'private const string PlaytestVersion = "{version}";' in
            mod_system_source,
        (
            "modinfo.json and ApprenticeModSystem must advertise the same "
            "build version so stale mod copies cannot tie."
        ),
    )

    authors = document.get("authors")
    validation.check(
        isinstance(authors, list)
        and all(isinstance(author, str) and author.strip() for author in authors),
        "modinfo.json authors must be a non-empty string array.",
    )

    launch_path = PROJECT_ROOT / "Properties" / "launchSettings.json"
    launch = require_mapping(validation, validation.load_json(launch_path), launch_path)
    profiles = launch.get("profiles", {})
    if isinstance(profiles, dict):
        duplicate_origins = [
            name
            for name, profile in profiles.items()
            if isinstance(profile, dict)
            and "--addOrigin" in str(profile.get("commandLineArgs", ""))
        ]
        validation.check(
            not duplicate_origins,
            (
                "Launch profiles must not add the source assets as a second "
                f"origin; duplicated profiles={duplicate_origins}."
            ),
        )


def validate_progression_config(validation: Validation) -> None:
    class_path = ASSET_ROOT / "config" / "class.json"
    tree_path = ASSET_ROOT / "config" / "skilltrees.json"
    trait_path = ASSET_ROOT / "config" / "traits.json"
    race_path = ASSET_ROOT / "config" / "characterclasses.json"
    legacy_override_path = (
        PROJECT_ROOT / "assets" / "game" / "config" / "characterclasses.json"
    )

    validation.check(
        not legacy_override_path.exists(),
        (
            f"{relative(legacy_override_path)} is a stale full replacement that "
            "removes vanilla classes. Delete it and use the namespaced patch."
        ),
    )

    classes = require_mapping(validation, validation.load_json(class_path), class_path)
    trees = require_mapping(validation, validation.load_json(tree_path), tree_path)
    traits = validation.load_json(trait_path)
    races = validation.load_json(race_path)

    class_types = classes.get("ClassTypes", {})
    tree_types = trees.get("Trees", {})
    validation.check(isinstance(class_types, dict), "class.json ClassTypes must be an object.")
    validation.check(isinstance(tree_types, dict), "skilltrees.json Trees must be an object.")
    if not isinstance(class_types, dict) or not isinstance(tree_types, dict):
        return

    class_ids = set(class_types)
    tree_ids = set(tree_types)
    validation.check(
        class_ids == tree_ids,
        (
            "Class/tree IDs differ: "
            f"missing trees={sorted(class_ids - tree_ids)}, "
            f"unknown trees={sorted(tree_ids - class_ids)}."
        ),
    )

    for class_id, tree in tree_types.items():
        if not isinstance(tree, dict):
            validation.errors.append(f"Skill tree '{class_id}' is not an object.")
            continue

        nodes = tree.get("Nodes", [])
        if not isinstance(nodes, list):
            validation.errors.append(f"Skill tree '{class_id}' Nodes must be an array.")
            continue

        node_ids = [node.get("Id") for node in nodes if isinstance(node, dict)]
        validation.check(
            len(node_ids) == len(set(node_ids)) and all(node_ids),
            f"Skill tree '{class_id}' contains an empty or duplicate node ID.",
        )
        known_ids = set(node_ids)
        positions: set[tuple[Any, Any]] = set()

        for node in nodes:
            if not isinstance(node, dict):
                validation.errors.append(f"Skill tree '{class_id}' contains a non-object node.")
                continue
            node_id = node.get("Id")
            position = (node.get("Column"), node.get("Row"))
            validation.check(
                position not in positions,
                f"Skill tree '{class_id}' repeats node position {position}.",
            )
            positions.add(position)

            for property_name in ("Requires", "RequiresAny"):
                values = node.get(property_name, [])
                validation.check(
                    isinstance(values, list),
                    f"Skill tree '{class_id}/{node_id}' {property_name} must be an array.",
                )
                if isinstance(values, list):
                    for required_id in values:
                        validation.check(
                            required_id in known_ids,
                            (
                                f"Skill tree '{class_id}/{node_id}' references unknown "
                                f"node '{required_id}' in {property_name}."
                            ),
                        )

    validation.check(isinstance(traits, list), "traits.json must contain an array.")
    validation.check(isinstance(races, list), "characterclasses.json must contain an array.")
    if isinstance(traits, list) and isinstance(races, list):
        trait_ids = {
            trait.get("code")
            for trait in traits
            if isinstance(trait, dict) and trait.get("code")
        }
        used_traits = {
            trait_id
            for race in races
            if isinstance(race, dict)
            for trait_id in race.get("traits", [])
        }
        validation.check(
            used_traits <= trait_ids,
            f"Race configuration references missing traits: {sorted(used_traits - trait_ids)}.",
        )
        validation.check(
            trait_ids <= used_traits,
            f"traits.json contains unused traits: {sorted(trait_ids - used_traits)}.",
        )


def apprentice_asset_path(kind: str, code: str, suffix: str) -> Path | None:
    prefix = "apprentice:"
    if not code.startswith(prefix):
        return None
    return ASSET_ROOT / kind / f"{code[len(prefix):]}{suffix}"


def validate_shapes_and_textures(validation: Validation) -> None:
    external_texture_slots = {"base", "metal"}
    for shape_path in sorted((ASSET_ROOT / "shapes").rglob("*.json")):
        shape = validation.load_json(shape_path)
        if not isinstance(shape, dict):
            continue
        textures = shape.get("textures", {})
        if not isinstance(textures, dict):
            validation.errors.append(f"{relative(shape_path)} textures must be an object.")
            continue
        for code in textures.values():
            if not isinstance(code, str):
                validation.errors.append(f"{relative(shape_path)} contains a non-string texture.")
                continue
            texture_path = apprentice_asset_path("textures", code, ".png")
            if texture_path is not None:
                validation.check(
                    texture_path.is_file(),
                    f"{relative(shape_path)} references missing {relative(texture_path)}.",
                )

        def validate_element_textures(elements: Any) -> None:
            if not isinstance(elements, list):
                return
            for element in elements:
                if not isinstance(element, dict):
                    continue
                faces = element.get("faces", {})
                if isinstance(faces, dict):
                    for face in faces.values():
                        texture = face.get("texture") if isinstance(face, dict) else None
                        if isinstance(texture, str) and texture.startswith("#"):
                            validation.check(
                                texture[1:] in textures or texture[1:] in external_texture_slots,
                                (
                                    f"{relative(shape_path)} element "
                                    f"'{element.get('name', '?')}' references unmapped "
                                    f"texture '{texture}'."
                                ),
                            )
                validate_element_textures(element.get("children"))

        validate_element_textures(shape.get("elements"))

        race_name = shape_path.stem
        if race_name in {"dragonborn", "dwarf", "elf", "gnome", "halfling"}:
            validation.check(
                textures.get("raceskin")
                == f"apprentice:entity/humanoid/races/skins/{race_name}",
                (
                    f"{relative(shape_path)} must provide a selected-skin "
                    "fallback for custom facial geometry."
                ),
            )

        if (
            "options/teeth" in shape_path.as_posix()
            and shape_path.stem != "none"
        ):
            validation.check(
                textures.get("toothwhite")
                == "apprentice:entity/humanoid/races/horns/white",
                (
                    f"{relative(shape_path)} teeth must use the dedicated "
                    "ivory-white texture key."
                ),
            )

    patch_path = ASSET_ROOT / "patches" / "add-race-model-skinpart.json"
    patches = validation.load_json(patch_path)
    if not isinstance(patches, list):
        validation.errors.append(f"{relative(patch_path)} must contain an array.")
        return

    texture_size_paths = {
        patch.get("path")
        for patch in patches
        if isinstance(patch, dict)
        and patch.get("file") == "game:shapes/entity/humanoid/seraph-faceless.json"
    }
    skinpart_values = {
        value.get("code"): value
        for patch in patches
        if isinstance(patch, dict)
        and isinstance((value := patch.get("value")), dict)
        and isinstance(value.get("code"), str)
    }

    face_skin = skinpart_values.get("apprenticefaceskin", {})
    face_variants = face_skin.get("variants", []) if isinstance(face_skin, dict) else []
    face_codes = {
        variant.get("code")
        for variant in face_variants
        if isinstance(variant, dict)
    }
    required_face_codes = {f"skin{index}" for index in range(1, 21)} | {
        "apprentice-dragonborn",
        "apprentice-dwarf",
        "apprentice-drow-black",
        "apprentice-elf",
        "apprentice-gnome",
        "apprentice-goliath",
        "apprentice-halfling",
        "apprentice-orc",
        "apprentice-tiefling",
    }
    validation.check(
        face_skin.get("textureTarget") == "raceskin",
        "apprenticefaceskin must target the custom geometry 'raceskin' texture.",
    )
    validation.check(
        required_face_codes <= face_codes,
        (
            "apprenticefaceskin is missing base-skin variants: "
            f"{sorted(required_face_codes - face_codes)}."
        ),
    )

    additions_path = ASSET_ROOT / "config" / "seraphskinnableparts.json"
    additions = validation.load_json(additions_path)
    ruby_registered = any(
        isinstance(part, dict)
        and part.get("code") == "eyecolor"
        and any(
            isinstance(variant, dict) and variant.get("code") == "ruby"
            for variant in part.get("variants", [])
        )
        for part in additions if isinstance(additions, list)
    )
    validation.check(
        ruby_registered,
        "seraphskinnableparts.json must register the Drow 'ruby' eye color.",
    )

    for patch in patches:
        if not isinstance(patch, dict):
            continue
        value = patch.get("value")
        if not isinstance(value, dict):
            continue

        part_type = value.get("type")
        template = value.get("shapeTemplate", {})
        variants = value.get("variants", [])
        if part_type == "shape" and isinstance(template, dict) and isinstance(variants, list):
            base = template.get("base")
            if isinstance(base, str) and "{code}" in base:
                for variant in variants:
                    if not isinstance(variant, dict) or not isinstance(variant.get("code"), str):
                        validation.errors.append("A shape skinpart variant is missing its code.")
                        continue
                    shape_code = base.replace("{code}", variant["code"])
                    shape_path = apprentice_asset_path("shapes", shape_code, ".json")
                    if shape_path is not None:
                        validation.check(
                            shape_path.is_file(),
                            f"Skinpart variant references missing {relative(shape_path)}.",
                        )

        if part_type == "texture":
            target = value.get("textureTarget")
            if isinstance(target, str) and target:
                validation.check(
                    f"/textureSizes/{target}" in texture_size_paths,
                    f"Missing base-shape textureSizes entry for '{target}'.",
                )
                validation.check(
                    f"/textureSizes/skinpart-{target}" in texture_size_paths,
                    f"Missing generated skinpart textureSizes entry for '{target}'.",
                )
            for variant in variants if isinstance(variants, list) else []:
                if not isinstance(variant, dict) or not isinstance(variant.get("texture"), str):
                    continue
                texture_path = apprentice_asset_path(
                    "textures", variant["texture"], ".png"
                )
                if texture_path is not None:
                    validation.check(
                        texture_path.is_file(),
                        f"Skinpart variant references missing {relative(texture_path)}.",
                    )


def validate_language_keys(validation: Validation) -> None:
    lang_path = ASSET_ROOT / "lang" / "en.json"
    lang = validation.load_json(lang_path)
    if not isinstance(lang, dict):
        validation.errors.append(f"{relative(lang_path)} must contain an object.")
        return

    used: set[str] = set()
    pattern = re.compile(r'Lang\.Get\(\s*"apprentice:([^"]+)"')
    for source in sorted((PROJECT_ROOT / "src").rglob("*.cs")):
        used.update(pattern.findall(source.read_text(encoding="utf-8-sig")))
    validation.check(
        used <= set(lang),
        f"en.json is missing direct language keys: {sorted(used - set(lang))}.",
    )
    metal_language_keys = {
        "game:item-metalplate-starsteel",
        "game:item-metalplate-aethersteel",
        "game:block-metalsheet-starsteel-down",
        "game:block-metalsheet-aethersteel-down",
        "game:block-metalblock-new-plain-starsteel",
        "game:block-metalblock-corroded-plain-starsteel",
        "game:block-metalblock-new-riveted-starsteel",
        "game:block-metalblock-corroded-riveted-starsteel",
        "game:block-metalblock-new-plain-aethersteel",
        "game:block-metalblock-corroded-plain-aethersteel",
        "game:block-metalblock-new-riveted-aethersteel",
        "game:block-metalblock-corroded-riveted-aethersteel",
    }
    validation.check(
        metal_language_keys <= set(lang),
        (
            "en.json is missing Vintage Story metal variant keys: "
            f"{sorted(metal_language_keys - set(lang))}."
        ),
    )

    for kind, directory in (
        ("item", ASSET_ROOT / "itemtypes"),
        ("block", ASSET_ROOT / "blocktypes"),
    ):
        for path in sorted(directory.rglob("*.json")):
            document = validation.load_json(path)
            if not isinstance(document, dict) or not isinstance(document.get("code"), str):
                continue
            codes = expanded_codes(document)
            for code in codes:
                validation.check(
                    f"{kind}-{code}" in lang or f"{kind}-{document['code']}" in lang,
                    f"en.json is missing '{kind}-{code}' for {relative(path)}.",
                )


def expanded_codes(document: dict[str, Any]) -> set[str]:
    """Expand the simple one-axis variants used by Apprentice 2.7 assets."""
    code = document.get("code")
    if not isinstance(code, str) or not code:
        return set()
    groups = document.get("variantgroups", [])
    if not isinstance(groups, list) or not groups:
        return {code}
    if len(groups) != 1 or not isinstance(groups[0], dict):
        return {code}
    states = groups[0].get("states", [])
    if not isinstance(states, list) or not all(isinstance(value, str) for value in states):
        return {code}
    return {f"{code}-{state}" for state in states}


def validate_content_27(validation: Validation) -> None:
    config_path = ASSET_ROOT / "config" / "content-2.7.json"
    config = require_mapping(validation, validation.load_json(config_path), config_path)

    lang_path = ASSET_ROOT / "lang" / "en.json"
    lang = require_mapping(validation, validation.load_json(lang_path), lang_path)
    validation.check(
        lang.get("game:tabname-apprentice") == "Apprentice",
        "en.json must name the dedicated Apprentice creative tab.",
    )
    validation.check(
        bool(lang.get("game:maplayer-apprentice-danger-heatmap")),
        "en.json must name the Apprentice danger heatmap map tab.",
    )

    heatmap_source_path = PROJECT_ROOT / "src" / "DangerHeatmapLayer.cs"
    try:
        heatmap_source = heatmap_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(heatmap_source_path)}: {error}")
        heatmap_source = ""
    validation.check(
        'LayerGroupCode => "apprentice-danger-heatmap"' in heatmap_source,
        "DangerHeatmapLayer must expose its own world-map layer group/tab.",
    )
    validation.check(
        "Render2DTexturePremultipliedAlpha" in heatmap_source
        and "EnsureTerrainVisible" in heatmap_source
        and "layer is ChunkMapLayer" in heatmap_source
        and "OnMapOpenedServer" in heatmap_source
        and "SendState(fromPlayer)" in heatmap_source
        and "HeatmapRingDepth = 55f" in heatmap_source
        and "if (!Active" in heatmap_source
        and "GetEngineShader(EnumShaderProgram.Gui)" not in heatmap_source,
        (
            "DangerHeatmapLayer must render as a premultiplied-alpha overlay "
            "at depth 55, above vanilla terrain (50) and below waypoint "
            "icons (60), and resend state whenever the map opens; the manual "
            "GUI-shader path can replace the map with an opaque rectangle."
        ),
    )
    mod_system_path = PROJECT_ROOT / "src" / "ApprenticeModSystem.cs"
    packet_path = PROJECT_ROOT / "src" / "DangerHeatmapStatePacket.cs"
    try:
        heatmap_network_source = (
            mod_system_path.read_text(encoding="utf-8-sig")
            + packet_path.read_text(encoding="utf-8-sig")
            + heatmap_source
        )
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"Danger heatmap request sources: {error}")
        heatmap_network_source = ""
    validation.check(
        "DangerHeatmapRequestPacket" in heatmap_network_source
        and "SetMessageHandler<DangerHeatmapRequestPacket>" in heatmap_network_source
        and "DangerHeatmapClientRuntime.RequestState" in heatmap_network_source
        and "stateRequestCooldown = 2f" in heatmap_network_source,
        (
            "The heatmap must actively request and retry its server snapshot "
            "while the map is open; one-way startup delivery is race-prone."
        ),
    )

    registry_source_path = PROJECT_ROOT / "src" / "ContentRegistry.cs"
    try:
        registry_source = registry_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(registry_source_path)}: {error}")
        registry_source = ""
    validation.check(
        "EnsureCreativeInventoryPresence" in registry_source
        and '"apprentice"' in registry_source,
        (
            "The 2.7 runtime must enforce creative-inventory visibility and "
            "the dedicated Apprentice tab after asset patches are finalized."
        ),
    )

    healthbar_source_path = (
        PROJECT_ROOT / "src" / "renderer" / "HealthBarRenderer.cs"
    )
    try:
        healthbar_source = healthbar_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(healthbar_source_path)}: {error}")
        healthbar_source = ""
    validation.check(
        'AssetDomain = "apprentice"' in healthbar_source
        and '"apprenticehealthbar"' in healthbar_source,
        (
            "HealthBarRenderer must load its uniquely named shaders from the "
            "Apprentice asset domain, not game:shaders."
        ),
    )
    for extension in ("vsh", "fsh"):
        shader_path = (
            ASSET_ROOT / "shaders" / f"apprenticehealthbar.{extension}"
        )
        validation.check(
            shader_path.is_file(),
            f"Missing Apprentice health-bar shader {relative(shader_path)}.",
        )

    collectibles: dict[str, str] = {}
    for kind, directory in (
        ("item", ASSET_ROOT / "itemtypes"),
        ("block", ASSET_ROOT / "blocktypes"),
    ):
        for path in sorted(directory.rglob("*.json")):
            document = validation.load_json(path)
            if not isinstance(document, dict):
                continue
            for code in expanded_codes(document):
                full_code = f"apprentice:{code}"
                validation.check(
                    full_code not in collectibles,
                    f"Duplicate Apprentice collectible code '{full_code}'.",
                )
                collectibles[full_code] = kind

            def check_texture(value: Any) -> None:
                if isinstance(value, dict):
                    base = value.get("base")
                    if isinstance(base, str) and "{" not in base:
                        texture_path = apprentice_asset_path("textures", base, ".png")
                        if texture_path is not None:
                            validation.check(
                                texture_path.is_file(),
                                f"{relative(path)} references missing {relative(texture_path)}.",
                            )
                    for child in value.values():
                        check_texture(child)
                elif isinstance(value, list):
                    for child in value:
                        check_texture(child)

            for key in ("texture", "textures", "textureByType", "texturesByType"):
                check_texture(document.get(key))

    artifacts = config.get("GrandmasterArtifacts", [])
    discoveries = config.get("Discoveries", [])
    charges = config.get("CementationCharges", [])
    poisons = config.get("Poisons", [])
    ecology = config.get("Ecology", [])
    validation.check(isinstance(artifacts, list), "GrandmasterArtifacts must be an array.")
    validation.check(isinstance(discoveries, list), "Discoveries must be an array.")
    validation.check(isinstance(charges, list), "CementationCharges must be an array.")
    validation.check(isinstance(poisons, list), "Poisons must be an array.")
    validation.check(isinstance(ecology, list), "Ecology must be an array.")

    if isinstance(artifacts, list):
        artifact_codes = {
            value.get("Code") for value in artifacts if isinstance(value, dict)
        }
        validation.check(len(artifact_codes) == 18, "2.7 requires exactly 18 unique Grandmaster artifacts.")
        for code in artifact_codes:
            validation.check(
                code in collectibles,
                f"Grandmaster artifact '{code}' does not resolve to an Apprentice asset.",
            )

        tree_path = ASSET_ROOT / "config" / "skilltrees.json"
        trees = require_mapping(validation, validation.load_json(tree_path), tree_path)
        unlock_outputs = {
            effect.get("Code")
            for tree in trees.get("Trees", {}).values()
            if isinstance(tree, dict)
            for node in tree.get("Nodes", [])
            if isinstance(node, dict)
            for effect in node.get("Effects", [])
            if isinstance(effect, dict) and effect.get("Type") == "UnlockRecipe"
        }
        validation.check(
            artifact_codes == unlock_outputs,
            "Grandmaster artifact registry must exactly match every UnlockRecipe output.",
        )
        grid_outputs = {
            recipe.get("output", {}).get("code")
            for recipe_path in (ASSET_ROOT / "recipes" / "grid").glob("*.json")
            for recipe in (
                validation.load_json(recipe_path)
                if recipe_path.is_file()
                else []
            )
            if isinstance(recipe, dict)
        }
        validation.check(
            artifact_codes <= grid_outputs,
            (
                "Every Grandmaster UnlockRecipe output must have a "
                "handbook-visible grid recipe. Missing: "
                f"{sorted(artifact_codes - grid_outputs)}."
            ),
        )

        language_path = ASSET_ROOT / "lang" / "en.json"
        language = require_mapping(
            validation,
            validation.load_json(language_path),
            language_path,
        )
        for artifact in artifacts:
            if not isinstance(artifact, dict):
                continue
            code = str(artifact.get("Code", "")).split(":", 1)[-1]
            prefix = (
                "blockdesc-"
                if artifact.get("Kind") == "block"
                else "itemdesc-"
            )
            profession = str(artifact.get("Profession", ""))
            display_profession = next(
                (
                    str(tree.get("DisplayName", profession))
                    for tree in trees.get("Trees", {}).values()
                    if isinstance(tree, dict)
                    and tree.get("ClassId") == profession
                ),
                profession,
            )
            notice = str(language.get(f"{prefix}{code}", ""))
            validation.check(
                f"{display_profession} Grandmaster skill" in notice,
                (
                    f"Handbook description for '{artifact.get('Code')}' "
                    f"must name the required {display_profession} "
                    "Grandmaster skill."
                ),
            )

    poison_recipe_path = (
        ASSET_ROOT / "recipes" / "barrel" / "poison-brewing.json"
    )
    poison_recipes = validation.load_json(poison_recipe_path)
    grandmaster_recipes = [
        recipe for recipe in poison_recipes
        if isinstance(recipe, dict)
        and recipe.get("output", {}).get("code") ==
            "apprentice:poisonportion-grandmaster"
    ] if isinstance(poison_recipes, list) else []
    expected_spirits = {"game:alcoholportion"}
    expected_catalysts = {
        ("apprentice:dangerous-tissue", 1),
        ("apprentice:venomberry", 5),
        ("apprentice:gloamcap", 5),
    }
    actual_alternatives = {
        (
            recipe.get("ingredients", [{}])[0].get("code"),
            recipe.get("ingredients", [{}, {}])[1].get("code"),
            recipe.get("ingredients", [{}, {}])[1].get("quantity"),
        )
        for recipe in grandmaster_recipes
        if len(recipe.get("ingredients", [])) == 2
    }
    validation.check(
        len(grandmaster_recipes) == 3
        and actual_alternatives == {
            (spirit, catalyst, quantity)
            for spirit in expected_spirits
            for catalyst, quantity in expected_catalysts
        }
        and all(recipe.get("sealHours") == 72 for recipe in grandmaster_recipes),
        (
            "Grandmaster poison must expose three separate 72-hour catalyst "
            "core recipes: 1 tissue OR 5 venomberries OR 5 gloamcaps."
        ),
    )

    if isinstance(discoveries, list):
        ids = [value.get("Id") for value in discoveries if isinstance(value, dict)]
        validation.check(len(ids) == 28 and len(set(ids)) == 28, "2.7 requires 28 unique discovery definitions.")

    if isinstance(charges, list):
        charges_by_id = {
            value.get("Id"): value for value in charges if isinstance(value, dict)
        }
        expected_charge_contracts = {
            "starsteel": (7.5, "apprentice:blister-starsteel", 32),
            "aethersteel": (10.0, "apprentice:blister-aethersteel", 48),
        }
        validation.check(
            set(charges_by_id) == set(expected_charge_contracts),
            "2.7 requires exactly the Starsteel and Aethersteel charge definitions.",
        )
        for charge in charges:
            if not isinstance(charge, dict):
                continue
            inputs = charge.get("Inputs", [])
            total = sum(
                value.get("Quantity", 0)
                for value in inputs
                if isinstance(value, dict) and isinstance(value.get("Quantity"), int)
            )
            validation.check(
                total == charge.get("OutputQuantity") == 16,
                f"Cementation charge '{charge.get('Id')}' must be an exact 16-item multiset.",
            )
            apprentice_codes = [
                charge.get("Output"),
                charge.get("RefractoryCode"),
                *charge.get("RequiredItems", []),
                *[
                    value.get("Code")
                    for value in inputs
                    if isinstance(value, dict)
                ],
            ]
            for code in apprentice_codes:
                if isinstance(code, str) and code.startswith("apprentice:"):
                    validation.check(
                        code in collectibles,
                        f"Cementation charge '{charge.get('Id')}' references missing '{code}'.",
                    )

        for charge_id, (duration, output, fuel) in expected_charge_contracts.items():
            charge = charges_by_id.get(charge_id, {})
            validation.check(
                charge.get("DurationDays") == duration
                and charge.get("Output") == output
                and charge.get("OutputQuantity") == 16
                and charge.get("FuelCode") == "game:charcoal"
                and charge.get("FuelQuantity") == fuel,
                f"The {charge_id} exact-charge contract has drifted.",
            )

    for collection, fields in (
        (poisons, ("ArrowCode",)),
        (ecology, ("DropCode", "WorldgenBlockCode")),
    ):
        if not isinstance(collection, list):
            continue
        for definition in collection:
            if not isinstance(definition, dict):
                continue
            for field in fields:
                code = definition.get(field)
                if isinstance(code, str) and code.startswith("apprentice:"):
                    validation.check(
                        code in collectibles,
                        f"2.7 definition '{definition.get('Id')}' references missing '{code}'.",
                    )

    crafted_outputs: set[str] = set()
    for path in sorted((ASSET_ROOT / "recipes").rglob("*.json")):
        document = validation.load_json(path)
        recipes = document if isinstance(document, list) else [document]
        for index, recipe in enumerate(recipes):
            if not isinstance(recipe, dict):
                continue
            pattern = recipe.get("ingredientPattern")
            if isinstance(pattern, str):
                width = recipe.get("width")
                height = recipe.get("height")
                rows = pattern.split(",")
                validation.check(
                    isinstance(width, int)
                    and isinstance(height, int)
                    and len(rows) == height
                    and all(len(row) == width for row in rows),
                    f"{relative(path)} recipe {index} has inconsistent grid dimensions.",
                )
                symbols = {character for row in rows for character in row if character != " "}
                ingredients = recipe.get("ingredients", {})
                validation.check(
                    isinstance(ingredients, dict) and symbols <= set(ingredients),
                    f"{relative(path)} recipe {index} uses an undefined ingredient symbol.",
                )
            output = recipe.get("output", {})
            code = output.get("code") if isinstance(output, dict) else None
            if isinstance(code, str) and code.startswith("apprentice:"):
                crafted_outputs.add(code)
                validation.check(
                    code in collectibles,
                    f"{relative(path)} recipe {index} outputs missing '{code}'.",
                )
            raw_ingredients = recipe.get("ingredients", [])
            ingredients = (
                raw_ingredients.values()
                if isinstance(raw_ingredients, dict)
                else raw_ingredients
                if isinstance(raw_ingredients, list)
                else []
            )
            for ingredient in ingredients:
                if not isinstance(ingredient, dict):
                    continue
                ingredient_code = ingredient.get("code")
                if isinstance(ingredient_code, str) and ingredient_code.startswith("apprentice:"):
                    validation.check(
                        "*" in ingredient_code or ingredient_code in collectibles,
                        f"{relative(path)} recipe {index} uses missing '{ingredient_code}'.",
                    )

    metal_patch_path = ASSET_ROOT / "patches" / "2.7" / "metals.json"
    metal_patches = validation.load_json(metal_patch_path)
    metal_patch_entries = metal_patches if isinstance(metal_patches, list) else []
    invalid_patch_targets = {
        patch.get("file")
        for patch in metal_patch_entries
        if isinstance(patch, dict)
    } & {"game:blocktypes/metal/ingotpile.json"}
    validation.check(
        not invalid_patch_targets,
        (
            "The 1.22 ingot-pile asset is not located at "
            "game:blocktypes/metal/ingotpile.json; do not ship a patch that "
            "can never resolve."
        ),
    )
    metal_tiers = {
        patch.get("value", {}).get("code"): patch.get("value", {}).get("tier")
        for patch in metal_patch_entries
        if isinstance(patch, dict)
        and isinstance(patch.get("value"), dict)
    }
    validation.check(
        metal_tiers.get("starsteel") == 6
        and metal_tiers.get("aethersteel") == 7,
        "The metal patch must register Starsteel tier 6 and Aethersteel tier 7.",
    )
    ingot_variant_exclusions = {
        value
        for patch in metal_patch_entries
        if isinstance(patch, dict)
        and patch.get("file") == "game:itemtypes/resource/ingot.json"
        and patch.get("path") == "/skipVariants"
        and isinstance(patch.get("value"), list)
        for value in patch["value"]
        if isinstance(value, str)
    }
    validation.check(
        not ({"*-starsteel", "*-aethersteel"} & ingot_variant_exclusions),
        (
            "The vanilla ingot template must generate native game:ingot-* "
            "variants for both Apprentice metals."
        ),
    )
    chunk_path = ASSET_ROOT / "itemtypes" / "2.7" / "metalchunk.json"
    chunks = require_mapping(
        validation,
        validation.load_json(chunk_path),
        chunk_path,
    )
    chunk_variants = chunks.get("variantgroups", [{}])
    chunk_states = (
        chunk_variants[0].get("states", [])
        if isinstance(chunk_variants, list)
        and chunk_variants
        and isinstance(chunk_variants[0], dict)
        else []
    )
    chunk_combustibles = chunks.get("combustiblePropsByType", {})
    validation.check(
        chunks.get("class") == "ItemNugget"
        and chunks.get("shape", {}).get("base") == "game:item/nugget"
        and {"starsteel", "aethersteel"} <= set(chunk_states)
        and all(
            chunk_combustibles.get(
                f"metalchunk-{metal}", {}
            ).get("smeltedRatio") == 20
            for metal in ("starsteel", "aethersteel")
        ),
        (
            "Explicit Apprentice metal chunks must use the vanilla nugget "
            "mesh/transforms and smelt at twenty chunks per ingot."
        ),
    )
    partial_metal_smelting = {
        (patch.get("file"), patch.get("path"))
        for patch in metal_patch_entries
        if isinstance(patch, dict)
        and isinstance(patch.get("value"), dict)
        and patch["value"].get("smeltedRatio") == 20
    }
    for partial_asset in ("game:itemtypes/resource/metalbit.json",):
        for metal in ("starsteel", "aethersteel"):
            validation.check(
                (partial_asset, f"/combustiblePropsByType/*-{metal}")
                in partial_metal_smelting,
                f"{partial_asset} must smelt {metal} units at 20 per ingot.",
            )

    venom_patch_path = (
        ASSET_ROOT / "patches" / "2.7" / "venomberry-fruitingbush.json"
    )
    venom_patches = validation.load_json(venom_patch_path)
    validation.check(
        venom_patches == [],
        "Venomberry must not patch vanilla fruiting-bush variant tables.",
    )
    cutting_path = (
        ASSET_ROOT / "blocktypes" / "2.7" / "venomberrycutting.json"
    )
    cutting = require_mapping(
        validation, validation.load_json(cutting_path), cutting_path
    )
    validation.check(
        cutting.get("attributes", {}).get("maturedBlockCode") ==
            "apprentice:venomberryplant",
        "Venomberry cuttings must mature into the Apprentice-owned bush.",
    )

    venom_source_path = PROJECT_ROOT / "src" / "BlockVenomberryBush.cs"
    completion_adapter_path = (
        PROJECT_ROOT / "src" / "interaction" /
        "CompletionInteractionAdapter.cs"
    )
    try:
        venom_source = venom_source_path.read_text(encoding="utf-8-sig")
        completion_adapter_source = completion_adapter_path.read_text(
            encoding="utf-8-sig"
        )
    except (OSError, UnicodeError) as error:
        validation.errors.append(str(error))
        venom_source = ""
        completion_adapter_source = ""

    transition_index = venom_source.find("SetBlock(")
    give_index = venom_source.find("TryGiveItemstack(")
    fallback_index = venom_source.find("SpawnItemEntity(")
    validation.check(
        "venomberrycutting-free" in venom_source
        and "ItemStack harvest = new(berry, 4);" in venom_source
        and 0 <= transition_index < give_index < fallback_index,
        (
            "A ripe venomberry bush must atomically enter its regrowing "
            "cutting state before granting one four-berry harvest; world-drop "
            "fallback may happen only after the inventory grant fails."
        ),
    )
    validation.check(
        '"Apprentice.BlockVenomberryBush"' in completion_adapter_source,
        (
            "The Apprentice venomberry override must use the right-click "
            "Harvest adapter so its bounded inventory gain awards normal XP."
        ),
    )

    for arrow_tier in ("mild", "standard", "potent", "grandmaster"):
        arrow_path = (
            ASSET_ROOT / "itemtypes" / "2.7" /
            f"arrow-poison-{arrow_tier}.json"
        )
        arrow = require_mapping(
            validation,
            validation.load_json(arrow_path),
            arrow_path,
        )
        arrow_gui = arrow.get("guiTransform", {})
        arrow_attributes = arrow.get("attributes", {})
        validation.check(
            arrow.get("shape", {}).get("base") ==
                "game:item/tool/arrow-copper"
            and arrow.get("class") == "ApprenticePoisonArrow"
            and arrow.get("textures", {}).get("material", {}).get("base") ==
                f"apprentice:item/2.7/arrow-poison-{arrow_tier}-material"
            and arrow_attributes.get("arrowEntityCode") == "arrow-copper"
            and arrow_attributes.get("apprenticePoison") == arrow_tier
            and arrow_gui.get("rotation") ==
                {"x": -23, "y": -45, "z": -145}
            and arrow_gui.get("scale") == 1.44
            and bool(arrow.get("fpHandTransform"))
            and bool(arrow.get("tpHandTransform")),
            (
                f"{relative(arrow_path)} must use the native copper-arrow "
                "mesh, a tier-specific material, a valid projectile entity, "
                "and calibrated vanilla transforms."
            ),
        )

    anvil_path = ASSET_ROOT / "blocktypes" / "2.7" / "anvil-starsteel.json"
    anvil = require_mapping(
        validation,
        validation.load_json(anvil_path),
        anvil_path,
    )
    anvil_texture = (
        anvil.get("textures", {}).get("metal", {}).get("base")
    )
    validation.check(
        anvil_texture == "apprentice:block/2.7/starsteel",
        (
            "The Starsteel anvil must use an opaque block material texture, "
            "not the transparent ingot inventory icon on every face."
        ),
    )
    anvil_texture_path = (
        ASSET_ROOT / "textures" / "block" / "2.7" / "starsteel.png"
    )
    validation.check(
        anvil_texture_path.is_file(),
        f"Missing Starsteel anvil material {relative(anvil_texture_path)}.",
    )

    icon_shape_path = ASSET_ROOT / "shapes" / "item" / "2.7" / "icon-flat.json"
    icon_shape = require_mapping(
        validation,
        validation.load_json(icon_shape_path),
        icon_shape_path,
    )
    icon_elements = icon_shape.get("elements", [])
    south_uv = None
    icon_faces = None
    if isinstance(icon_elements, list) and icon_elements and isinstance(icon_elements[0], dict):
        icon_faces = icon_elements[0].get("faces", {})
        south_uv = (
            icon_elements[0]
            .get("faces", {})
            .get("south", {})
            .get("uv")
        )
    validation.check(
        south_uv == [0, 128, 128, 0],
        "The shared 2.7 item icon face must cancel the engine's GUI UV inversion.",
    )
    validation.check(
        isinstance(icon_faces, dict) and set(icon_faces) == {"south"},
        (
            "The shared 2.7 item icon must render only its GUI-facing south "
            "plane; a second textured face overlaps it upside-down."
        ),
    )
    flat_icon_names = {
        "advancedtrapkit", "armorpaddingkit", "arrow-poison-grandmaster",
        "arrow-poison-mild", "arrow-poison-potent", "arrow-poison-standard",
        "atlatl", "blister-aethersteel", "blister-starsteel",
        "chain-starsteel", "compositebow", "dangerous-tissue", "gloamcap",
        "graftingkit", "grandmaster-refractory", "ingot-aethersteel",
        "ingot-starsteel", "mastercookbook", "masterfishingrod",
        "masterforgeplans", "mastersurveykit", "masterweaponpatterns",
        "plate-aethersteel", "plate-starsteel", "scales-aethersteel",
        "sewingkit", "towershield", "upgraded-refractory", "venomberry",
        "veterinarykit",
    }
    flat_icon_root = ASSET_ROOT / "textures" / "item" / "2.7"
    for icon_name in sorted(flat_icon_names):
        icon_path = flat_icon_root / f"{icon_name}.png"
        validation.check(
            icon_path.is_file(),
            f"Missing coherent 2.7 inventory icon {relative(icon_path)}.",
        )
        if icon_path.is_file():
            try:
                icon_data = icon_path.read_bytes()
                if len(icon_data) < 26 or not icon_data.startswith(b"\x89PNG\r\n\x1a\n"):
                    raise ValueError("not a complete PNG header")
                icon_width, icon_height = struct.unpack(">II", icon_data[16:24])
                validation.check(
                    (icon_width, icon_height) == (128, 128),
                    (
                        f"{relative(icon_path)} must be a transparent-ready "
                        f"128x128 inventory icon, got {icon_width}x{icon_height}."
                    ),
                )
                validation.check(
                    icon_data[25] in (4, 6),
                    f"{relative(icon_path)} must contain an alpha channel.",
                )
            except (OSError, struct.error, ValueError) as error:
                validation.errors.append(f"{relative(icon_path)}: {error}")

    for item_path in sorted((ASSET_ROOT / "itemtypes" / "2.7").glob("*.json")):
        item_document = require_mapping(
            validation,
            validation.load_json(item_path),
            item_path,
        )
        shape_base = item_document.get("shape", {}).get("base")
        validation.check(
            isinstance(shape_base, str)
            and shape_base != "apprentice:item/2.7/icon-flat",
            (
                f"{relative(item_path)} must use a real 3D model; "
                "flat icon planes are not valid item shapes."
            ),
        )
        if isinstance(shape_base, str) and shape_base.startswith(
            "apprentice:item/"
        ):
            shape_path = apprentice_asset_path(
                "shapes",
                shape_base,
                ".json",
            )
            validation.check(
                shape_path is not None and shape_path.is_file(),
                (
                    f"{relative(item_path)} references missing 3D shape "
                    f"'{shape_base}'."
                ),
            )

    shield_path = ASSET_ROOT / "itemtypes" / "2.7" / "towershield.json"
    shield = require_mapping(
        validation,
        validation.load_json(shield_path),
        shield_path,
    )
    shield_stats = shield.get("attributes", {}).get("shield", {})
    shield_fp = shield.get("fpHandTransform", {})
    shield_hand = shield.get("tpHandTransform", {})
    shield_offhand = shield.get("tpOffHandTransform", {})
    validation.check(
        shield.get("class") == "ItemShield"
        and shield.get("storageFlags") == 257
        and shield.get("shape", {}).get("base") ==
            "apprentice:item/2.7/tower-shield"
        and bool(shield.get("fpHandTransform"))
        and shield_fp.get("origin") == {
            "x": 0.5, "y": 0.5, "z": 0.609375
        }
        and shield_offhand.get("translation") == {
            "x": -0.67, "y": -0.6, "z": -0.78
        }
        and shield_offhand.get("rotation") == {
            "x": -4, "y": 173, "z": 90
        }
        and shield_offhand.get("origin") == {
            "x": 0.5, "y": 0.5, "z": 0.609375
        }
        and shield_offhand.get("scale") == 0.82
        and shield_hand.get("rotation") == {
            "x": -4, "y": 4, "z": 90
        }
        and shield_hand.get("origin") == {
            "x": 0.5, "y": 0.5, "z": 0.609375
        }
        and isinstance(shield_stats, dict)
        and isinstance(shield_stats.get("protectionChance"), dict),
        (
            "Tower Shield must use the fixed ItemShield class, a dedicated "
            "tall shield mesh, grip-derived hand anchors, horizontal pose, "
            "and native shield stats."
        ),
    )
    shield_grip = (8, 8, 9.75)
    validation.check(
        points_close(
            transformed_model_point(shield_grip, shield_hand),
            NATIVE_HELD_GRIP_TARGET,
        ),
        (
            "Tower Shield rear-grip centre must land on the same hand-space "
            "point as the approved Composite Bow grip in the main hand. The "
            "off-hand pose must use its raised shield-specific anchor."
        ),
    )
    shield_chances = shield_stats.get("protectionChance", {})
    validation.check(
        shield_chances.get("active-projectile") == 1.0
        and shield_chances.get("passive-projectile") == 0.35,
        (
            "Tower Shield must block projectiles at 100% while actively "
            "raised and retain the 35% passive projectile chance."
        ),
    )
    reviewed_root = PROJECT_ROOT / "tools" / "model_sources" / "reviewed"
    reviewed_models = {
        "tower-shield",
        "master-fishing-rod",
        "grandmaster-spear",
        "kit-trap",
        "kit-armor-upgrade",
        "kit-weapon-upgrade",
        "kit-tool-upgrade",
        "kit-first-aid",
    }
    reviewed_documents = {}
    for model_name in sorted(reviewed_models):
        reviewed_path = reviewed_root / f"{model_name}.json"
        production_path = (
            ASSET_ROOT / "shapes" / "item" / "2.7" /
            f"{model_name}.json"
        )
        reviewed_document = require_mapping(
            validation,
            validation.load_json(reviewed_path),
            reviewed_path,
        )
        production_document = require_mapping(
            validation,
            validation.load_json(production_path),
            production_path,
        )
        reviewed_documents[model_name] = production_document
        validation.check(
            production_document == reviewed_document,
            (
                f"{relative(production_path)} must exactly match its "
                "approved production model source."
            ),
        )
        validation.check(
            "game:block/stone/granite" not in json.dumps(reviewed_document),
            (
                f"{relative(reviewed_path)} retains the invalid granite "
                "texture alias that causes client startup warnings."
            ),
        )

    shield_elements = reviewed_documents.get("tower-shield", {}).get(
        "elements", []
    )
    shield_by_name = {
        element.get("name"): element for element in shield_elements
        if isinstance(element, dict)
    }
    validation.check(
        all(
            name in shield_by_name
            for name in (
                "flat-frame-left", "flat-frame-right",
                "flat-frame-top", "flat-frame-bottom",
                "upper-left-diagonal", "upper-right-diagonal",
                "lower-left-diagonal", "lower-right-diagonal",
                "rear-hand-grip", "forearm-strap-upper",
                "forearm-strap-lower",
            )
        )
        and shield_by_name.get("main-face", {}).get("to", [0, 0])[1]
            - shield_by_name.get("main-face", {}).get("from", [0, 0])[1]
            >= 14
        and shield_by_name.get("main-face", {}).get("to", [0, 0, 0])[2]
            - shield_by_name.get("main-face", {}).get("from", [0, 0, 0])[2]
            <= 0.8,
        (
            "Tower Shield must retain the approved tall symmetric Runebound "
            "front, flat silver frame, shallow core and rear-side straps."
        ),
    )
    fishing_elements = reviewed_documents.get(
        "master-fishing-rod", {}
    ).get("elements", [])
    fishing_element_names = {
        element.get("name") for element in fishing_elements
        if isinstance(element, dict)
    }
    validation.check(
        all(
            f"grip-wrap-{index}" in fishing_element_names
            for index in range(1, 7)
        )
        and "reel-gold-inlay" in fishing_element_names
        and "reel-crank-arm" in fishing_element_names
        and "reel-crank-knob" in fishing_element_names,
        (
            "Master Fishing Rod must retain the approved Gilded Angler "
            "wrapped grip, working reel silhouette and gold reel details."
        ),
    )
    spear_elements = reviewed_documents.get(
        "grandmaster-spear", {}
    ).get("elements", [])
    spear_element_names = {
        element.get("name") for element in spear_elements
        if isinstance(element, dict)
    }
    validation.check(
        "sun-core-front" in spear_element_names
        and "sun-core-rear" in spear_element_names,
        (
            "Grandmaster Spear must retain the approved Sunlance emblem on "
            "both faces of its spearhead."
        ),
    )

    bow_path = ASSET_ROOT / "itemtypes" / "2.7" / "compositebow.json"
    bow = require_mapping(validation, validation.load_json(bow_path), bow_path)
    bow_shape = bow.get("shape", {})
    expected_bow_shapes = [
        "apprentice:item/2.7/composite-bow",
        "apprentice:item/2.7/composite-bow-charge1",
        "apprentice:item/2.7/composite-bow-charge2",
        "apprentice:item/2.7/composite-bow-charge3",
        "apprentice:item/2.7/composite-bow-charge4",
        "apprentice:item/2.7/composite-bow-charge5",
    ]
    validation.check(
        bow.get("class") == "ApprenticeCompositeBow"
        and bow.get("attributes", {}).get("aimAnimation") == "bowaimrecurve"
        and bow.get("attributes", {}).get("damage") == 6.5
        and bow.get("attributes", {}).get("statModifier", {}).get(
            "rangedWeaponsAcc"
        ) == 0.4
        and bow_shape.get("base") == expected_bow_shapes[0]
        and bow_shape.get("alternates") == [
            {"base": code} for code in expected_bow_shapes[1:]
        ],
        (
            "Composite Bow must use six Apprentice-owned recurve draw meshes, "
            "its extended ItemBow subclass, 6.5 piercing damage, and +40% "
            "ranged accuracy."
        ),
    )
    for index, shape_code in enumerate(expected_bow_shapes):
        bow_shape_path = apprentice_asset_path("shapes", shape_code, ".json")
        bow_model = require_mapping(
            validation,
            validation.load_json(bow_shape_path) if bow_shape_path else None,
            bow_shape_path or bow_path,
        )
        element_names = {
            element.get("name")
            for element in bow_model.get("elements", [])
            if isinstance(element, dict)
        }
        required_names = {
            "leather-grip", "lower-riser-neck", "upper-riser-neck",
            "upper-limb-1", "upper-limb-6",
            "lower-limb-1", "lower-limb-6", "upper-string",
            "lower-string",
        }
        if index:
            required_names |= {"arrow-shaft", "arrowhead-point"}
        elements_by_name = {
            element.get("name"): element
            for element in bow_model.get("elements", [])
            if isinstance(element, dict)
        }
        grip = elements_by_name.get("leather-grip", {})
        grip_from = grip.get("from", [0, 0, 0])
        grip_to = grip.get("to", [0, 0, 0])
        grip_span = [
            grip_to[axis] - grip_from[axis]
            for axis in range(3)
        ] if len(grip_from) == 3 and len(grip_to) == 3 else [0, 0, 0]
        upper_limb = elements_by_name.get("upper-limb-1", {})
        grip_faces = grip.get("faces", {})
        native_bow_axes = (
            grip_span[0] > grip_span[1]
            and grip_span[0] > grip_span[2]
            and "rotationY" in upper_limb
            and "rotationZ" not in upper_limb
            and all(
                isinstance(face, dict)
                and face.get("uv") == [0, 0, 16, 16]
                for face in grip_faces.values()
            )
            and all(
                isinstance(grip_faces.get(face_name), dict)
                and grip_faces[face_name].get("rotation") == 90
                for face_name in ("north", "south", "up", "down")
            )
        )
        if index:
            arrow = elements_by_name.get("arrow-shaft", {})
            arrowhead = elements_by_name.get("arrowhead-point", {})
            fletching = elements_by_name.get("upper-fletching", {})
            arrow_from = arrow.get("from", [0, 0, 0])
            arrow_to = arrow.get("to", [0, 0, 0])
            arrowhead_from = arrowhead.get("from", [0, 0, 0])
            arrowhead_to = arrowhead.get("to", [0, 0, 0])
            fletching_from = fletching.get("from", [0, 0, 0])
            fletching_to = fletching.get("to", [0, 0, 0])
            arrow_span = [
                arrow_to[axis] - arrow_from[axis]
                for axis in range(3)
            ] if len(arrow_from) == 3 and len(arrow_to) == 3 else [0, 0, 0]
            grip_center_z = (grip_from[2] + grip_to[2]) / 2
            arrowhead_center_z = (
                arrowhead_from[2] + arrowhead_to[2]
            ) / 2
            fletching_center_z = (
                fletching_from[2] + fletching_to[2]
            ) / 2
            native_bow_axes = (
                native_bow_axes
                and arrow_span[2] > arrow_span[0]
                and arrow_span[2] > arrow_span[1]
                # Native bows aim toward -Z. The head must be beyond the
                # riser on the target side while the fletching remains beside
                # the string/player. Axis-only validation missed this reversal.
                and arrowhead_center_z < grip_center_z < fletching_center_z
            )

        def rounded_vector(element: dict[str, Any], key: str) -> list[float]:
            value = element.get(key, [])
            return [round(float(component), 4) for component in value]

        lower_riser = elements_by_name.get("lower-riser-neck", {})
        arrow_rest = elements_by_name.get("arrow-rest", {})
        lower_limb = elements_by_name.get("lower-limb-1", {})
        lower_facing = elements_by_name.get("lower-horn-facing-1", {})
        upper_string = elements_by_name.get("upper-string", {})
        lower_string = elements_by_name.get("lower-string", {})
        expected_lower_limb_rotations = [
            134.7152, 136.5968, 138.6822, 141.0, 141.8973, 142.8305,
        ]
        expected_lower_facing_rotations = [
            131.7152, 133.5968, 135.6822, 138.0, 138.8973, 139.8305,
        ]
        expected_arrow_nock_z = [
            None, 13.05, 14.9, 16.9, 19.336, 21.622,
        ]
        owner_editor_geometry = (
            "upper-far-gold-point" not in element_names
            and "lower-far-gold-point" not in element_names
            and rounded_vector(lower_riser, "from") == [6.8, -0.62, 7.61]
            and rounded_vector(lower_riser, "to") == [8.3, 0.62, 8.39]
            and lower_riser.get("rotationY") == -180.0
            and rounded_vector(arrow_rest, "from") == [9.6, 0.6, 8.45]
            and rounded_vector(arrow_rest, "to") == [9.77, 1.25, 9.05]
            and lower_limb.get("rotationY") == expected_lower_limb_rotations[index]
            and lower_facing.get("rotationY") == expected_lower_facing_rotations[index]
            and round(float(upper_string.get("from", [0, 0])[1]), 4) == 0.86
            and round(float(lower_string.get("from", [0, 0])[1]), 4) == 0.86
        )
        if index:
            owner_editor_geometry = (
                owner_editor_geometry
                and rounded_vector(arrow, "from")[:2] == [9.76, 0.81]
                and rounded_vector(arrow, "to")[:2] == [10.04, 1.09]
                and round(float(arrow.get("to", [0, 0, 0])[2]), 4) ==
                    expected_arrow_nock_z[index]
            )
        validation.check(
            required_names <= element_names
            and bow_model.get("textures", {}).get("laminate") ==
                "apprentice:item/2.7/compositebow-material"
            and bow_model.get("textures", {}).get("gripwrap") ==
                "apprentice:item/2.7/compositebow-grip-wrap"
            and not any(
                name.startswith("rope-wrap-")
                for name in element_names
                if isinstance(name, str)
            ),
            (
                f"{relative(bow_shape_path or bow_path)} must keep a connected, "
                "materially distinct bow silhouette and a single textured grip "
                "without protruding wrap geometry"
                + (" and visible nocked arrow." if index else ".")
            ),
        )
        validation.check(
            native_bow_axes,
            (
                f"{relative(bow_shape_path or bow_path)} must use Vintage "
                "Story's native bow axes: limbs/grip along X, thin section "
                "along Y, arrowhead toward -Z, and fletching/string toward "
                "+Z. The grip must map the complete 16x16 wrap texture on "
                "every face. An upright, direction-reversed, or implicit-UV "
                "model is invalid under the native held-item transform."
            ),
        )
        validation.check(
            owner_editor_geometry,
            (
                f"{relative(bow_shape_path or bow_path)} must preserve the "
                "project owner's propagated Model Creator edits: clean lower "
                "riser/root, shifted string and arrow/rest, state-specific "
                "limb flex, and no far-side gold points."
            ),
        )

    validation.check(
        points_close(
            transformed_model_point((8, 0, 8), bow.get("tpHandTransform", {})),
            NATIVE_HELD_GRIP_TARGET,
        ),
        (
            "Composite Bow grip centre must resolve to the native recurve "
            "grip's third-person hand-space point using the engine's actual "
            "scale-before-translation matrix order."
        ),
    )

    approved_bow_textures = {
        "compositebow-material.png":
            "98210ddb9738939deb6c9fe7d2ad2bc992b7d7f6c980f646078568308a2649a1",
        "compositebow-grip-wrap.png":
            "24248470abaf38954d87490c6ba728cdcb252eba57bc065dd04dd57866450b3c",
    }
    bow_texture_root = ASSET_ROOT / "textures" / "item" / "2.7"
    for texture_name, expected_hash in approved_bow_textures.items():
        texture_path = bow_texture_root / texture_name
        try:
            actual_hash = hashlib.sha256(texture_path.read_bytes()).hexdigest()
        except OSError as error:
            validation.errors.append(f"{relative(texture_path)}: {error}")
            continue
        validation.check(
            actual_hash == expected_hash,
            (
                f"{relative(texture_path)} must retain the approved darkwood/"
                "gold/oxblood Composite Bow palette."
            ),
        )

    spear_path = ASSET_ROOT / "itemtypes" / "2.7" / "atlatl.json"
    spear = require_mapping(
        validation, validation.load_json(spear_path), spear_path
    )
    spear_attributes = spear.get("attributes", {})
    validation.check(
        not str(spear.get("shape", {}).get("base", "")).startswith(
            "game:item/tool/spear/ornate"
        )
        and "ornategold" not in str(
            spear_attributes.get("spearEntityCode", "")
        )
        and "ornategold" not in json.dumps(
            spear_attributes.get("spearEntityCodeByType", {})
        ),
        (
            "Grandmaster Spear must use Apprentice-owned held and projectile "
            "geometry; recoloring the vanilla Ornate Gold Spear is forbidden."
        ),
    )
    validation.check(
        spear.get("tpHandTransform", {}).get("origin") == {
            "x": 0.375, "y": 0.5, "z": 0.5
        }
        and spear.get("fpHandTransform", {}).get("origin") == {
            "x": 0.375, "y": 0.5, "z": 0.5
        }
        and points_close(
            transformed_model_point(
                (6, 8, 8), spear.get("tpHandTransform", {})
            ),
            NATIVE_HELD_GRIP_TARGET,
        ),
        (
            "Grandmaster Spear rear-grip centre must resolve to the native "
            "held-item grip point. The named rear grip must define the pivot "
            "instead of orbiting around the model's lower bounding corner."
        ),
    )

    for model_code in (
        "advancedtrapkit",
        "armorpaddingkit",
        "graftingkit",
        "mastercookbook",
        "mastersurveykit",
        "masterweaponpatterns",
        "sewingkit",
        "veterinarykit",
    ):
        model_path = (
            ASSET_ROOT / "itemtypes" / "2.7" / f"{model_code}.json"
        )
        model_item = require_mapping(
            validation,
            validation.load_json(model_path),
            model_path,
        )
        shape_base = model_item.get("shape", {}).get("base", "")
        validation.check(
            shape_base != "apprentice:item/2.7/icon-flat"
            and bool(model_item.get("fpHandTransform"))
            and bool(model_item.get("tpHandTransform"))
            and bool(model_item.get("groundTransform")),
            (
                f"{model_code} is a usable item and must have a 3D model "
                "with first-person, third-person and ground transforms."
            ),
        )

    trap_source_path = PROJECT_ROOT / "src" / "AdvancedTrapKit.cs"
    try:
        trap_source = trap_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(trap_source_path)}: {error}")
        trap_source = ""
    validation.check(
        "override float OnGettingBroken" in trap_source
        and "override bool OnBlockInteractStart" in trap_source
        and "trap.SetTimedRearming" in trap_source
        and "RearmDurationSeconds = 5f" in trap_source
        and "ApplyRearmingVisualState" in trap_source
        and "rearmingProgress >= 0.2f" in trap_source
        and "rearmingProgress >= 0.4f" in trap_source
        and "rearmingProgress >= 0.6f" in trap_source
        and "rearmingProgress >= 0.8f" in trap_source
        and '"opening3"' in trap_source
        and '"opening4"' in trap_source
        and "return remainingResistance" in trap_source,
        (
            "Advanced Trap must rearm through a cancellable five-second "
            "right-click action with one replicated state per second."
        ),
    )
    legacy_trap_path = (
        ASSET_ROOT / "blocktypes" / "2.7" / "advancedtrap-legacy.json"
    )
    legacy_trap = require_mapping(
        validation,
        validation.load_json(legacy_trap_path),
        legacy_trap_path,
    )
    legacy_drops = legacy_trap.get("drops", [])
    validation.check(
        legacy_trap.get("code") == "advancedtrap"
        and "variantgroups" not in legacy_trap
        and legacy_trap.get("class") == "ApprenticeLegacyAdvancedTrap"
        and legacy_trap.get("entityClass") == "ApprenticeAdvancedTrap"
        and legacy_trap.get("shape", {}).get("base") ==
            "apprentice:block/2.7/advancedtrap-triggered"
        and legacy_trap.get("collisionBoxes") is None
        and isinstance(legacy_drops, list)
        and any(
            isinstance(drop, dict)
            and drop.get("code") == "apprentice:advancedtrapkit"
            for drop in legacy_drops
        ),
        (
            "The exact apprentice:advancedtrap compatibility block must load "
            "old saves as a triggered, interactable trap that still drops its "
            "kit."
        ),
    )
    mod_system_path = PROJECT_ROOT / "src" / "ApprenticeModSystem.cs"
    try:
        mod_system_source = mod_system_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(mod_system_path)}: {error}")
        mod_system_source = ""
    composite_bow_class_path = PROJECT_ROOT / "src" / "ItemCompositeBow.cs"
    try:
        composite_bow_class_source = composite_bow_class_path.read_text(
            encoding="utf-8-sig"
        )
    except (OSError, UnicodeError) as error:
        validation.errors.append(
            f"{relative(composite_bow_class_path)}: {error}"
        )
        composite_bow_class_source = ""
    validation.check(
        "public sealed class BlockLegacyAdvancedTrap" in trap_source
        and 'protected override string State => "triggered"' in trap_source
        and "Block is BlockLegacyAdvancedTrap" in trap_source
        and '"ApprenticeLegacyAdvancedTrap"' in mod_system_source
        and "typeof(BlockLegacyAdvancedTrap)" in mod_system_source,
        (
            "Legacy Advanced Trap migration must be registered on both the "
            "block and block-entity state paths."
        ),
    )
    validation.check(
        'BowAssetFingerprint = "BOW-DARKWOOD-OXBLOOD-C-AXIS2-EDIT1-UV1-DRAW5"' in mod_system_source
        and "LogBowAssetFingerprint(api)" in mod_system_source
        and "configuredShapeCodes.SequenceEqual(ExpectedBowShapeCodes)" in
            mod_system_source
        and "api.Assets.TryGet" in mod_system_source,
        (
            "The approved Composite Bow must emit its runtime fingerprint and "
            "verify all configured draw-state assets in the loaded mod copy."
        ),
    )
    validation.check(
        'ReviewedAssetFingerprint = "ITEMS-RUNEBOUND5-GILDED2-SUNLANCE2-KITS-D-TRAP-C5"' in mod_system_source
        and "LogReviewedAssetFingerprint(api)" in mod_system_source
        and "ExpectedReviewedAssetPaths" in mod_system_source,
        (
            "The approved Tower Shield, fishing rod, Sunlance, upgrade kits, "
            "First Aid Kit, and five-stage Bear Trap must emit one runtime "
            "asset fingerprint and verify their loaded files."
        ),
    )
    validation.check(
        '"ApprenticeCompositeBow"' in mod_system_source
        and "typeof(ItemCompositeBow)" in mod_system_source
        and "class ItemCompositeBow : ItemBow" in composite_bow_class_source
        and "MaximumRenderVariant = 5" in composite_bow_class_source
        and "RenderVariantsPerSecond = 4f" in composite_bow_class_source
        and 'TempAttributes.SetInt("renderVariant", renderVariant)' in
            composite_bow_class_source
        and 'Attributes.SetInt("renderVariant", renderVariant)' in
            composite_bow_class_source,
        (
            "Composite Bow must register its ItemBow subclass and drive all "
            "six render variants on both temporary client and synchronized "
            "item attributes."
        ),
    )
    trap_stage_root = (
        PROJECT_ROOT / "tools" / "model_sources" / "beartrap-c"
    )
    trap_state_sources = {
        "triggered": "stage-01-1s.json",
        "opening1": "stage-01-1s.json",
        "opening2": "stage-02-2s.json",
        "opening3": "stage-03-3s.json",
        "opening4": "stage-04-4s.json",
        "armed": "stage-05-5s.json",
    }
    fixed_trap_prefixes = (
        "base-", "left-jaw-pivot", "right-jaw-pivot", "chain-",
        "anchor-chain", "left-actuator-", "left-longspring-",
        "right-fixed-hinge-", "pan-pivot-", "dog-pivot",
    )
    fixed_trap_reference = None
    for state_name, stage_file in trap_state_sources.items():
        trap_shape_path = (
            ASSET_ROOT / "shapes" / "block" / "2.7" /
            f"advancedtrap-{state_name}.json"
        )
        trap_shape = require_mapping(
            validation,
            validation.load_json(trap_shape_path),
            trap_shape_path,
        )
        stage_path = trap_stage_root / stage_file
        stage_shape = require_mapping(
            validation,
            validation.load_json(stage_path),
            stage_path,
        )
        trap_elements = trap_shape.get("elements", [])
        tooth_count = sum(
            1 for element in trap_elements
            if isinstance(element, dict) and "tooth" in str(element.get("name", ""))
        ) if isinstance(trap_elements, list) else 0
        validation.check(
            trap_shape == stage_shape and tooth_count >= 10,
            (
                f"Bear Trap {state_name} must use the approved option C "
                "mechanical stage and retain its toothed jaw arches."
            ),
        )
        fixed_elements = {
            element.get("name"): element
            for element in trap_elements
            if isinstance(element, dict)
            and str(element.get("name", "")).startswith(
                fixed_trap_prefixes
            )
        }
        if fixed_trap_reference is None:
            fixed_trap_reference = fixed_elements
        else:
            validation.check(
                fixed_elements == fixed_trap_reference,
                (
                    f"Bear Trap {state_name} moved its fixed U frame, "
                    "silver rods, black spring or pivots."
                ),
            )

    upgrade_source_path = PROJECT_ROOT / "src" / "ItemUpgradeKits.cs"
    try:
        upgrade_source = upgrade_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(upgrade_source_path)}: {error}")
        upgrade_source = ""
    expected_upgrade_items = {
        "armorpaddingkit": ("kit-armor-upgrade", "armor"),
        "masterweaponpatterns": ("kit-weapon-upgrade", "weapon"),
        "graftingkit": ("kit-tool-upgrade", "tool"),
    }
    for item_code, (shape_code, category) in expected_upgrade_items.items():
        item_path = ASSET_ROOT / "itemtypes" / "2.7" / f"{item_code}.json"
        item_document = require_mapping(
            validation,
            validation.load_json(item_path),
            item_path,
        )
        validation.check(
            item_document.get("class") == "ApprenticeUpgradeKit"
            and item_document.get("shape", {}).get("base") ==
                f"apprentice:item/2.7/{shape_code}"
            and item_document.get("attributes", {}).get(
                "upgradeCategory"
            ) == category,
            f"{item_code} must use its approved one-use upgrade kit.",
        )
    first_aid_path = ASSET_ROOT / "itemtypes" / "2.7" / "veterinarykit.json"
    first_aid = require_mapping(
        validation,
        validation.load_json(first_aid_path),
        first_aid_path,
    )
    validation.check(
        first_aid.get("class") == "ApprenticeFirstAidKit"
        and first_aid.get("shape", {}).get("base") ==
            "apprentice:item/2.7/kit-first-aid"
        and first_aid.get("attributes", {}).get("healAmount") == 8
        and "durabilityUpgrade20" in upgrade_source
        and "Math.Ceiling(__result * 1.2)" in upgrade_source
        and "MatchesCategory" in upgrade_source
        and "ItemFirstAidKit" in upgrade_source
        and '"ApprenticeUpgradeKit"' in mod_system_source
        and '"ApprenticeFirstAidKit"' in mod_system_source
        and "GetMaxDurabilityPostfix" in mod_system_source,
        (
            "Upgrade and First Aid kits must have registered server-owned "
            "mechanics, one-use 20% durability state and any-entity healing."
        ),
    )

    fishing_path = ASSET_ROOT / "itemtypes" / "2.7" / "masterfishingrod.json"
    fishing = require_mapping(
        validation,
        validation.load_json(fishing_path),
        fishing_path,
    )
    fishing_source_path = PROJECT_ROOT / "src" / "ItemMasterFishingRod.cs"
    try:
        fishing_source = fishing_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(fishing_source_path)}: {error}")
        fishing_source = ""
    validation.check(
        fishing.get("class") == "ApprenticeMasterFishingRod"
        and fishing.get("shape", {}).get("base") ==
            "apprentice:item/2.7/master-fishing-rod"
        and fishing.get("attributes", {}).get("ropelessShape", {}).get(
            "base"
        ) == "apprentice:item/2.7/master-fishing-rod"
        and bool(fishing.get("fpHandTransform"))
        and bool(fishing.get("tpHandTransform"))
        and "ItemMasterFishingRod : ItemFishingPole" in fishing_source
        and "StopFishingAnimations" in fishing_source
        and '"ApprenticeMasterFishingRod"' in mod_system_source,
        (
            "Master Fishing Rod must extend the native fishing-pole behavior, "
            "release stale fishing animations and retain visible first- and "
            "third-person 3D transforms."
        ),
    )

    for item_code in ("mastercookbook", "mastersurveykit", "sewingkit"):
        item_path = ASSET_ROOT / "itemtypes" / "2.7" / f"{item_code}.json"
        item = require_mapping(
            validation,
            validation.load_json(item_path),
            item_path,
        )
        gui_z = item.get("guiTransform", {}).get("rotation", {}).get("z")
        validation.check(
            isinstance(gui_z, (int, float)) and -90 <= gui_z <= 90,
            f"{item_code} GUI transform must keep its functional top upright.",
        )

    spear_path = ASSET_ROOT / "itemtypes" / "2.7" / "atlatl.json"
    spear = require_mapping(validation, validation.load_json(spear_path), spear_path)
    validation.check(
        spear.get("class") == "ApprenticeGrandmasterSpear"
        and spear.get("shape", {}).get("base") ==
            "apprentice:item/2.7/grandmaster-spear"
        and spear.get("attributes", {}).get("spearEntityCode") ==
            "apprentice:atlatl"
        and "spearEntityCodeByType" not in spear.get("attributes", {}),
        (
            "Grandmaster Spear must use the guarded native spear lifecycle "
            "and resolve one exact Apprentice projectile entity."
        ),
    )
    validation.check(
        spear.get("guiTransform", {}).get("origin") == {
            "x": 1.125, "y": 0.5, "z": 0.5
        }
        and spear.get("guiTransform", {}).get("scale") == 0.67,
        (
            "Grandmaster Spear inventory transform must use the custom "
            "Sunlance centre and fit the complete model inside a fixed slot."
        ),
    )
    spear_entity_path = (
        ASSET_ROOT / "entities" / "2.7" / "atlatl.json"
    )
    spear_entity = require_mapping(
        validation,
        validation.load_json(spear_entity_path),
        spear_entity_path,
    )
    spear_client = spear_entity.get("client", {})
    spear_server = spear_entity.get("server", {})
    client_behavior_codes = {
        behavior.get("code")
        for behavior in spear_client.get("behaviors", [])
        if isinstance(behavior, dict)
    }
    server_behavior_codes = {
        behavior.get("code")
        for behavior in spear_server.get("behaviors", [])
        if isinstance(behavior, dict)
    }
    validation.check(
        spear_entity.get("code") == "atlatl"
        and spear_entity.get("class") == "EntityProjectile"
        and spear_entity.get("attributes", {}).get("isProjectile") is True
        and spear_client.get("renderer") == "Shape"
        and spear_client.get("shape", {}).get("base") ==
            "apprentice:item/2.7/grandmaster-spear"
        and spear_client.get("shape", {}).get("offsetX") == -0.8125
        and {"passivephysics", "interpolateposition"}.issubset(
            client_behavior_codes
        )
        and {"passivephysics", "despawn"}.issubset(
            server_behavior_codes
        ),
        (
            "apprentice:atlatl must be a complete EntityProjectile using the "
            "Apprentice Grandmaster Spear shape and native spear physics."
        ),
    )
    spear_source_path = PROJECT_ROOT / "src" / "ItemGrandmasterSpear.cs"
    try:
        spear_source = spear_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(spear_source_path)}: {error}")
        spear_source = ""
    validation.check(
        "public sealed class ItemGrandmasterSpear : ItemSpear" in spear_source
        and "GetEntityType" in spear_source
        and "if (entityType == null)" in spear_source
        and "base.OnHeldInteractStop" in spear_source
        and '"ApprenticeGrandmasterSpear"' in mod_system_source
        and "typeof(ItemGrandmasterSpear)" in mod_system_source,
        (
            "Grandmaster Spear must guard projectile lookup at runtime and "
            "remain registered as an Apprentice item class."
        ),
    )

    poison_portion_path = (
        ASSET_ROOT / "itemtypes" / "2.7" / "poisonportion.json"
    )
    poison_portion = require_mapping(
        validation,
        validation.load_json(poison_portion_path),
        poison_portion_path,
    )
    container_props = (
        poison_portion.get("attributes", {})
        .get("waterTightContainerProps", {})
    )
    expected_poison_textures = {
        f"*-{tier}": {
            "base": f"apprentice:item/2.7/poison-fluid-{tier}"
        }
        for tier in ("mild", "standard", "potent", "grandmaster")
    }
    actual_container_textures = (
        container_props.get("textureByType", {})
        if isinstance(container_props, dict)
        else {}
    )
    actual_item_textures = poison_portion.get("textureByType", {})
    validation.check(
        isinstance(container_props, dict)
        and container_props.get("itemsPerLitre") == 10,
        (
            "Arrow poison must use ten 100 mL portions per litre so the "
            "0.1 L arrow-coating recipe consumes exactly one portion."
        ),
    )
    for variant, expected in expected_poison_textures.items():
        poison_texture_path = apprentice_asset_path(
            "textures",
            expected["base"],
            ".png",
        )
        validation.check(
            poison_texture_path is not None and poison_texture_path.is_file(),
            f"Missing clean poison-liquid texture for variant '{variant}'.",
        )
        for owner, textures in (
            ("container", actual_container_textures),
            ("item", actual_item_textures),
        ):
            texture = textures.get(variant, {}) if isinstance(textures, dict) else {}
            validation.check(
                isinstance(texture, dict)
                and texture.get("base") == expected["base"],
                (
                    f"Poison {owner} variant '{variant}' must use its clean "
                    "poison-fluid texture instead of ingredient artwork."
                ),
            )

    danger = config.get("Danger", {})
    validation.check(
        isinstance(danger, dict)
        and danger.get("MaximumTier") == 10
        and isinstance(danger.get("Palette"), list)
        and len(danger.get("Palette", [])) == 11
        and all(
            isinstance(color, str)
            and re.fullmatch(r"#[0-9a-fA-F]{6}", color) is not None
            for color in danger.get("Palette", [])
        ),
        "Danger config must define tiers 0-10 and exactly eleven heatmap colors.",
    )

    if isinstance(artifacts, list):
        validation.check(
            {value.get("Code") for value in artifacts if isinstance(value, dict)} <= crafted_outputs,
            "Every Grandmaster artifact must have a recipe output.",
        )


def validate_pngs(validation: Validation) -> None:
    for path in sorted(ASSET_ROOT.rglob("*.png")):
        try:
            data = path.read_bytes()
            validation.png_count += 1
            validation.check(
                data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) >= 24,
                f"{relative(path)} is not a valid PNG file.",
            )
            if len(data) >= 24:
                width, height = struct.unpack(">II", data[16:24])
                validation.check(
                    width > 0 and height > 0,
                    f"{relative(path)} has invalid dimensions {width}x{height}.",
                )
        except OSError as error:
            validation.errors.append(f"{relative(path)}: {error}")


def total_exp_for_level(level: int) -> int:
    if level <= 1:
        return 0
    if level <= 10:
        return 5 * level * (level - 1)
    return int(2.5 * level * level + 42.5 * level - 225)


def validate_level_table(validation: Validation) -> None:
    path = PROJECT_ROOT / "src" / "experience" / "ExperienceMath.cs"
    try:
        source = path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(f"{relative(path)}: {error}")
        return

    validation.check(
        "private const int FirstReducedGrowthLevel = 10;" in source
        and "private const double LevelTenCost = 95;" in source
        and "private const double LateLevelIncrease = 5;" in source
        and "return 5d *" in source
        and "return 2.5d * level * level +" in source,
        "ExperienceMath.cs no longer contains the approved progression curve.",
    )

    previous_total = -1
    for level in range(1, 102):
        total = total_exp_for_level(level)
        validation.check(
            total > previous_total,
            f"Level {level} XP threshold is not strictly increasing.",
        )
        previous_total = total

    for level in range(1, 101):
        total = total_exp_for_level(level)
        next_total = total_exp_for_level(level + 1)
        next_cost = next_total - total
        expected_cost = level * 10 if level < 10 else 95 + (level - 10) * 5
        validation.check(
            next_cost == expected_cost,
            f"Level {level} next-level cost is {next_cost}, expected {expected_cost}.",
        )


def validate_repository_layout(validation: Validation) -> None:
    game_domain = PROJECT_ROOT / "assets" / "game"
    additive_roots = (
        game_domain / "worldgen" / "deposits",
        game_domain / "textures" / "block" / "metal",
    )
    distributed_game_files = [
        path for path in game_domain.rglob("*")
        if path.is_file()
        and not any(path.is_relative_to(root) for root in additive_roots)
    ]
    validation.check(
        not distributed_game_files,
        (
            "Do not distribute full assets in the game domain: "
            f"{[relative(path) for path in distributed_game_files]}."
        ),
    )

    project_path = PROJECT_ROOT / "Apprentice.csproj"
    package_source_path = REPOSITORY_ROOT / "CakeBuild" / "Program.cs"
    try:
        project_source = project_path.read_text(encoding="utf-8-sig")
        package_source = package_source_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(str(error))
        project_source = ""
        package_source = ""

    validation.check(
        'Name="CleanStaleDebugAssets"' in project_source
        and 'BeforeTargets="PrepareForBuild"' in project_source
        and 'BeforeTargets="CopyFilesToOutputDirectory"' not in project_source,
        (
            "Debug asset cleanup must run before PrepareForBuild; running it "
            "at CopyFilesToOutputDirectory can delete assets after MSBuild "
            "has copied them."
        ),
    )

    required_loose_mod_sentinels = (
        "config\\class.json",
        "config\\content-2.7.json",
        "itemtypes\\2.7\\compositebow.json",
        "itemtypes\\2.7\\towershield.json",
        "blocktypes\\2.7\\advancedtrap.json",
        "entities\\2.7\\atlatl.json",
        "composite-bow-charge5.json",
        "compositebow-material.png",
        "compositebow-grip-wrap.png",
    )
    validation.check(
        'Name="VerifyLooseModAssets"' in project_source
        and 'AfterTargets="Build"' in project_source
        and "SourceLooseModAssetCount" in project_source
        and "BuiltLooseModAssetCount" in project_source
        and all(value in project_source for value in required_loose_mod_sentinels),
        (
            "The project must fail a loose Visual Studio build when required "
            "progression, trap, shield, spear or bow assets are absent."
        ),
    )
    validation.check(
        "sourceAssetCount" in package_source
        and "stagedAssetCount" in package_source
        and all(value.replace("\\", "/") in package_source
            for value in required_loose_mod_sentinels),
        (
            "The Cake package gate must require the same representative "
            "runtime assets as the loose-mod build gate."
        ),
    )

    class_patch_path = ASSET_ROOT / "patches" / (
        "replace-vanilla-characterclasses.json"
    )
    validation.check(
        not class_patch_path.exists(),
        (
            "Apprentice must not disable or replace vanilla classes. Filter "
            "them only from the Race selection dialog."
        ),
    )

    death_adapter_path = (
        PROJECT_ROOT / "src" / "interaction" /
        "EntityDeathInteractionAdapter.cs"
    )
    experience_manager_path = (
        PROJECT_ROOT / "src" / "experience" / "ExperienceManager.cs"
    )
    cementation_path = PROJECT_ROOT / "src" / "CementationFurnace.cs"
    race_system_path = PROJECT_ROOT / "src" / "RaceAppearanceSystem.cs"
    race_dialog_path = PROJECT_ROOT / "src" / "RaceAppearanceSystem.Dialog.cs"
    try:
        death_adapter_source = death_adapter_path.read_text(encoding="utf-8-sig")
        experience_manager_source = experience_manager_path.read_text(encoding="utf-8-sig")
        cementation_source = cementation_path.read_text(encoding="utf-8-sig")
        race_system_source = race_system_path.read_text(encoding="utf-8-sig")
        race_dialog_source = race_dialog_path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as error:
        validation.errors.append(str(error))
        death_adapter_source = ""
        experience_manager_source = ""
        cementation_source = ""
        race_system_source = ""
        race_dialog_source = ""
    validation.check(
        "LoseCurrentLevelProgress(deadPlayer)" in death_adapter_source
        and "ExpMath.GetLevelStartExp(level)" in experience_manager_source
        and "IsPenalty = true" in experience_manager_source,
        (
            "Player death must floor current-level profession XP and send an "
            "immediate penalty refresh without changing completed levels."
        ),
    )
    validation.check(
        'SkillTreeRuntime.HasCapstone(player, "blacksmith")' in cementation_source
        and "masterforgeplans" not in cementation_source.lower(),
        (
            "Cementation authorization must come from Blacksmith Grandmaster "
            "state, never from a forge-plan inventory token."
        ),
    )
    validation.check(
        "ApprovedCharacterDialogClosures" in race_system_source
        and "NativeFinalCharacterConfirmCallbacks" in race_system_source
        and "PatchDialogMethod(\n                typeof(GuiDialog)," in race_system_source
        and "BeforeCharacterDialogTryClose" in race_dialog_source
        and "ref ActionConsumable __2" in race_dialog_source
        and "__2 = () => OnCharacterSkinConfirmClicked(dialog)" in race_dialog_source
        and "NativeFinalCharacterConfirmCallbacks[dialog] = __2" in race_dialog_source
        and "SetDialogTab(dialog, 1);" in race_dialog_source
        and "nativeConfirm();" in race_dialog_source
        and "CompletePendingSkinConfirmation(dialog, requestId);" in race_dialog_source
        and "RegisterCallback(" not in race_dialog_source[
            race_dialog_source.find("private static void BeginSkinConfirmation"):
            race_dialog_source.find("private static void OnRaceSaveResult")
        ]
        and "onConfirm.Invoke(dialog, null)" in race_dialog_source
        and "Removed {0} stale character dialog" not in race_dialog_source,
        (
            "Character creation must replace the native next-tab Skin callback, "
            "retain the actual final character-confirm callback, queue persistence "
            "before native completion without "
            "deadlocking on the paused single-player server, "
            "and patch the implemented GuiDialog.TryClose declaration so "
            "every close route remains blocked until Race and Skin are confirmed."
        ),
    )
    validation.check(
        "FilterCharacterDialogRaceClasses(dialog, state);" in race_dialog_source
        and "RestoreCharacterDialogClassList(state);" in race_dialog_source
        and "RaceByClass.ContainsKey(code)" in race_dialog_source
        and 'AccessTools.Field(characterSystem.GetType(), "characterClasses")'
            in race_dialog_source
        and "classesField.SetValue(characterSystem, raceClasses);"
            in race_dialog_source
        and "state.CharacterClassesField.SetValue(" in race_dialog_source,
        (
            "The Race dialog must temporarily show only Apprentice races and "
            "restore Vintage Story's complete class list when it closes."
        ),
    )


def validate_installed_game_class_roster(validation: Validation) -> bool:
    game_install = os.environ.get("VINTAGE_STORY", "").strip()
    if not game_install:
        return False

    game_root = Path(game_install)
    candidates = (
        game_root / "assets" / "game" / "config" / "characterclasses.json",
        game_root / "Assets" / "game" / "config" / "characterclasses.json",
    )
    class_path = next((path for path in candidates if path.is_file()), None)
    if class_path is None:
        validation.errors.append(
            "VINTAGE_STORY is set, but the vanilla characterclasses.json "
            "could not be found below that installation."
        )
        return True

    classes = validation.load_json(class_path)
    expected_codes = (
        "commoner",
        "hunter",
        "malefactor",
        "clockmaker",
        "blackguard",
        "tailor",
    )
    actual_codes = tuple(
        entry.get("code") if isinstance(entry, dict) else None
        for entry in classes[: len(expected_codes)]
    ) if isinstance(classes, list) else ()

    validation.check(
        actual_codes == expected_codes,
        (
            "The installed Vintage Story class roster differs from the "
            f"expected vanilla entries. Expected {expected_codes}, found "
            f"{actual_codes}; update the Race dialog filter compatibility."
        ),
    )
    return True


def main() -> int:
    validation = Validation()
    validate_all_json(validation)
    validate_modinfo(validation)
    validate_progression_config(validation)
    validate_shapes_and_textures(validation)
    validate_language_keys(validation)
    validate_content_27(validation)
    validate_pngs(validation)
    validate_level_table(validation)
    validate_repository_layout(validation)
    checked_game_install = validate_installed_game_class_roster(validation)

    if validation.errors:
        print(f"Validation failed with {len(validation.errors)} error(s):", file=sys.stderr)
        for error in validation.errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print(
        "Validation passed: "
        f"{validation.json_count} JSON reads, "
        f"{validation.png_count} PNG files, progression references, "
        "skinpart assets, language keys, metadata, and level table; "
        + (
            "installed game class roster checked."
            if checked_game_install
            else "installed game class roster skipped (VINTAGE_STORY not set)."
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
