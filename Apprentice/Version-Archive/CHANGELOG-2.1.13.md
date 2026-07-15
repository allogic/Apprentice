# Apprentice 2.1.13 — Complete Skill Effect Rebuild

## Based on 2.1.12

This release is a gameplay-effect redesign rather than a text-only balance pass.
Every non-capstone node now has a distinct effect type within its own profession
tree, and all remaining mastery XP-gain passives were replaced with archetype
mechanics.

## Hunter rebuild

- **True Aim** increases ranged critical-hit chance.
- **Field Dressing** increases hide yield from carcasses.
- **Tracker** adds a real tracking mechanic:
  - sneak to detect nearby animals;
  - each rank expands detection range;
  - the basic node reports a direction.
- **Butcher** gives additional meat when harvesting carcasses.
- **Master Tracker** upgrades the tracking report with animal identity and
  approximate distance.
- **Apex Hunter** increases damage dealt specifically to animals.

## Tank rebuild

Tank no longer consists only of maximum-health bonuses:

- **Iron Constitution** — maximum health.
- **Pain Tolerance** — general incoming-damage reduction.
- **Ironhide** — reduced fire and heat damage.
- **Juggernaut** — movement speed.
- **Second Wind** — improved healing received.
- **Unstoppable** — extra damage reduction below 35% health.

## Full tree redesign

- All 18 trees now use six distinct, concrete node effects per tree.
- Removed all remaining `ExperienceGain` node effects.
- Removed all remaining `ExperienceGain` mastery passives.
- Added working mechanics for:
  - tool durability preservation;
  - block-drop yield bonuses;
  - crop and seed yield;
  - fish, honey, meat, and hide yield;
  - crafting, smithing, cooking, joinery, furniture, and clay output chances;
  - movement speed;
  - hunger reduction;
  - food saturation;
  - healing effectiveness;
  - maximum health;
  - general, shielded, low-health, projectile, fall, fire, animal, and bee
    damage reduction;
  - weapon, ranged, low-health, and animal damage;
  - ranged critical chance and critical damage;
  - animal tracking.
- Effect labels describe the stat or output that actually changes instead of
  using vague phrases such as “Apex takedown damage”.

## Effect panel clarity

The Effects panel now shows only mechanical information:

- unlearned nodes: the value granted at the next rank;
- learned nodes: the current value and next-rank value;
- maximum-rank nodes: the final current value;
- reductions use a minus sign;
- tracking range is displayed in metres;
- Master Tracker explains the additional tracking information directly.

## Stray-line fix

The unwanted diagonal lines are handled with a stronger rendering boundary:

- every node icon is drawn on its own transparent Cairo `ImageSurface`;
- the completed icon is composited into the main tree canvas;
- primitive drawing helpers begin with a fresh Cairo path;
- node rendering also clears paths at its start and end.

An icon rendered for one node can therefore no longer share a Cairo path with
another node.

## Compatibility

- Tree IDs are unchanged.
- Node IDs are unchanged.
- Existing purchased ranks remain save-compatible.
- `animalhusbandry` remains the internal ID, while the visible name stays
  **Husbandry**.
- No external icon textures are required.
