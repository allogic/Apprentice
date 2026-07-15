# Apprentice 2.2.1a — Deterministic Class Registry

## Fixed

- Replaced the fragile `/0/enabled` through `/5/enabled` JSON patches with a
  complete disabled compatibility registry for the six vanilla class codes.
- The class registry no longer assumes that the base game array contains six
  entries when Apprentice patches are processed.
- Existing Commoner, Hunter, Malefactor, Clockmaker, Blackguard, and Tailor
  saves retain a valid class code and can open `.charsel` to choose a race.
- Compatibility records retain the vanilla trait codes so existing characters
  keep their class modifiers until they choose a race.
- The obsolete patch file now contains `[]`, which also overwrites a stale
  patch copied into an uncleared `bin/Debug/Mods/mod` output directory.
- Uses valid SemVer `2.2.1-a` internally; the release filename remains 2.2.1a.

## Preserved

- Nine enabled fantasy races and their positive/negative traits.
- The corrected `DamageItem` durability hook.
- The approved 2.1.14i Apprentice GUI layout.
- All profession, skill-tree, and hidden-class progression identifiers.
