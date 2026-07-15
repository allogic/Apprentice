# Apprentice 2.1.14b — Tabs and Hidden Class Discoveries

## Interface

- Expanded the Apprentice window to use nearly the full available game window.
- Added three top-level tabs:
  - **SKILLTREE** — the existing interactive profession trees.
  - **STATS** — lists every currently active bonus gained from purchased nodes and discovered hidden classes.
  - **HIDDEN CLASSES** — displays undiscovered classes as question marks until their capstone requirements are fulfilled.
- Added a shield behind the crossed swords in the Combat category icon.
- Replaced the Eagle Eye node icon with an actual eye.

## Hidden-class discovery system

Hidden classes are unlocked automatically when all required profession capstones are purchased. Unlocking one is a permanent discovery event and sends a server notification to the player. Locked entries reveal neither their name nor requirements.

Included discoveries:

- **Lumberjack** — Woodworker + Builder
  - 10% axe durability conservation.
  - 10% additional log and plank crafting output.
- **Weapon Master** — Warrior + Spearman + Blacksmith
  - 10% durability conservation for weapons and tools.
- **Shadow Archer** — Ranger + Hunter + Husbandry
  - Guaranteed critical arrow strike against unaware PvE targets.
- **Berserk** — Tank + Warrior + Cook
  - Once every 48 in-game hours, lethal damage is prevented and 50% health is restored.
- **Deepwarden** — Miner + Shield + Builder
  - 15% less damage and 15% additional ore yield while underground.
- **Wildheart** — Farmer + Hunter + Husbandry
  - 20% less damage from animals and 15% additional meat yield.
- **Grand Artisan** — Cook + Potter + Tailor + Leatherworker
  - 20% additional crafted-item output.
- **Stormforged** — Blacksmith + Ranger + Beekeeper
  - 15% ranged damage and 15% ranged-equipment durability conservation.

## Compatibility

- Existing profession and node IDs remain unchanged.
- Existing saves remain compatible.
- Hidden-class discoveries are stored in synchronized player progression attributes.
