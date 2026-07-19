#!/usr/bin/env python3
"""Validate Apprentice assets without requiring a Vintage Story install."""

from __future__ import annotations

import csv
import json
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

    shield_path = ASSET_ROOT / "itemtypes" / "2.7" / "towershield.json"
    shield = require_mapping(
        validation,
        validation.load_json(shield_path),
        shield_path,
    )
    shield_stats = shield.get("attributes", {}).get("shield", {})
    validation.check(
        shield.get("class") == "ItemShield"
        and isinstance(shield_stats, dict)
        and isinstance(shield_stats.get("protectionChance"), dict),
        (
            "Tower Shield must use the fixed ItemShield class with native "
            "shield stats; ItemShieldFromAttributes requires variant tables "
            "and throws during asset loading without them."
        ),
    )

    bow_path = ASSET_ROOT / "itemtypes" / "2.7" / "compositebow.json"
    bow = require_mapping(validation, validation.load_json(bow_path), bow_path)
    bow_shape = bow.get("shape", {})
    validation.check(
        bow.get("class") == "ItemBow"
        and bow.get("attributes", {}).get("aimAnimation") == "bowaimrecurve"
        and bow_shape.get("base") == "game:item/tool/bow/recurve"
        and bow_shape.get("alternates") == [
            {"base": "game:item/tool/bow/recurve-charge1"},
            {"base": "game:item/tool/bow/recurve-charge2"},
            {"base": "game:item/tool/bow/recurve-charge3"},
        ],
        (
            "Composite Bow must use the complete native recurve bow mesh, "
            "draw variants, and recurve aim animation."
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
    path = REPOSITORY_ROOT / "docs" / "LEVEL-TABLE-1-100.csv"
    try:
        with path.open(newline="", encoding="utf-8-sig") as stream:
            rows = list(csv.DictReader(stream))
    except (OSError, csv.Error) as error:
        validation.errors.append(f"{relative(path)}: {error}")
        return

    validation.check(len(rows) == 100, "Level table must contain levels 1 through 100.")
    for expected_level, row in enumerate(rows, start=1):
        try:
            level = int(row["Current Level"])
            total = int(row["Total XP At Level Start"])
            next_total = int(row["Total XP At Next Level"])
            next_cost = int(row["XP To Next Level"])
        except (KeyError, TypeError, ValueError) as error:
            validation.errors.append(f"{relative(path)} row {expected_level}: {error}")
            continue

        validation.check(level == expected_level, f"Level table row {expected_level} has level {level}.")
        validation.check(
            total == total_exp_for_level(level),
            f"Level {level} starts at {total}, expected {total_exp_for_level(level)}.",
        )
        validation.check(
            next_total == total_exp_for_level(level + 1),
            f"Level {level + 1} starts at {next_total}, expected {total_exp_for_level(level + 1)}.",
        )
        validation.check(
            next_cost == next_total - total,
            f"Level {level} next-level cost does not match its totals.",
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


def validate_installed_game_patch_targets(validation: Validation) -> bool:
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
            "replace-vanilla-characterclasses.json no longer targets the "
            f"expected vanilla entries. Expected {expected_codes}, found "
            f"{actual_codes}."
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
    checked_game_install = validate_installed_game_patch_targets(validation)

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
            "installed game patch targets checked."
            if checked_game_install
            else "installed game patch targets skipped (VINTAGE_STORY not set)."
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
