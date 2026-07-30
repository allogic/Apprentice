#!/usr/bin/env python3

import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCHEMATIC_ROOT = (
    ROOT
    / "assets"
    / "apprenticehighlands"
    / "worldgen"
    / "schematics"
    / "apprentice-highlands"
)
CONFIG_ROOT = ROOT / "assets" / "apprenticehighlands" / "worldgen"

# Level 7 architecture deliberately keeps its own dark palette. The former
# granite placeholder/local-rock remap turned pale rock regions into white
# debug-looking cities. Only foundations visually merge with the surrounding
# cursed terrain now; the civilization itself stays recognisably basaltic.
BRICK = "game:stonebricks-basalt"
AGED = "game:agedstonebricks-basalt"
CRACKED = "game:crackedstonebricks-basalt"
COBBLE = "game:cobblestone-basalt"
POLISHED = "game:rockpolished-basalt"
OBSIDIAN = "game:rock-obsidian"
METAL = "game:metalblock-corroded-riveted-iron"
LIGHT = "apprenticehighlands:riftlight"
EMBER_LIGHT = "apprenticehighlands:emberlight"
LOOT_MARKER = "apprenticehighlands:lootmarker"
WRAITHWOOD = "apprenticehighlands:wraithwood"
LAVA = "apprenticehighlands:coolingmagma-still-7"
TOXIC_WATER = "apprenticemire:toxicwater-still-7"

CULTURES = (
    ("crownless", "Crownless Citadel", "fortress"),
    ("basilica", "Ashen Basilica", "cathedral"),
    ("aqueduct", "Broken Aqueduct", "waterworks"),
    ("forum", "Obsidian Forum", "civic"),
    ("foundry", "Sunken Foundry", "industrial"),
    ("necropolis", "Silent Necropolis", "funerary"),
)


