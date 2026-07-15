# Apprentice 2.1.14 — Ranger Icons and Movement Rebalance

## Based on 2.1.13

This release keeps the complete skill-effect rebuild and isolated Cairo icon rendering from 2.1.13.

## GUI

- Increased the **Reset View** button width so its text stays inside the button border.

## Ranger icon redesign

- Reworked every Ranger icon that uses a bow.
- Bow graphics now show a **fully drawn bow with a nocked arrow ready to release**.
- Ranger specialization icons now use either the drawn-bow design or a real quiver motif:
  - Eagle Eye: drawn bow
  - String Care: drawn bow with emphasized string
  - Sharpshooter: drawn bow aimed at a target
  - Skirmisher: drawn bow with movement marks
  - Master Sharpshooter: drawn bow with a full sight marker
  - Arrowstorm: overflowing arrow quiver
- The Ranger profession icon uses the new drawn-bow silhouette.

## Miner icon redesign

- Reworked the Miner pickaxe so it no longer reads as a scythe.
- Both sides of the pickaxe head now have equal reach and matching curvature.
- Added a visible lower head edge and socket so the silhouette reads as a two-sided pickaxe.

## Movement-speed rebalance

Movement speed can now come from only four skills:

- Hunter — **Trail Legs**: maximum +3%
- Woodworker — **Timber Stride**: maximum +4%
- Builder — **Site Runner**: maximum +3%
- Beekeeper — **Bee Line**: maximum +5%

Combined maximum: **+15% movement speed**.

The code also clamps the total Apprentice movement-speed bonus at +15%.

All former movement-speed sources in Farmer, Cook, Leatherworker, Tailor, Warrior, Ranger, Fisher, Husbandry, Spearman, and Tank were replaced with class-appropriate effects. Juggernaut now reduces projectile damage instead of increasing movement speed.

## Compatibility

- Tree and node IDs are unchanged.
- Existing save data remains compatible.
