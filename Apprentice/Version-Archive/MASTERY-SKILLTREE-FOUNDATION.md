# Mastery and Skill Tree Foundation — 2.0.0

## Core rules

- Every class level after level 1 grants one skill point.
- Earned points are derived from level; they cannot desync or duplicate.
- Purchased ranks are stored under the class watched attributes.
- Purchases are validated server-side.
- Nodes support ranks, costs, level requirements, spent-point requirements,
  prerequisites, either/or prerequisites, exclusive specializations and capstones.
- Capstone recipe locks are server-enforced at ingredient consumption.

## Automatic mastery

Fully wired automatic gameplay passives in this foundation:

- Miner: +0.75% stone/ore mining speed per mastery rank, capped at 50 ranks.
- Woodworker: +0.75% wood chopping speed per mastery rank, capped at 50 ranks.
- Warrior: +0.5% matching one-handed weapon damage per mastery rank.
- Ranger: +0.5% matching ranged weapon damage per mastery rank.
- Spearman: +0.5% matching spear damage per mastery rank.
- Tank: +0.1 maximum health per mastery rank, capped at +5 HP.

PvP receives half of the combat bonus. Weapon matching is data-driven through
`WeaponPatterns` in skilltrees.json.

Every other class already has a full point tree, exclusive specialization and
capstone recipe key. Their current automatic foundation bonus is a small class
training/XP bonus while their specialized yield, durability, food and machine
hooks are balanced in later passes.

## Skill effects currently implemented

- ExperienceGain
- MiningSpeed passive
- WoodMiningSpeed passive
- ToolSpeed nodes
- WeaponDamage passive and nodes
- MaxHealth passive and nodes
- UnlockRecipe

The JSON effect registry is deliberately extensible for future effects such as
crop yield, item durability, shield efficiency, satiety and active abilities.

## GUI

Press U to open a full mastery and skill-tree dialog:

- class list on the left;
- level, XP bar, earned and spent points at the top;
- branching skill nodes in the center;
- selected-node requirements and effects on the right;
- capstones are explicitly marked;
- purchase feedback is returned by the server.

## Capstones

Each class ends in a level-40, eight-point capstone after 28 points have been
spent and one expert specialization has been chosen. The capstone stores a
recipe unlock code such as `apprentice:sewingkit` or
`apprentice:compositebow`.

The recipe gate is already active. When those item recipes are added, players
without the matching capstone cannot consume the crafting ingredients.