def stable_hash(*values):
    value = 0x9E3779B97F4A7C15
    for item in values:
        value ^= item & 0xFFFFFFFFFFFFFFFF
        value = (value * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
        value ^= value >> 29
    value ^= value >> 30
    value = (value * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
    value ^= value >> 27
    value = (value * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
    return value ^ (value >> 31)


class Model:
    def __init__(self, size_x, size_y, size_z):
        self.size_x = size_x
        self.size_y = size_y
        self.size_z = size_z
        self.blocks = {}

    def set(self, x, y, z, block):
        if (
            0 <= x < self.size_x
            and 0 <= y < self.size_y
            and 0 <= z < self.size_z
        ):
            self.blocks[(x, y, z)] = block

    def remove(self, x, y, z):
        self.blocks.pop((x, y, z), None)

    def fill(self, x1, y1, z1, x2, y2, z2, block):
        for y in range(y1, y2 + 1):
            for z in range(z1, z2 + 1):
                for x in range(x1, x2 + 1):
                    self.set(x, y, z, block)

    def clear(self, x1, y1, z1, x2, y2, z2):
        for y in range(y1, y2 + 1):
            for z in range(z1, z2 + 1):
                for x in range(x1, x2 + 1):
                    self.remove(x, y, z)

    def floor(self, x1, z1, x2, z2, y=2, block=COBBLE, depth=3):
        self.fill(x1, max(0, y - depth + 1), z1, x2, y, z2, block)

    def pillar(self, x, z, y1, y2, block=BRICK, width=1):
        self.fill(
            x,
            y1,
            z,
            min(self.size_x - 1, x + width - 1),
            y2,
            min(self.size_z - 1, z + width - 1),
            block,
        )

    def wall_box(
        self,
        x1,
        y1,
        z1,
        x2,
        y2,
        z2,
        block=BRICK,
        floor_block=COBBLE,
        foundation_depth=3,
    ):
        self.floor(
            x1,
            z1,
            x2,
            z2,
            y1,
            floor_block,
            foundation_depth,
        )
        for y in range(y1 + 1, y2 + 1):
            for x in range(x1, x2 + 1):
                self.set(x, y, z1, block)
                self.set(x, y, z2, block)
            for z in range(z1 + 1, z2):
                self.set(x1, y, z, block)
                self.set(x2, y, z, block)

    def arch_x(self, center_x, y, z, half_width, height, block=BRICK):
        left = center_x - half_width
        right = center_x + half_width
        for offset_y in range(height):
            width = 2 if offset_y < 3 else 1
            self.fill(
                left,
                y + offset_y,
                z,
                left + width - 1,
                y + offset_y,
                z,
                block,
            )
            self.fill(
                right - width + 1,
                y + offset_y,
                z,
                right,
                y + offset_y,
                z,
                block,
            )
        for x in range(left, right + 1):
            self.set(x, y + height, z, block)
            if x in (left, right) or abs(x - center_x) > half_width - 2:
                self.set(x, y + height + 1, z, block)

    def arch_z(self, x, y, center_z, half_width, height, block=BRICK):
        north = center_z - half_width
        south = center_z + half_width
        for offset_y in range(height):
            width = 2 if offset_y < 3 else 1
            self.fill(
                x,
                y + offset_y,
                north,
                x,
                y + offset_y,
                north + width - 1,
                block,
            )
            self.fill(
                x,
                y + offset_y,
                south - width + 1,
                x,
                y + offset_y,
                south,
                block,
            )
        for z in range(north, south + 1):
            self.set(x, y + height, z, block)
            if z in (north, south) or abs(z - center_z) > half_width - 2:
                self.set(x, y + height + 1, z, block)

    def doorway_north(self, x1, z, width=3, bottom=3, height=6):
        self.clear(x1, bottom, z, x1 + width - 1, bottom + height - 1, z)

    def doorway_south(self, x1, z, width=3, bottom=3, height=6):
        self.clear(x1, bottom, z, x1 + width - 1, bottom + height - 1, z)

    def doorway_west(self, x, z1, width=3, bottom=3, height=6):
        self.clear(x, bottom, z1, x, bottom + height - 1, z1 + width - 1)

    def doorway_east(self, x, z1, width=3, bottom=3, height=6):
        self.clear(x, bottom, z1, x, bottom + height - 1, z1 + width - 1)

    def battlements(self, x1, y, z1, x2, z2, block=CRACKED):
        for x in range(x1, x2 + 1, 3):
            self.set(x, y, z1, block)
            self.set(x, y, z2, block)
        for z in range(z1, z2 + 1, 3):
            self.set(x1, y, z, block)
            self.set(x2, y, z, block)

    def ruined_building(
        self,
        x1,
        z1,
        width,
        depth,
        height,
        seed,
        wall=BRICK,
        accent=AGED,
    ):
        x2 = x1 + width - 1
        z2 = z1 + depth - 1
        top = min(self.size_y - 2, height)
        self.wall_box(x1, 2, z1, x2, top, z2, wall, COBBLE, 3)

        door_x = x1 + width // 2 - 1
        self.doorway_north(door_x, z1, 3, 3, min(6, top - 3))
        for x in range(x1 + 3, x2 - 1, 5):
            for y in range(7, top - 2, 5):
                self.clear(x, y, z1, min(x + 1, x2), min(y + 2, top), z1)
                self.clear(x, y, z2, min(x + 1, x2), min(y + 2, top), z2)
        for z in range(z1 + 3, z2 - 1, 5):
            for y in range(7, top - 2, 5):
                self.clear(x1, y, z, x1, min(y + 2, top), min(z + 1, z2))
                self.clear(x2, y, z, x2, min(y + 2, top), min(z + 1, z2))

        for x, z in (
            (x1 + 1, z1 + 1),
            (x2 - 1, z1 + 1),
            (x1 + 1, z2 - 1),
            (x2 - 1, z2 - 1),
        ):
            self.pillar(x, z, 3, top + 1, accent)
        self.battlements(x1, top + 1, z1, x2, z2, accent)

        # Partial upper floors, beams and roof remnants make the shell read as
        # a former building instead of four unrelated rows of pillars.
        for floor_y in range(8, top - 3, 6):
            if floor_y % 12 == 8:
                self.fill(
                    x1 + 1,
                    floor_y,
                    z1 + 1,
                    x2 - 2,
                    floor_y,
                    z1 + max(2, depth // 3),
                    accent,
                )
            else:
                self.fill(
                    x1 + 1,
                    floor_y,
                    z2 - max(2, depth // 3),
                    x2 - 2,
                    floor_y,
                    z2 - 1,
                    accent,
                )
        roof_side = stable_hash(seed, 91) % 4
        if roof_side in (0, 1):
            roof_z = z1 + 1 if roof_side == 0 else z2 - 2
            self.fill(x1 + 2, top, roof_z, x2 - 2, top + 1, roof_z + 1, CRACKED)
        else:
            roof_x = x1 + 1 if roof_side == 2 else x2 - 2
            self.fill(roof_x, top, z1 + 2, roof_x + 1, top + 1, z2 - 2, CRACKED)

        # A collapsed corner creates an intentional ruin silhouette rather
        # than uniformly deleting random wall blocks.
        broken_corner = stable_hash(seed, width, depth) % 4
        corner_x = x1 if broken_corner in (0, 2) else x2
        corner_z = z1 if broken_corner in (0, 1) else z2
        for dy in range(0, min(7, top - 4)):
            radius = max(0, 3 - dy // 2)
            self.clear(
                corner_x - radius,
                top - dy,
                corner_z - radius,
                corner_x + radius,
                top + 1,
                corner_z + radius,
            )

    def rubble(self, center_x, center_z, radius, seed):
        for dz in range(-radius, radius + 1):
            for dx in range(-radius, radius + 1):
                distance = abs(dx) + abs(dz)
                if distance > radius + 1:
                    continue
                roll = stable_hash(seed, dx, dz) % 100
                if roll > 68:
                    continue
                height = max(1, (radius - distance // 2) // 2)
                material = CRACKED if roll % 3 else OBSIDIAN
                self.fill(
                    center_x + dx,
                    0,
                    center_z + dz,
                    center_x + dx,
                    3 + height,
                    center_z + dz,
                    material,
                )

    def evil_lantern(self, x, z, seed, base_y=3):
        light = LIGHT if stable_hash(seed, x, z) & 1 else EMBER_LIGHT
        self.set(x, base_y, z, OBSIDIAN)
        self.set(x, base_y + 1, z, METAL)
        self.set(x, base_y + 2, z, light)

    def wall_light(self, x, y, z, seed):
        light = LIGHT if stable_hash(seed, x, y, z) & 1 else EMBER_LIGHT
        self.set(x, y, z, light)

    def magma_pool(self, x1, z1, x2, z2, y=3, seed=0):
        if x2 - x1 < 4 or z2 - z1 < 4:
            return
        for z in range(z1, z2 + 1):
            for x in range(x1, x2 + 1):
                border = x in (x1, x2) or z in (z1, z2)
                if border:
                    shore = (
                        OBSIDIAN
                        if stable_hash(seed, x, z) % 4 == 0
                        else CRACKED
                    )
                    self.set(x, y, z, shore)
                    continue
                self.set(
                    x,
                    y - 1,
                    z,
                    OBSIDIAN,
                )
                self.set(x, y, z, LAVA)

        for x, z in (
            (x1, z1),
            (x2, z1),
            (x1, z2),
            (x2, z2),
        ):
            self.set(
                x,
                y + 1,
                z,
                EMBER_LIGHT,
            )

    def poison_fountain(self, center_x, center_z, y=3, seed=0):
        """Build an unmistakable, open-air toxic fountain court.

        Every landmark template contains one complete court so any selected
        center variant is valid. Runtime keeps poison only in the primary city
        landmark and converts all secondary courts to their planned liquid.
        """
        radius = 4
        self.clear(
            center_x - radius,
            y,
            center_z - radius,
            center_x + radius,
            self.size_y - 1,
            center_z + radius,
        )
        self.floor(
            center_x - radius,
            center_z - radius,
            center_x + radius,
            center_z + radius,
            y - 1,
            CRACKED,
            3,
        )
        for z in range(center_z - radius, center_z + radius + 1):
            for x in range(center_x - radius, center_x + radius + 1):
                distance = max(abs(x - center_x), abs(z - center_z))
                if distance == radius:
                    self.set(
                        x,
                        y,
                        z,
                        CRACKED
                        if stable_hash(seed, x, z) % 3
                        else OBSIDIAN,
                    )
                elif distance >= 2:
                    self.set(x, y - 1, z, OBSIDIAN)
                    self.set(x, y, z, TOXIC_WATER)

        self.fill(
            center_x - 1,
            y,
            center_z - 1,
            center_x + 1,
            y + 2,
            center_z + 1,
            OBSIDIAN,
        )
        self.set(center_x, y + 3, center_z, LIGHT)
        self.set(center_x, y + 4, center_z, EMBER_LIGHT)
        self.set(center_x, y + 5, center_z, EMBER_LIGHT)
        for x, z in (
            (center_x - radius, center_z - radius),
            (center_x + radius, center_z - radius),
            (center_x - radius, center_z + radius),
            (center_x + radius, center_z + radius),
        ):
            self.evil_lantern(x, z, seed, y + 1)

    def dead_tree(self, x, z, height, seed):
        direction_x = -1 if stable_hash(seed, 1) & 1 else 1
        direction_z = -1 if stable_hash(seed, 2) & 1 else 1
        trunk_x = x
        trunk_z = z
        self.fill(x, 0, z, x, 2, z, WRAITHWOOD)
        for y in range(3, min(self.size_y - 2, 3 + height)):
            if y in (7, 12):
                trunk_x += direction_x
            if y in (9, 14):
                trunk_z += direction_z
            self.set(trunk_x, y, trunk_z, WRAITHWOOD)
            if y in (8, 11, 14):
                length = 2 + int(stable_hash(seed, y) % 3)
                for offset in range(1, length + 1):
                    self.set(
                        trunk_x + direction_z * offset,
                        y + offset // 2,
                        trunk_z - direction_x * offset,
                        WRAITHWOOD,
                    )

    def age(self, seed, intensity=12, protect_y=3):
        protected = {
            OBSIDIAN,
            METAL,
            LIGHT,
            EMBER_LIGHT,
            POLISHED,
            WRAITHWOOD,
            LOOT_MARKER,
            LAVA,
            TOXIC_WATER,
        }
        for (x, y, z), block in list(self.blocks.items()):
            if y <= protect_y or block in protected:
                continue
            edge = (
                x in (0, self.size_x - 1)
                or z in (0, self.size_z - 1)
                or y >= self.size_y - 3
            )
            threshold = intensity + (6 if edge else 0)
            roll = stable_hash(seed, x, y, z) % 100
            if roll < threshold:
                self.remove(x, y, z)
            elif roll < threshold + 23 and block == BRICK:
                self.set(x, y, z, AGED if roll % 2 == 0 else CRACKED)

    def add_corruption(self, seed, amount=7, add_light=False):
        for (x, y, z), block in list(self.blocks.items()):
            if y <= 2 or block in (
                LIGHT,
                EMBER_LIGHT,
                METAL,
                POLISHED,
                WRAITHWOOD,
                LOOT_MARKER,
                LAVA,
                TOXIC_WATER,
            ):
                continue
            roll = stable_hash(seed, x * 3, y * 5, z * 7) % 100
            if roll < amount:
                self.set(x, y, z, OBSIDIAN)
        if add_light:
            self.set(self.size_x // 2, 4, self.size_z // 2, LIGHT)

    def remove_unsupported_components(self):
        # Aging may sever a wall top, branch or roof fragment. Vintage Story
        # keeps those disconnected voxels forever, which produced the large
        # floating debris visible in the rejected screenshots. Retain only
        # components that remain physically connected to schematic Y=0.
        supported = {
            position
            for position in self.blocks
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
                if (
                    neighbour in self.blocks
                    and neighbour not in supported
                ):
                    supported.add(neighbour)
                    frontier.append(neighbour)
        self.blocks = {
            position: block
            for position, block in self.blocks.items()
            if position in supported
        }


def fortress_tower(model, x, z, width, height, seed):
    model.ruined_building(x, z, width, width, height, seed, BRICK, OBSIDIAN)
    model.fill(x + 2, 3, z + 2, x + width - 3, 4, z + width - 3, CRACKED)
    model.battlements(x, height + 2, z, x + width - 1, z + width - 1, OBSIDIAN)


def citadel_landmark(variant):
    model = Model(31, 55, 31)
    model.floor(1, 1, 29, 29, 2, COBBLE, 3)
    fortress_tower(model, 2, 2, 8, 38 + variant, 100 + variant)
    fortress_tower(model, 21, 2, 8, 33 + variant * 2, 120 + variant)
    fortress_tower(model, 2, 21, 8, 31 + variant, 140 + variant)
    fortress_tower(model, 21, 21, 8, 41 - variant, 160 + variant)
    model.ruined_building(10, 6, 11, 19, 29 + variant, 180 + variant)
    model.doorway_north(13, 6, 5, 3, 10)
    model.doorway_south(13, 24, 5, 3, 10)
    model.arch_x(15, 3, 15, 6, 18 + variant, CRACKED)
    model.fill(10, 24 + variant, 14, 20, 26 + variant, 16, AGED)
    for x in (11, 15, 19):
        model.pillar(x, 15, 27, 50 - ((x + variant) % 4), OBSIDIAN, 2)
    model.rubble(15, 16, 5, 190 + variant)
    for x, z in ((9, 15), (21, 15), (15, 9), (15, 21)):
        model.evil_lantern(x, z, 198 + variant)
    model.dead_tree(7, 15, 16, 195 + variant)
    model.dead_tree(24, 16, 13, 197 + variant)
    model.age(200 + variant, 11 + variant)
    model.add_corruption(500 + variant, 6 + variant, True)
    return model


def basilica_landmark(variant):
    model = Model(29, 55, 31)
    model.floor(1, 1, 27, 29, 2, COBBLE, 3)
    model.wall_box(6, 2, 3, 22, 31 + variant, 27, BRICK, COBBLE, 3)
    model.doorway_north(11, 3, 7, 3, 11)
    model.doorway_south(11, 27, 7, 3, 11)
    model.wall_box(2, 2, 12, 26, 21 + variant, 21, AGED, COBBLE, 3)
    for z in range(7, 26, 6):
        model.pillar(4, z, 3, 24 + variant, POLISHED, 2)
        model.pillar(23, z, 3, 24 + variant, POLISHED, 2)
        model.fill(4, 24 + variant, z, 9, 25 + variant, z, CRACKED)
        model.fill(19, 24 + variant, z, 24, 25 + variant, z, CRACKED)
    for z in range(8, 26, 7):
        model.arch_x(14, 3, z, 5, 17, BRICK)
    for y in range(32, 52 - variant * 2):
        radius = max(1, (52 - variant * 2 - y) // 5)
        model.fill(14 - radius, y, 24, 14 + radius, y, 26, POLISHED)
    for z in (9, 16, 24):
        model.set(14, 11, z, LIGHT)
    for x, z in ((5, 5), (23, 5), (5, 26), (23, 26)):
        model.evil_lantern(x, z, 618 + variant)
    model.rubble(9, 24, 5, 620 + variant)
    model.rubble(23, 9, 4, 625 + variant)
    model.dead_tree(3, 8, 14, 630 + variant)
    model.age(210 + variant, 10 + variant * 2)
    model.add_corruption(600 + variant, 5 + variant, False)
    return model


def aqueduct_landmark(variant):
    model = Model(31, 47, 21)
    model.floor(1, 2, 29, 18, 2, COBBLE, 3)
    for center_x in (5, 15, 25):
        model.arch_x(center_x, 3, 10, 5, 25 + (center_x + variant) % 4, BRICK)
        model.pillar(center_x - 5, 9, 3, 35, AGED, 2)
        model.pillar(center_x + 4, 9, 3, 35, AGED, 2)
    model.fill(1, 33, 8, 29, 37, 12, CRACKED)
    model.clear(11 + variant, 31, 7, 19 + variant, 42, 13)
    for x in range(3, 29, 6):
        model.pillar(x, 4, 3, 17 + (x % 5), OBSIDIAN)
        model.pillar(x, 16, 3, 15 + (x % 4), OBSIDIAN)
    model.ruined_building(8, 3, 15, 15, 21 + variant, 730 + variant)
    model.magma_pool(2, 13, 9, 18, 3, 731 + variant)
    model.magma_pool(21, 2, 28, 7, 3, 732 + variant)
    model.set(3, 34, 10, LIGHT)
    model.set(27, 33, 10, LIGHT)
    for x, z in ((4, 10), (26, 10), (15, 2), (15, 18)):
        model.evil_lantern(x, z, 734 + variant)
    model.rubble(15, 10, 5, 735 + variant)
    model.dead_tree(7, 17, 12, 740 + variant)
    model.age(220 + variant, 9 + variant)
    model.add_corruption(700 + variant, 5 + variant, False)
    return model


def forum_landmark(variant):
    model = Model(31, 52, 31)
    model.floor(1, 1, 29, 29, 2, COBBLE, 3)
    center = 15
    for index in range(16):
        angle = index * math.pi * 2 / 16
        x = round(center + math.cos(angle) * 11)
        z = round(center + math.sin(angle) * 11)
        height = 15 + int(stable_hash(variant, index) % 7)
        model.pillar(x, z, 3, height, POLISHED, 2)
        if index % 2 == 0:
            model.set(x, height + 1, z, OBSIDIAN)
    for radius in (6, 9, 13):
        for step in range(0, 360, 5):
            angle = math.radians(step)
            x = round(center + math.cos(angle) * radius)
            z = round(center + math.sin(angle) * radius)
            model.set(x, 3, z, AGED if radius != 9 else CRACKED)
    model.fill(10, 3, 10, 20, 7, 20, OBSIDIAN)
    model.clear(12, 4, 12, 18, 8, 18)
    for y in range(8, 47 - variant):
        radius = 3 if y < 25 else (2 if y < 34 else 1)
        model.fill(
            center - radius,
            y,
            center - radius,
            center + radius,
            y,
            center + radius,
            POLISHED if y % 5 == 0 else OBSIDIAN,
        )
    model.set(center, 47 - variant, center, LIGHT)
    model.magma_pool(11, 23, 19, 28, 3, 821 + variant)
    for x, z in ((5, 5), (25, 5), (5, 25), (25, 25)):
        model.ruined_building(x - 3, z - 3, 7, 7, 14 + variant, x * z + variant)
        model.evil_lantern(x, z, 825 + variant)
    model.rubble(23, 15, 5, 830 + variant)
    model.dead_tree(7, 16, 15, 835 + variant)
    model.age(230 + variant, 10 + variant)
    model.add_corruption(800 + variant, 8, False)
    return model


def foundry_landmark(variant):
    model = Model(31, 55, 29)
    model.floor(1, 1, 29, 27, 2, COBBLE, 3)
    model.ruined_building(4, 5, 23, 19, 27 + variant, 910 + variant, BRICK, METAL)
    model.doorway_north(12, 5, 7, 3, 10)
    model.doorway_south(12, 23, 7, 3, 10)
    for x, z, height in (
        (6, 7, 49),
        (24, 7, 43 + variant),
        (6, 22, 40 + variant * 2),
        (24, 22, 51 - variant),
    ):
        model.fill(x - 2, 2, z - 2, x + 2, height, z + 2, METAL)
        model.fill(x - 3, height - 4, z - 3, x + 3, height, z + 3, CRACKED)
        model.clear(x - 1, 4, z - 1, x + 1, height, z + 1)
        model.set(x, height - 2, z, LIGHT)
    for x in range(9, 25, 6):
        model.pillar(x, 14, 3, 22 + (x % 5), METAL, 2)
    model.fill(8, 3, 9, 22, 8, 19, OBSIDIAN)
    model.clear(11, 4, 12, 19, 9, 16)
    model.magma_pool(11, 12, 19, 16, 4, 925 + variant)
    for x, z in ((9, 9), (21, 9), (9, 19), (21, 19)):
        model.evil_lantern(x, z, 927 + variant)
    model.rubble(15, 25, 5, 930 + variant)
    model.dead_tree(3, 24, 13, 935 + variant)
    model.age(240 + variant, 10 + variant)
    model.add_corruption(900 + variant, 8 + variant, False)
    return model


def necropolis_landmark(variant):
    model = Model(31, 53, 31)
    model.floor(1, 1, 29, 29, 2, OBSIDIAN, 3)
    model.ruined_building(10, 10, 11, 11, 29 + variant, 1010 + variant, POLISHED, OBSIDIAN)
    for side in range(4):
        if side == 0:
            model.doorway_north(12, 10, 7, 3, 9)
        elif side == 1:
            model.doorway_south(12, 20, 7, 3, 9)
        elif side == 2:
            model.doorway_west(10, 12, 7, 3, 9)
        else:
            model.doorway_east(20, 12, 7, 3, 9)
    for x, z in ((4, 4), (26, 4), (4, 26), (26, 26)):
        height = 43 - int(stable_hash(x, z, variant) % 7)
        for y in range(3, height):
            radius = 2 if y < 17 else (1 if y < 28 else 0)
            model.fill(
                x - radius,
                y,
                z - radius,
                x + radius,
                y,
                z + radius,
                POLISHED if y % 6 == 0 else OBSIDIAN,
            )
        model.set(x, height, z, LIGHT)
    for lane in range(3, 28, 6):
        for offset in range(3, 28, 7):
            model.fill(lane, 3, offset, lane + 2, 5, offset + 4, CRACKED)
            model.set(lane + 1, 6, offset + 1, AGED)
    model.arch_x(15, 3, 5, 5, 17, BRICK)
    model.arch_x(15, 3, 25, 5, 17, BRICK)
    model.magma_pool(11, 22, 19, 27, 3, 1026 + variant)
    for x, z in ((8, 8), (22, 8), (8, 22), (22, 22)):
        model.evil_lantern(x, z, 1028 + variant)
    model.rubble(15, 16, 5, 1030 + variant)
    model.dead_tree(6, 15, 17, 1035 + variant)
    model.dead_tree(25, 15, 15, 1040 + variant)
    model.age(250 + variant, 9 + variant)
    model.add_corruption(1000 + variant, 7 + variant, False)
    return model


LANDMARK_BUILDERS = {
    "crownless": citadel_landmark,
    "basilica": basilica_landmark,
    "aqueduct": aqueduct_landmark,
    "forum": forum_landmark,
    "foundry": foundry_landmark,
    "necropolis": necropolis_landmark,
}


def district_model(culture, variant):
    width = 29 + (variant % 2) * 2
    depth = 29 + ((variant + 1) % 2) * 2
    height = 28 + (variant % 2) * 4
    model = Model(width, height, depth)

    street_width = 5
    center_x = width // 2
    center_z = depth // 2
    model.floor(center_x - 3, 0, center_x + 3, depth - 1, 2, AGED, 3)
    model.floor(0, center_z - 3, width - 1, center_z + 3, 2, AGED, 3)
    model.floor(
        center_x - 5,
        center_z - 5,
        center_x + 5,
        center_z + 5,
        2,
        COBBLE,
        3,
    )
    plots = (
        (2, 2, center_x - street_width // 2 - 3, center_z - street_width // 2 - 3),
        (center_x + 3, 2, width - 3, center_z - street_width // 2 - 3),
        (2, center_z + 3, center_x - street_width // 2 - 3, depth - 3),
        (center_x + 3, center_z + 3, width - 3, depth - 3),
    )
    loot_markers = []
    for index, (x1, z1, x2, z2) in enumerate(plots):
        if x2 - x1 < 7 or z2 - z1 < 7:
            continue
        building_height = 12 + int(stable_hash(variant, index, len(culture)) % 11)
        wall = BRICK
        accent = AGED
        if culture == "basilica":
            accent = POLISHED
        elif culture == "forum":
            wall = AGED
            accent = OBSIDIAN
        elif culture == "foundry":
            accent = METAL
        elif culture == "necropolis":
            wall = POLISHED
            accent = OBSIDIAN
        model.ruined_building(
            x1,
            z1,
            x2 - x1 + 1,
            z2 - z1 + 1,
            building_height,
            2000 + variant * 31 + index,
            wall,
            accent,
        )
        # One guaranteed interior marker per room. The runtime city planner
        # turns exactly one marker into a filled chest only in the 9–13
        # selected district sectors and removes every other marker.
        marker_x = x1 + 2 + int(stable_hash(variant, index, 7) % max(1, x2 - x1 - 3))
        marker_z = z1 + 2 + int(stable_hash(variant, index, 11) % max(1, z2 - z1 - 3))
        loot_markers.append((marker_x, marker_z))
        door_x = x1 + (x2 - x1) // 2
        model.wall_light(
            door_x,
            min(building_height - 2, 6),
            z1,
            2050 + variant * 17 + index,
        )
        if index % 2 == variant % 2:
            ledge_y = min(height - 3, building_height - 2)
            model.fill(
                x1 + 1,
                ledge_y,
                z1 + 1,
                x2 - 1,
                ledge_y,
                z1 + 2,
                accent,
            )

    if culture == "crownless":
        model.arch_x(center_x, 3, center_z, 5, 10, BRICK)
        model.battlements(1, 11, 1, width - 2, depth - 2, CRACKED)
    elif culture == "basilica":
        for z in range(5, depth - 4, 7):
            model.pillar(center_x, z, 4, 19 + variant, POLISHED, 2)
    elif culture == "aqueduct":
        for x in range(7, width - 5, 10):
            model.arch_x(x, 4, center_z, 4, 10 + variant, CRACKED)
        model.magma_pool(
            center_x - 4,
            center_z - 4,
            center_x + 4,
            center_z + 4,
            3,
            2150 + variant,
        )
    elif culture == "forum":
        for x, z in (
            (center_x - 6, center_z - 6),
            (center_x + 6, center_z - 6),
            (center_x - 6, center_z + 6),
            (center_x + 6, center_z + 6),
        ):
            model.pillar(x, z, 4, 16 + variant, POLISHED)
        model.pillar(center_x, center_z, 4, 24, OBSIDIAN, 2)
    elif culture == "foundry":
        model.fill(center_x - 3, 4, center_z - 3, center_x + 3, 11, center_z + 3, METAL)
        model.pillar(center_x, center_z, 12, 25, METAL, 2)
        model.magma_pool(
            center_x - 4,
            center_z - 4,
            center_x + 4,
            center_z + 4,
            3,
            2160 + variant,
        )
    else:
        for x in range(5, width - 4, 6):
            model.fill(x, 4, center_z - 1, x + 2, 6, center_z + 1, OBSIDIAN)

    model.rubble(
        center_x + (variant % 3 - 1) * 6,
        center_z + ((variant + 1) % 3 - 1) * 6,
        5,
        2200 + variant + len(culture) * 37,
    )
    if variant % 2 == 0:
        model.dead_tree(4, depth - 5, 11 + variant, 2250 + variant)
    if variant % 3 == 0:
        model.set(center_x, 5, center_z, LIGHT)
    for x, z in (
        (center_x - 5, center_z - 5),
        (center_x + 5, center_z - 5),
        (center_x - 5, center_z + 5),
        (center_x + 5, center_z + 5),
    ):
        model.evil_lantern(x, z, 2280 + variant)
    model.age(2300 + variant + len(culture) * 41, 8 + variant)
    model.add_corruption(2400 + variant + len(culture) * 43, 5 + variant // 2)
    # Marker placement is deliberately last: culture-specific courtyards,
    # Rubble and cooling-magma courts may reshape the district, but may never
    # consume an interior chest candidate.
    for marker_x, marker_z in loot_markers:
        model.clear(marker_x, 3, marker_z, marker_x, 4, marker_z)
        model.set(marker_x, 3, marker_z, LOOT_MARKER)
    return model


def road_model(culture, variant):
    size_x = 31
    size_z = 31
    model = Model(size_x, 29, size_z)
    center_x = size_x // 2
    center_z = size_z // 2
    model.floor(0, center_z - 3, size_x - 1, center_z + 3, 2, AGED, 3)
    model.floor(center_x - 3, 0, center_x + 3, size_z - 1, 2, AGED, 3)
    model.floor(center_x - 6, center_z - 6, center_x + 6, center_z + 6, 2, COBBLE, 3)

    # Every road sector is rotation-independent. Continuous cross streets
    # remain readable even when the native schematic pool chooses a different
    # variant, while culture-specific ruins stay low enough to preserve the
    # view toward the monumental city core.
    if culture == "crownless":
        model.floor(2, center_z - 6, 10, center_z - 5, 2, COBBLE, 3)
        model.floor(20, center_z + 5, 28, center_z + 6, 2, COBBLE, 3)
        model.fill(2, 3, center_z - 6, 10, 7 + variant, center_z - 5, BRICK)
        model.fill(20, 3, center_z + 5, 28, 7 + variant, center_z + 6, BRICK)
        model.clear(5, 3, center_z - 6, 8, 6, center_z - 5)
        model.battlements(2, 8 + variant, center_z - 6, 10, center_z - 5, CRACKED)
    elif culture == "basilica":
        model.floor(center_x - 6, center_z - 8, center_x + 6, center_z - 6, 2, COBBLE, 3)
        model.floor(4, 22, 11, 25, 2, COBBLE, 3)
        model.arch_x(center_x, 3, center_z - 7, 5, 10 + variant, POLISHED)
        model.fill(4, 3, 22, 11, 6, 25, AGED)
        model.clear(6, 3, 22, 9, 5, 25)
    elif culture == "aqueduct":
        model.floor(0, center_z - 1, size_x - 1, center_z + 1, 1, OBSIDIAN, 2)
        model.floor(center_x - 7, center_z + 6, center_x + 7, center_z + 8, 2, COBBLE, 3)
        model.arch_x(center_x, 3, center_z + 7, 6, 11 + variant, CRACKED)
    elif culture == "forum":
        model.floor(4, 4, 11, 10, 2, COBBLE, 3)
        model.floor(20, 20, 27, 26, 2, COBBLE, 3)
        model.fill(4, 3, 4, 11, 5, 10, AGED)
        model.fill(20, 3, 20, 27, 5, 26, AGED)
        for x, z in ((5, 5), (10, 5), (21, 25), (26, 25)):
            model.pillar(x, z, 6, 10 + variant, POLISHED)
    elif culture == "foundry":
        model.floor(3, 21, 11, 27, 2, COBBLE, 3)
        model.floor(22, 4, 25, 7, 2, COBBLE, 3)
        model.fill(3, 3, 21, 11, 7, 27, METAL)
        model.clear(5, 4, 22, 9, 7, 26)
        model.fill(22, 3, 4, 25, 11 + variant, 7, METAL)
        model.clear(23, 4, 5, 24, 10 + variant, 6)
    else:
        for x, z in ((4, 5), (22, 4), (5, 22), (23, 23)):
            model.floor(x, z, x + 3, z + 5, 2, COBBLE, 3)
            model.fill(x, 3, z, x + 3, 5, z + 5, OBSIDIAN)
            model.set(x + 1, 6, z + 2, AGED)
    if variant % 2 == 1:
        model.rubble(center_x + 7, center_z - 7, 4, 3000 + variant + len(culture) * 41)
    if variant in (0, 3):
        model.set(center_x + 5, 4, center_z + 5, LIGHT)
    for x, z in (
        (center_x - 6, center_z - 6),
        (center_x + 6, center_z - 6),
        (center_x - 6, center_z + 6),
        (center_x + 6, center_z + 6),
    ):
        model.evil_lantern(x, z, 3050 + variant + len(culture))
    model.age(3100 + variant + len(culture) * 43, 5 + variant)
    return model


def remnant_model(culture, variant):
    size = 25 + (variant % 2) * 4
    model = Model(size, 36, size)
    center = size // 2
    if culture == "crownless":
        model.floor(1, center - 5, size - 2, center + 5, 2, COBBLE, 3)
        model.fill(2, 3, center - 1, size - 3, 17 + variant, center + 1, BRICK)
        model.clear(center - 3, 3, center - 1, center + 3, 11, center + 1)
        model.ruined_building(2, center - 5, 8, 11, 24 + variant, 4010 + variant)
        model.ruined_building(size - 10, center - 5, 8, 11, 21 + variant, 4020 + variant)
        model.battlements(2, 18 + variant, center - 1, size - 3, center + 1, CRACKED)
    elif culture == "basilica":
        model.ruined_building(5, 2, size - 10, size - 4, 25 + variant, 4030 + variant, BRICK, POLISHED)
        model.arch_x(center, 3, 5, 5, 13 + variant, POLISHED)
        model.fill(center - 4, 17, size - 6, center + 4, 19, size - 4, CRACKED)
    elif culture == "aqueduct":
        model.floor(1, center - 4, size - 2, center + 4, 2, COBBLE, 3)
        for arch_center in (6, center, size - 7):
            model.arch_x(arch_center, 3, center, 4, 17 + variant, BRICK)
        model.fill(1, 20 + variant, center - 2, size - 2, 23 + variant, center + 2, CRACKED)
        model.clear(center - 3, 18, center - 3, center + 4, 28, center + 3)
    elif culture == "forum":
        model.floor(2, 3, size - 3, size - 4, 2, COBBLE, 3)
        model.fill(2, 3, size - 6, size - 3, 14 + variant, size - 4, AGED)
        for x in range(4, size - 3, 4):
            model.pillar(x, 6, 3, 16 + variant, POLISHED, 2)
        model.pillar(center, center + 3, 3, 25, OBSIDIAN, 3)
    elif culture == "foundry":
        model.floor(2, 2, size - 3, size - 3, 2, COBBLE, 3)
        model.fill(4, 3, 4, size - 5, 15 + variant, size - 5, METAL)
        model.clear(7, 4, 7, size - 8, 16 + variant, size - 8)
        model.fill(center - 3, 14, center - 3, center + 3, 31 - variant, center + 3, METAL)
        model.clear(center - 1, 15, center - 1, center + 1, 31 - variant, center + 1)
    else:
        model.ruined_building(4, 4, size - 8, size - 8, 23 + variant, 4070 + variant, POLISHED, OBSIDIAN)
        model.clear(center - 3, 3, 4, center + 3, 12, 5)
        for x, z in ((2, 2), (size - 6, 2), (2, size - 7), (size - 6, size - 7)):
            model.floor(x, z, x + 3, z + 5, 2, COBBLE, 3)
            model.fill(x, 2, z, x + 3, 6, z + 5, OBSIDIAN)
            model.set(x + 1, 7, z + 2, AGED)
    if variant % 2 == 0:
        model.dead_tree(3, size - 4, 10 + variant, 4000 + variant)
    else:
        model.rubble(center + 4, center - 5, 5, 4090 + variant)
    if variant == 5:
        model.set(center, 8, center, LIGHT)
    if variant in (1, 4):
        model.evil_lantern(center - 4, center, 4095 + variant)
        model.evil_lantern(center + 4, center, 4097 + variant)
    model.age(4100 + variant + len(culture) * 47, 8 + variant)
    model.add_corruption(4200 + variant + len(culture) * 53, 6 + variant // 2)
    # Even the most eroded outskirts retain one grounded beacon. This keeps
    # the night skyline readable and gives the player a route cue between the
    # dense city core and the remnant ring.
    model.fill(center, 0, center, center, 3, center, OBSIDIAN)
    model.set(center, 4, center, METAL)
    model.set(
        center,
        5,
        center,
        LIGHT if variant & 1 else EMBER_LIGHT,
    )
    return model


def schematic_json(model):
    model.remove_unsupported_components()
    codes = sorted(set(model.blocks.values()))
    code_ids = {code: index + 1 for index, code in enumerate(codes)}
    entries = sorted(
        model.blocks.items(),
        key=lambda entry: (
            entry[0][1],
            entry[0][2],
            entry[0][0],
        ),
    )
    return {
        "GameVersion": "1.22.3",
        "SizeX": model.size_x,
        "SizeY": model.size_y,
        "SizeZ": model.size_z,
        "BlockCodes": {str(code_ids[code]): code for code in codes},
        "ItemCodes": {},
        "Indices": [x | (z << 10) | (y << 20) for (x, y, z), _ in entries],
        "BlockIds": [code_ids[block] for _, block in entries],
        "DecorIndices": [],
        "DecorIds": [],
        "BlockEntities": {},
        "Entities": [],
        "ReplaceMode": 2,
        "EntranceRotation": -1,
    }


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(data, separators=(",", ":")),
        encoding="utf-8",
    )


def build_assets():
    offsets = {}
    for culture, _, _ in CULTURES:
        for variant in range(4):
            relative = (
                f"apprentice-highlands/{culture}/landmarks/"
                f"landmark-{variant + 1}"
            )
            landmark = LANDMARK_BUILDERS[culture](variant)
            landmark.poison_fountain(
                landmark.size_x // 2,
                landmark.size_z - 6,
                3,
                5000 + variant + len(culture) * 67,
            )
            write_json(
                SCHEMATIC_ROOT
                / culture
                / "landmarks"
                / f"landmark-{variant + 1}.json",
                schematic_json(landmark),
            )
            offsets[relative] = -3

        for variant in range(6):
            relative = (
                f"apprentice-highlands/{culture}/districts/"
                f"district-{variant + 1}"
            )
            write_json(
                SCHEMATIC_ROOT
                / culture
                / "districts"
                / f"district-{variant + 1}.json",
                schematic_json(district_model(culture, variant)),
            )
            offsets[relative] = -3

        for variant in range(5):
            relative = (
                f"apprentice-highlands/{culture}/infrastructure/"
                f"road-{variant + 1}"
            )
            write_json(
                SCHEMATIC_ROOT
                / culture
                / "infrastructure"
                / f"road-{variant + 1}.json",
                schematic_json(road_model(culture, variant)),
            )
            offsets[relative] = -2

        for variant in range(6):
            relative = (
                f"apprentice-highlands/{culture}/remnants/"
                f"remnant-{variant + 1}"
            )
            write_json(
                SCHEMATIC_ROOT
                / culture
                / "remnants"
                / f"remnant-{variant + 1}.json",
                schematic_json(remnant_model(culture, variant)),
            )
            offsets[relative] = -3

    write_json(
        CONFIG_ROOT / "structures.json",
        {
            "chanceMultiplier": 0,
            "rocktypeRemapGroups": {"highlands": {}},
            "schematicYOffsets": offsets,
            "structures": [],
        },
    )

    # Vintage Story's LooselyGrouped placer intentionally composes each
    # group inside a compact 48-block neighbourhood. A whole city therefore
    # uses many native groups, one at each terrain-supported city-sector
    # anchor, instead of attempting to squeeze dozens of large ruins into a
    # village-sized circle. The C# coordinator owns only the deterministic
    # sector plan; every actual structure remains a native schematic group.
    village_types = []
    parts = (
        ("landmark", "landmarks", 3600),
        ("district", "districts", 0),
        ("infrastructure", "infrastructure", 0),
        ("remnant", "remnants", 0),
    )
    for culture, name, _ in CULTURES:
        prefix = f"apprentice-highlands/{culture}"
        for part, folder, min_distance in parts:
            village_types.append(
                {
                    "code": f"highlands-city-{culture}-{part}",
                    "name": f"{name} {part}",
                    "group": "apprentice-highlands-city",
                    "minGroupDistance": min_distance,
                    "chance": 1,
                    "quantityStructures": {
                        "avg": 1,
                        "var": 0,
                        "dist": "Uniform",
                    },
                    "schematics": [
                        {
                            "path": f"{prefix}/{folder}/*",
                            "weight": 1,
                            "minQuantity": 1,
                            "maxQuantity": 1,
                        },
                    ],
                    "replacewithblocklayers": [
                        "game:rock-basalt",
                        "game:crackedrock-basalt",
                        "game:gravel-basalt",
                    ],
                    "rockTypeRemapGroup": "highlands",
                    "buildProtected": False,
                }
            )
    write_json(
        CONFIG_ROOT / "cities.json",
        {
            "chanceMultiplier": 1,
            "villageTypes": village_types,
        },
    )


if __name__ == "__main__":
    build_assets()
