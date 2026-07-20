#!/usr/bin/env python3

import json
import math
import os
from pathlib import Path

os.environ.setdefault("MPLCONFIGDIR", "/tmp/apprentice-matplotlib")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.patches import Polygon, Rectangle


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Apprentice" / "assets" / "apprentice"
OUT = ROOT / "artifacts" / "engine-fixed-slot-qa.png"
ITEMS = (
    ("Composite Bow", "compositebow", "composite-bow"),
    ("Tower Shield", "towershield", "tower-shield"),
    ("Sunlance", "atlatl", "grandmaster-spear"),
)
COLORS = {
    "wood": "#86552e",
    "darkwood": "#3f271b",
    "iron": "#85888a",
    "metal": "#85888a",
    "gold": "#d8a92e",
    "copper": "#b76534",
    "leather": "#5b291d",
    "string": "#1f1815",
    "laminate": "#3d536f",
    "bone": "#ded5bc",
    "base": "#747b82",
}
FACES = (
    (0, 1, 3, 2),
    (4, 5, 7, 6),
    (0, 1, 5, 4),
    (2, 3, 7, 6),
    (0, 2, 6, 4),
    (1, 3, 7, 5),
)


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def translate(values):
    matrix = np.eye(4)
    matrix[:3, 3] = values
    return matrix


def scale(value):
    matrix = np.eye(4)
    matrix[0, 0] = value
    matrix[1, 1] = value
    matrix[2, 2] = value
    return matrix


def rotate(axis, degrees):
    angle = math.radians(degrees)
    cosine = math.cos(angle)
    sine = math.sin(angle)
    matrix = np.eye(4)
    if axis == "x":
        matrix[1:3, 1:3] = ((cosine, -sine), (sine, cosine))
    elif axis == "y":
        matrix[0, 0] = cosine
        matrix[0, 2] = sine
        matrix[2, 0] = -sine
        matrix[2, 2] = cosine
    else:
        matrix[:2, :2] = ((cosine, -sine), (sine, cosine))
    return matrix


def corners(start, end):
    return np.array([
        (x, y, z, 1.0)
        for z in (start[2], end[2])
        for y in (start[1], end[1])
        for x in (start[0], end[0])
    ])


def transformed_faces(item, shape, slot_size=66):
    transform = item["guiTransform"]
    origin_data = transform.get("origin", {})
    origin = np.array([origin_data.get(axis, 0.5) for axis in "xyz"])
    rotation = transform.get("rotation", {})
    model = translate(origin)
    model = model @ scale((slot_size / 2) * transform.get("scale", 1))
    for axis in "xyz":
        model = model @ rotate(axis, rotation.get(axis, 0))
    model = model @ translate(-origin)
    faces = []
    for element in shape.get("elements", []):
        start = np.array(element.get("from", (0, 0, 0)), dtype=float) / 16
        end = np.array(element.get("to", (0, 0, 0)), dtype=float) / 16
        element_origin = np.array(
            element.get("rotationOrigin", (0, 0, 0)), dtype=float
        ) / 16
        element_model = translate(element_origin)
        for axis in "xyz":
            element_model = element_model @ rotate(
                axis, element.get(f"rotation{axis.upper()}", 0)
            )
        element_model = element_model @ translate(-element_origin)
        points = (model @ element_model @ corners(start, end).T).T[:, :3]
        face_values = [
            face for face in (element.get("faces") or {}).values()
            if isinstance(face, dict)
        ]
        texture = next(
            (face.get("texture", "#base") for face in face_values),
            "#base",
        ).lstrip("#")
        color = COLORS.get(texture, COLORS["base"])
        for indices in FACES:
            polygon = points[list(indices)]
            faces.append((polygon[:, 2].mean(), polygon[:, :2], color))
    return sorted(faces, key=lambda entry: entry[0])


def draw_slot(axis, title, item, shape):
    axis.add_patch(Rectangle((-33, -33), 66, 66, color="#f1ddbc"))
    for _, polygon, color in transformed_faces(item, shape):
        axis.add_patch(Polygon(polygon, closed=True, facecolor=color,
                               edgecolor="#302820", linewidth=0.45))
    axis.add_patch(Rectangle((-33, -33), 66, 66, fill=False,
                             edgecolor="#241e19", linewidth=2))
    axis.set_xlim(-34, 34)
    axis.set_ylim(-34, 34)
    axis.set_aspect("equal")
    axis.axis("off")
    axis.set_title(title, color="#f5eadb", fontsize=11, pad=7)


def main():
    figure, axes = plt.subplots(1, len(ITEMS), figsize=(7.8, 3.0))
    figure.patch.set_facecolor("#392f25")
    for axis, (title, item_code, shape_code) in zip(axes, ITEMS):
        item = load(ASSETS / "itemtypes" / "2.7" / f"{item_code}.json")
        shape = load(
            ASSETS / "shapes" / "item" / "2.7" / f"{shape_code}.json"
        )
        draw_slot(axis, title, item, shape)
    figure.subplots_adjust(left=0.025, right=0.975, bottom=0.04, top=0.86,
                           wspace=0.12)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(OUT, dpi=180, facecolor=figure.get_facecolor())
    print(OUT)


if __name__ == "__main__":
    main()
