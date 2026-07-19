#!/usr/bin/env python3
"""Generate deterministic poison compatibility barrel recipes for 2.7.0.

The lists below are intentionally derived from the installed 1.22 mod assets.  We
match exact variant sets instead of broad berry/mushroom wildcards, so edible
foods can never silently become poison ingredients.
"""

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/apprentice/recipes/barrel"


def ingredient(code, quantity=None, litres=None, variants=None, kind="item", variant_name="input"):
    value = {"type": kind, "code": code}
    if "*" in code:
        value["name"] = variant_name
    if variants:
        value["allowedVariants"] = variants
    if quantity is not None:
        value["quantity"] = quantity
    if litres is not None:
        value["litres"] = litres
        value["consumeLitres"] = litres
    return value


def food(code, variants=None, kind="item"):
    return (code, variants, kind)


low = [
    food("apprentice:venomberry"),
    food("wildcraftfruit:fruit-*", ["coralbead", "snowberry", "pokeberry", "ivy", "woodbine", "falsetomato", "elderberry", "biasong", "rowanberry", "pittedchinaberry", "ginkgopulp"]),
    food("wildcraftfruit:dryfruit-*", ["juniper"]),
    food("expandedfoods:choppedmushroom-*", ["bitterbolete", "devilstooth", "golddropmilkcap"]),
    food("expandedfoods:cookedchoppedmushroom-*", ["bitterbolete-partbaked", "devilstooth-partbaked", "golddropmilkcap-partbaked", "bitterbolete-perfect", "devilstooth-perfect", "golddropmilkcap-perfect", "earthball-charred", "elfinsaddle-charred", "jackolantern-charred", "flyagaric-charred", "bitterbolete-charred", "devilstooth-charred", "golddropmilkcap-charred", "sickener-charred"]),
    food("game:mushroom-*-normal", ["bitterbolete", "devilstooth", "golddropmilkcap"], "block"),
    food("game:mushroom-*-normal-north", ["devilstooth"], "block"),
]

medium = [
    food("apprentice:gloamcap"),
    food("wildcraftfruit:fruit-*", ["cashewwhole", "blacknightshadeunripe"]),
    food("wildcraftfruit:rustyberry-*", ["fractureberry"]),
    food("expandedfoods:choppedmushroom-*", ["flyagaric", "earthball", "elfinsaddle", "jackolantern", "sickener"]),
    food("expandedfoods:cookedchoppedmushroom-*", ["flyagaric-partbaked", "earthball-partbaked", "elfinsaddle-partbaked", "jackolantern-partbaked", "devilbolete-partbaked", "laughingjim-partbaked", "sickener-partbaked", "pinkbonnet-partbaked", "flyagaric-perfect", "earthball-perfect", "elfinsaddle-perfect", "jackolantern-perfect", "devilbolete-perfect", "laughingjim-perfect", "sickener-perfect", "pinkbonnet-perfect", "devilbolete-charred", "laughingjim-charred", "foolsconecap-charred", "pinkbonnet-charred"]),
    food("game:mushroom-*-normal", ["flyagaric", "earthball", "elfinsaddle", "jackolantern", "sickener"], "block"),
]

high = [
    food("apprentice:dangerous-tissue"),
    food("wildcraftfruit:fruit-*", ["wolfberry", "belladonna", "bryony", "bitternightshade", "baneberry", "spindle", "crowseye", "seamango", "yew", "chinaberry"]),
    food("expandedfoods:choppedmushroom-*", ["deathcap", "devilbolete", "laughingjim", "foolsconecap", "funeralbell", "pinkbonnet"]),
    food("expandedfoods:cookedchoppedmushroom-*", ["deathcap-partbaked", "foolsconecap-partbaked", "funeralbell-partbaked", "deathcap-perfect", "foolsconecap-perfect", "funeralbell-perfect", "deathcap-charred", "funeralbell-charred"]),
    food("game:mushroom-*-normal", ["deathcap", "devilbolete", "laughingjim", "foolsconecap", "funeralbell", "pinkbonnet"], "block"),
    food("game:mushroom-*-normal-north", ["funeralbell", "pinkbonnet"], "block"),
]

