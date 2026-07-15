# Apprentice 2.1.14e — Whole-Dialog Scaling

## Interface

- Restored the three tabs to their compact 150-pixel width.
- Kept the third tab label as **HIDDEN**.
- Added one shared scale transform for the complete Apprentice interface:
  tabs, header, profession sidebar, skill tree, details, Stats, Hidden cards,
  icons, borders, spacing, and text now scale together.
- The scale is calculated only from the inner bounds of the
  **Apprentice Mastery and Skill Trees** dialog.
- The game-window width and height are no longer used for interface scaling.
- Mouse hit testing, middle-button dragging, and wheel targeting use the
  inverse dialog scale so input remains aligned with the scaled visuals.

## Compatibility

- Existing saves remain compatible.
- Tree IDs, node IDs, hidden-class IDs, and tab IDs are unchanged.
