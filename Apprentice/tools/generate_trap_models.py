#!/usr/bin/env python3

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tools/model_sources/beartrap-c"
OUT = ROOT / "assets/apprentice/shapes/block/2.7"

STAGE_FILES = {
    1: "stage-01-1s.json",
    2: "stage-02-2s.json",
    3: "stage-03-3s.json",
    4: "stage-04-4s.json",
    5: "stage-05-5s.json",
}

STATE_STAGES = {
    "triggered": 1,
    "opening1": 1,
    "opening2": 2,
    "opening3": 3,
    "opening4": 4,
    "armed": 5,
}


def load_stage(stage):
    document = json.loads(
        (SOURCE / STAGE_FILES[stage]).read_text(encoding="utf-8")
    )
    if not isinstance(document.get("elements"), list):
        raise ValueError(f"bear trap stage {stage}: missing elements")
    return document


stages = {stage: load_stage(stage) for stage in STAGE_FILES}
fixed_prefixes = (
    "base-",
    "left-jaw-pivot",
    "right-jaw-pivot",
    "chain-",
    "anchor-chain",
    "left-actuator-",
    "left-longspring-",
    "right-fixed-hinge-",
    "pan-pivot-",
    "dog-pivot",
)

fixed_reference = {
    element["name"]: element
    for element in stages[5]["elements"]
    if element.get("name", "").startswith(fixed_prefixes)
}
for stage, document in stages.items():
    current = {
        element["name"]: element
        for element in document["elements"]
        if element.get("name", "").startswith(fixed_prefixes)
    }
    if current != fixed_reference:
        raise ValueError(
            f"bear trap stage {stage}: fixed frame, rods, or pivots moved"
        )

OUT.mkdir(parents=True, exist_ok=True)
for state, stage in STATE_STAGES.items():
    document = stages[stage]
    (OUT / f"advancedtrap-{state}.json").write_text(
        json.dumps(document, indent=2) + "\n",
        encoding="utf-8",
    )

print(f"generated {len(STATE_STAGES)} reviewed bear-trap states")
