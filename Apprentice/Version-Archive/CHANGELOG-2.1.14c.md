# Apprentice 2.1.14c — Compact Window Restore

## Interface

- Restored the Apprentice window to its earlier fixed `1120 x 682` canvas.
- Removed the 2.1.14b behavior that expanded the dialog toward the active
  game-window edges.
- Kept the SKILLTREE, STATS, and HIDDEN CLASSES tabs introduced in 2.1.14b.
- Changed the compact Hidden Classes layout to four columns so all eight
  discovery cards remain visible at the restored window size.
- Kept the shield-backed Combat icon and true eye icon for Eagle Eye.

## Review

- Confirmed that tab hit regions account for the 44-pixel tab bar offset.
- Confirmed that hidden-class discovery remains server-owned and persistent.
- Confirmed that hidden-class bonuses continue through the existing damage,
  durability, drop-yield, crafting-yield, and player-stat hooks.
- Confirmed that tree IDs and node IDs are unchanged.

## Compatibility

- Existing saves remain compatible.
- Hidden-class discoveries from 2.1.14b remain compatible.
