# Apprentice 2.1.11b — Stray Line Rendering Fix

## Fixed

- Removed the remaining yellow diagonal lines appearing between unrelated skill-tree nodes.
- The lines were Cairo current-path artifacts, not intended skill-tree connections.
- Node icon rendering now explicitly clears the Cairo path before and after drawing each icon.
- Connection curves also begin with a fresh path to prevent future rendering leakage.

## Preserved

- Compact header counter format: `XP 120 / 250`.
- Normal gray skill-tree prerequisite connections.
- All prior purchase, icon, description, and foundation-skill improvements.
- Existing node IDs and save compatibility.
