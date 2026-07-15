# Apprentice 2.2.1c — Isolated Compatibility Domain

## Fixed

- Moved all disabled saved-class migration records into the new
  `apprenticecompat` asset domain.
- The development origins shown in the submitted logs can only replace the
  `apprentice` and `game` domains, so they can no longer shadow this registry.
- The regular Apprentice class file again contains only the nine enabled race
  choices.
- Existing vanilla, Aldi's Classes, and Homesteader saved class codes remain
  resolvable through the isolated compatibility registry.
- Uses valid SemVer `2.2.1-c` internally; the release filename remains 2.2.1c.

## Preserved

- The intentionally empty game-domain registry that hides vanilla choices.
- The empty obsolete index patch file.
- The corrected `DamageItem` durability hook.
- The approved 2.1.14i Apprentice GUI layout.
- All Apprentice profession, skill-tree, and hidden-class identifiers.
