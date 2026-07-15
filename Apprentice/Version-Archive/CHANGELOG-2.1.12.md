# Apprentice 2.1.12 — Archetype Skill Rewrite and Icon Pass

## Based on 2.1.11b

This release keeps the 2.1.11b stray-line fix and the compact XP counter, then reworks skill-tree bonuses and icons in a broader pass.

## New in 2.1.12

- Replaced **every remaining XP-gain node bonus** with a more fitting archetype-related effect.
  - Gathering / crafting / profession trees now use more specific **work-speed** style bonuses.
  - Combat trees keep or expand **weapon-damage** or **maximum-health** style bonuses where they fit.
  - Shield progression was reworked away from XP gain and now grants additional **endurance / maximum health** bonuses.
- Reworked the **skill-tree icon pass** to reduce generic-looking nodes.
  - The old generic book / medal treatment for foundation and discipline nodes was removed.
  - Nodes now use more class-themed iconography.
  - Node icons were made larger for better readability.
- Reworked the **Miner pickaxe icon** to more closely match the provided pickaxe reference.
- Reworked the **Ranger** profession icon to a clearer bow-and-arrow style.
- Reworked the following node icons to feel more like the real thing:
  - Surveyor
  - Master Surveyor
  - Tracker
  - Master Tracker
  - Skirmisher
  - Arrowstorm
- Shortened **Animal Husbandry** to **Husbandry** in the JSON-driven display data.
  - This affects the visible class naming in the UI while keeping the internal tree id unchanged for compatibility.
- The Grandmaster crown styling remains in place.

## Compatibility

- Existing save data remains compatible.
- Tree IDs remain unchanged.
- Node IDs remain unchanged.
- No external icon textures are required.
