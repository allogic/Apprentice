# Apprentice 2.1.8 — Icon Skill Tree

## Based on 2.1.7

This release keeps the 2.1.7 profession-definition and purchase-response fixes intact, including server-authoritative purchasing, pending-button feedback, exact rejection messages, class-scoped point accounting, and immediate client display updates.

## New in 2.1.8

- Replaced category placeholders such as `CB`, `GA`, and `CR` with scalable vector icons.
- Replaced all profession abbreviations with profession icons:
  - Miner: pickaxe
  - Woodworker: axe
  - Farmer: wheat
  - Builder: bricks
  - Blacksmith: anvil
  - Cook: cooking pot
  - Potter: vase
  - Leatherworker: hide
  - Tailor: needle and thread
  - Hunter: bow
  - Warrior: sword
  - Ranger: compass
  - Fisher: fish
  - Animal Husbandry: paw
  - Beekeeper: bee
  - Spearman: spear
  - Shield: shield
  - Tank: helmet
- Replaced node placeholders `I`, `II`, `A`, `B`, `A+`, and `B+` with visual tier/path badges.
- Capstones now combine the profession icon with a crown.
- Icons are drawn directly with Cairo vector paths and scale with the responsive GUI; no external texture files are required.
- Preserved all existing node IDs for save compatibility.

## Inherited 2.1.7 fixes

- Profession-specific node names, descriptions, and displayed effect labels for all 18 trees.
- Purchase requests show a pending state and cannot be spam-clicked.
- Server results immediately update the displayed rank and remaining points while watched attributes synchronize.
- Exact server rejection reasons are shown to the player and written to the server log.
- Skill points and purchased ranks remain scoped to their own profession tree.
