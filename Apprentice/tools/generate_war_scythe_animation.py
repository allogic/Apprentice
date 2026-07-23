#!/usr/bin/env python3
"""Generate the continuous chest-height War Scythe animation track."""

from __future__ import annotations

import copy
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PATCH_PATH = (
    ROOT
    / "assets/apprentice/patches/2.7/war-scythe-player-animations.json"
)

ARM_ELEMENTS = (
    "UpperArmR",
    "LowerArmR",
    "ItemAnchor",
    "UpperArmL",
    "LowerArmL",
)
UPRIGHT_ELEMENTS = ("LowerTorso", "UpperTorso", "Neck", "Head")
TRANSFORM_FIELDS = (
    "offsetX",
    "offsetY",
    "offsetZ",
    "rotationX",
    "rotationY",
    "rotationZ",
)
KEY_FRAMES = (0, 1, 2, 3, 4, 7, 10, 13, 16, 19, 22, 23, 24, 25)


def normalized_elements(frame: dict) -> dict:
    elements = copy.deepcopy(frame["elements"])
    for transform in elements.values():
        for field in TRANSFORM_FIELDS:
            transform[field] = float(transform.get(field, 0.0))
    return elements


def upright_neutral(elements: dict) -> dict:
    neutral = copy.deepcopy(elements)
    for name in UPRIGHT_ELEMENTS:
        transform = neutral[name]
        for field in TRANSFORM_FIELDS:
            transform[field] = 0.0
    return neutral


def interpolate(start: dict, end: dict, amount: float) -> dict:
    output = {}
    for name in start:
        output[name] = {}
        for field in TRANSFORM_FIELDS:
            left = start[name][field]
            right = end[name][field]
            output[name][field] = round(left + (right - left) * amount, 9)
    return output


def animation_operation(document: list, code: str) -> dict:
    return next(
        operation
        for operation in document
        if operation.get("value", {}).get("code") == code
        and "keyframes" in operation.get("value", {})
    )


def metadata_operation(document: list, code: str) -> dict:
    return next(
        operation
        for operation in document
        if operation.get("value", {}).get("code") == code
        and "animation" in operation.get("value", {})
    )


document = json.loads(PATCH_PATH.read_text(encoding="utf-8"))
cut_operation = animation_operation(document, "apprenticewarscythecut")
ready_operation = animation_operation(document, "apprenticewarscytheready")
cut_metadata = metadata_operation(document, "apprenticewarscythecut")["value"]
ready_metadata = metadata_operation(document, "apprenticewarscytheready")["value"]

source_by_frame = {
    frame["frame"]: normalized_elements(frame)
    for frame in cut_operation["value"]["keyframes"]
}

# These three poses already passed the Vintage Story animator checks in .69:
# frame 10 is the left wind-up, frame 13 crosses center, and frame 16 is the
# right follow-through.  The neutral pose reuses the verified center arm/item
# geometry while removing body yaw, pitch, and roll.
left = source_by_frame[10]
center = source_by_frame[13]
right = source_by_frame[16]
neutral = upright_neutral(center)
neutral["UpperArmL"].update(
    rotationX=8.567830727,
    rotationY=-39.920982447,
    rotationZ=-75.461943320,
)
neutral["LowerArmL"].update(
    rotationX=28.363412179,
    rotationY=-23.829179570,
    rotationZ=-59.991129479,
)

generated = []
for frame in KEY_FRAMES:
    if frame <= 7:
        elements = interpolate(neutral, left, frame / 7.0)
    elif frame <= 10:
        elements = copy.deepcopy(left)
    elif frame <= 13:
        elements = interpolate(left, center, (frame - 10) / 3.0)
    elif frame <= 16:
        elements = interpolate(center, right, (frame - 13) / 3.0)
    else:
        elements = interpolate(right, neutral, (frame - 16) / 9.0)
    generated.append({"frame": frame, "elements": elements})

cut_operation["value"]["keyframes"] = generated
cut_operation["value"]["onActivityStopped"] = "EaseOut"
cut_operation["value"]["onAnimationEnd"] = "EaseOut"

# Ready and cut use the exact same neutral arm/item pose and the exact same
# blend policy.  Starting or ending an attack therefore cannot introduce a
# pose discontinuity.  The delayed vanilla scytheIdle track remains separate.
ready_operation["value"]["keyframes"] = [
    {
        "frame": 0,
        "elements": {
            name: copy.deepcopy(neutral[name]) for name in ARM_ELEMENTS
        },
    }
]

shared_weights = {
    "UpperArmR": 20,
    "LowerArmR": 20,
    "UpperArmL": 20,
    "LowerArmL": 20,
}
shared_blends = {name: "AddAverage" for name in shared_weights}
for metadata in (ready_metadata, cut_metadata):
    metadata["blendMode"] = "Add"
    metadata["elementWeight"] = copy.deepcopy(shared_weights)
    metadata["elementBlendMode"] = copy.deepcopy(shared_blends)

ready_metadata["easeOutSpeed"] = 999
cut_metadata["easeInSpeed"] = 999
cut_metadata["easeOutSpeed"] = 10

PATCH_PATH.write_text(
    json.dumps(document, indent=2, ensure_ascii=False) + "\n",
    encoding="utf-8",
)
print(f"Generated {PATCH_PATH} with {len(generated)} attack phases")
