# Apprentice 2.2.1b — Saved-Class Migration

## Fixed

- Moved the six disabled vanilla compatibility records into Apprentice's own
  `config/characterclasses.json`, which the client log confirms is loaded.
- Existing vanilla class codes remain resolvable even when a stale external
  `assets/game/config/characterclasses.json` is an empty array.
- Added disabled migration aliases for the Aldi's Classes and Homesteader class
  codes previously present in this save's crash history.
- Only the nine Apprentice races remain enabled and visible in `.charsel`.
- Uses valid SemVer `2.2.1-b` internally; the release filename remains 2.2.1b.

## Preserved

- The game-domain six-class compatibility registry for clean installations.
- The empty obsolete index patch file.
- The corrected `DamageItem` durability hook.
- The approved 2.1.14i Apprentice GUI layout.
- All Apprentice profession, skill-tree, and hidden-class identifiers.
