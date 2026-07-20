#!/usr/bin/env python3
"""Generate deterministic core-game poison recipes for Apprentice 2.7.0.

Optional-mod recipes are deliberately excluded: barrel recipes do not honor the
asset dependency field and otherwise emit dead-recipe errors when those mods are
not installed.
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
    food("game:mushroom-*-normal", ["bitterbolete", "devilstooth", "golddropmilkcap"], "block"),
    food("game:mushroom-*-normal-north", ["devilstooth"], "block"),
]

medium = [
    food("apprentice:gloamcap"),
    food("game:mushroom-*-normal", ["flyagaric", "earthball", "elfinsaddle", "jackolantern", "sickener"], "block"),
]

high = [
    food("apprentice:dangerous-tissue"),
    food("game:mushroom-*-normal", ["deathcap", "devilbolete", "laughingjim", "foolsconecap", "funeralbell", "pinkbonnet"], "block"),
    food("game:mushroom-*-normal-north", ["funeralbell", "pinkbonnet"], "block"),
]

wines = [
    "game:ciderportion-*",
]

spirits = [
    "game:alcoholportion",
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

grandmaster_catalysts = [
    ("apprentice:dangerous-tissue", 1),
    ("apprentice:venomberry", 5),
    ("apprentice:gloamcap", 5),
]

for spirit_index, spirit in enumerate(spirits):
    for catalyst_index, (catalyst, quantity) in enumerate(grandmaster_catalysts):
        recipes.append(with_dependencies({
            "code": f"apprentice:poison-grandmaster-{spirit_index}-{catalyst_index}", "sealHours": 72,
            "ingredients": [
                ingredient(spirit, litres=1, variant_name="spirit"),
                ingredient(catalyst, quantity=quantity),
            ],
            "output": {"type": "item", "code": "apprentice:poisonportion-grandmaster", "litres": 1},
        }, spirit))

(OUT / "poison-brewing.json").write_text(json.dumps(recipes, indent=2) + "\n", encoding="utf-8")

arrow_recipes = []
arrow_families = ["game:arrow-*"]
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
