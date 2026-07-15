# Apprentice 2.2.0b — Races Foundation Server Fix

This distinct hotfix version makes it easy to confirm that the server has
stopped loading an older duplicate 2.2.0 package.

The obsolete character-class patch path is intentionally retained as an empty
patch array. This harmless asset shadows stale copies of the former root-level
replace operation while the valid direct game-domain override does the work.

## Added

- Added nine playable races: Dragonborn, Dwarf, Elf, Gnome, Goliath,
  Halfling, Human, Orc, and Tiefling.
- Added one positive and one negative, server-authoritative trait for every
  race.
- Added localized race names, descriptions, trait names, and stat text.
- Added common starting clothing to every race.

## Changed

- The vanilla character-class asset is cleared through a focused JSON patch,
  leaving the mod-owned races as the character-creation choices.
- Updated mod metadata to version 2.2.0.

## Preserved

- All Apprentice professions and XP rewards.
- All skill-tree and node IDs.
- All hidden-class IDs, discovery conditions, and effects.
- The complete approved 2.1.14i Apprentice GUI baseline:
  - compact top tabs with the **HIDDEN** label;
  - whole-dialog scaling inside the Apprentice window;
  - fixed larger content area and reduced brown frame padding;
  - raised, backed skill-tree labels;
  - horizontally and vertically centered profession names;
  - centered `Lv N` labels.

## Corrected package baseline

- Replaced the mistakenly reused 2.1.14d GUI files with the final 2.1.14i
  source before packaging 2.2.0.
- Race assets do not alter Apprentice window sizing or layout.

## Server startup fixes

- Replaced the invalid root-array JSON operation with a direct empty override
  at `assets/game/config/characterclasses.json` and an inert `[]` shadow file
  at the former patch path.
- Corrected the Harmony durability-prefix parameter from `itemslot` to the
  Vintage Story API name `itemSlot`, allowing the `DamageItem` hook to install.

## Next model milestone

Race-specific player meshes, textures, attachment transforms, eye heights,
and hitboxes are not included yet. They require validation against the actual
Vintage Story player shape and renderer before they can safely replace the
default model.