wines = [
    "game:ciderportion-*",
    "expandedfoods:strongwineportion-*",
    "expandedfoods:potentwineportion-*",
    "wildcraftfruit:ciderportion-*",
    "wildcraftfruit:flowerwine-*",
    "wildcraftfruit:fineflowerwine-*",
]

spirits = [
    "game:alcoholportion",
    "expandedfoods:strongspiritportion-*",
    "expandedfoods:potentspiritportion-*",
    "wildcraftfruit:spiritportion-*",
    "wildcraftfruit:finespiritportion-*",
]


recipes = []


def dependencies(*codes):
    """Return recipe dependencies for every foreign asset domain used."""
    domains = {code.split(":", 1)[0] for code in codes}
    domains -= {"game", "apprentice"}
    return [{"modid": domain} for domain in sorted(domains)]


def with_dependencies(recipe, *codes):
    required = dependencies(*codes)
    if required:
        recipe["dependsOn"] = required
    return recipe


for index, (code, variants, kind) in enumerate(low):
    recipes.append(with_dependencies({
        "code": f"apprentice:poison-mild-{index}", "sealHours": 24,
        "ingredients": [ingredient("game:waterportion", litres=1), ingredient(code, quantity=4, variants=variants, kind=kind, variant_name="food")],
        "output": {"type": "item", "code": "apprentice:poisonportion-mild", "litres": 1},
    }, code))

for wine_index, wine in enumerate(wines):
    for food_index, (code, variants, kind) in enumerate(medium):
        recipes.append(with_dependencies({
            "code": f"apprentice:poison-standard-{wine_index}-{food_index}", "sealHours": 36,
            "ingredients": [ingredient(wine, litres=1, variant_name="wine"), ingredient(code, quantity=2, variants=variants, kind=kind, variant_name="food")],
            "output": {"type": "item", "code": "apprentice:poisonportion-standard", "litres": 1},
        }, wine, code))

for spirit_index, spirit in enumerate(spirits):
    for food_index, (code, variants, kind) in enumerate(high):
        recipes.append(with_dependencies({
            "code": f"apprentice:poison-potent-{spirit_index}-{food_index}", "sealHours": 48,
            "ingredients": [ingredient(spirit, litres=1, variant_name="spirit"), ingredient(code, quantity=2, variants=variants, kind=kind, variant_name="food")],
            "output": {"type": "item", "code": "apprentice:poisonportion-potent", "litres": 1},
        }, spirit, code))

for spirit_index, spirit in enumerate(spirits):
    recipes.append(with_dependencies({
        "code": f"apprentice:poison-grandmaster-{spirit_index}", "sealHours": 72,
        "ingredients": [
            ingredient(spirit, litres=1, variant_name="spirit"),
            ingredient("apprentice:dangerous-tissue", quantity=1),
            ingredient("apprentice:venomberry", quantity=5),
            ingredient("apprentice:gloamcap", quantity=5),
        ],
        "output": {"type": "item", "code": "apprentice:poisonportion-grandmaster", "litres": 1},
    }, spirit))

(OUT / "poison-brewing.json").write_text(json.dumps(recipes, indent=2) + "\n", encoding="utf-8")

arrow_recipes = []
arrow_families = ["game:arrow-*", "butchering:arrow-*"]
for tier in ("mild", "standard", "potent", "grandmaster"):
    for index, arrow_code in enumerate(arrow_families):
        arrow_recipes.append(with_dependencies({
            "code": f"apprentice:coat-arrows-{tier}-{index}",
            "ingredients": [
                ingredient(f"apprentice:poisonportion-{tier}", litres=0.1),
                ingredient(arrow_code, quantity=8, variant_name="arrow"),
            ],
            "output": {"type": "item", "code": f"apprentice:arrow-poison-{tier}", "stackSize": 8},
        }, arrow_code))

(OUT / "poison-arrows.json").write_text(json.dumps(arrow_recipes, indent=2) + "\n", encoding="utf-8")
print(f"generated {len(recipes)} brewing and {len(arrow_recipes)} coating recipes")
