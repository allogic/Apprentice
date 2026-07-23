#!/usr/bin/env python3
"""Render a quick orthographic front silhouette for model iteration."""
import json
import math
import sys
from pathlib import Path

import matplotlib.pyplot as plt
from matplotlib.patches import Polygon

shape = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
output = Path(sys.argv[2])
colors = {
    "darksteel": "#29333a", "blade": "#43555c", "edge": "#a7bcc0",
    "bone": "#baa97b", "horn": "#4d3729", "grip": "#252629",
    "mana": "#087dcc", "brightmana": "#41e5ff",
}

fig, ax = plt.subplots(figsize=(5, 11), facecolor="#101318")
ax.set_facecolor("#101318")
for element in shape["elements"]:
    x0, y0, _ = element["from"]
    x1, y1, _ = element["to"]
    corners = [(x0,y0), (x1,y0), (x1,y1), (x0,y1)]
    if "rotationZ" in element:
        ox, oy, _ = element["rotationOrigin"]
        angle = math.radians(element["rotationZ"])
        rotated = []
        for x, y in corners:
            dx, dy = x-ox, y-oy
            rotated.append((ox + dx*math.cos(angle)-dy*math.sin(angle),
                            oy + dx*math.sin(angle)+dy*math.cos(angle)))
        corners = rotated
    texture = next(iter(element["faces"].values()))["texture"].lstrip("#")
    ax.add_patch(Polygon(corners, closed=True, facecolor=colors.get(texture, "#888"),
                         edgecolor="#0b0c0e", linewidth=.35, alpha=.96))
ax.set_xlim(-6, 22)
ax.set_ylim(-7, 54)
ax.set_aspect("equal")
ax.axis("off")
plt.tight_layout(pad=0)
fig.savefig(output, dpi=180, bbox_inches="tight", facecolor=fig.get_facecolor())
