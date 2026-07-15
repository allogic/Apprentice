# Apprentice 2.1.9 — Skill Tree Icon Overhaul

## Based on 2.1.8

This release keeps the 2.1.7 and 2.1.8 fixes intact, including the server-authoritative skill purchase flow, class-scoped point accounting, profession-specific skill tree text, and responsive Cairo rendering.

## New in 2.1.9

- Reworked the **Miner** profession icon with a cleaner pickaxe silhouette.
- Changed the **Combat / Warrior** icon language from an X-like mark to explicit **crossed swords**.
- Replaced the generic-looking node markers with more specific node icons:
  - **Foundation** nodes now use an open-book fundamentals icon.
  - **Discipline** nodes now use a medal / rosette icon.
  - **Path** and **Expert** nodes now use specialization-themed icons instead of abstract badges.
  - **Expert** nodes add spark accents to read as upgraded versions of their specialization.
- Added class badges to non-capstone nodes so each profession still reads clearly at a glance.
- Gave each profession pair of specialization nodes a more thematic visual treatment, such as:
  - Miner: survey / delving
  - Woodworker: joinery / felling
  - Farmer: seedkeeping / harvest
  - Builder: drafting / masonry
  - Blacksmith: blades / armor
  - Cook: gourmet / feastmaking
  - Potter: ceramics / kilnwork
  - Leatherworker: saddlery / tanning
  - Tailor: couture / weaving
  - Hunter: tracking / butchery
  - Warrior: dueling / berserking
  - Ranger: precision / skirmishing
  - Fisher: angling / netcasting
  - Animal Husbandry: breeding / shepherding
  - Beekeeper: hivekeeping / honeycraft
  - Spearman: lancing / impaling
  - Shield: guarding / bulwark defense
  - Tank: ironhide / juggernaut endurance

## Compatibility

- Node IDs are unchanged for save compatibility.
- The skill purchase system remains server-authoritative.
- No external icon textures are required; the icons are still drawn with Cairo vector paths.
