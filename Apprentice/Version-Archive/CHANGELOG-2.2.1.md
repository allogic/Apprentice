# Apprentice 2.2.1 — Character Selection Compatibility

Final release promoted from the tested 2.2.1f checkpoint.

## Fixed

- Preserved the six vanilla class definitions and disabled them through their
  `enabled` fields instead of deleting the entire vanilla class registry.
- Existing players whose saved class is Commoner, Hunter, Malefactor,
  Clockmaker, Blackguard, or Tailor can open `.charsel` without triggering
  `Not a valid character class code`.
- The character selector continues to offer only the nine Apprentice races.
- Changed the package version to valid SemVer `2.2.1`.
- Migrates invalid legacy class state on both the server and client before the
  automatic character-selection dialog opens.
- Prevents the partially initialized selection dialog from crashing on its
  first mouse click.

## Preserved

- The corrected `DamageItem` durability hook.
- The approved 2.1.14i Apprentice GUI layout.
- All profession, skill-tree, and hidden-class progression identifiers.
