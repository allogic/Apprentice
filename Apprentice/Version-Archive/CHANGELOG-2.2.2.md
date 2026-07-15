# Apprentice 2.2.2

- Promotes the tested 2.2.1 runtime and character-class migration fixes.
- Retains the nine fantasy races and approved Apprentice skill-tree GUI.
- Retains the early client guard that prevents invalid legacy classes from
  crashing automatic character selection or `.charsel`.
- Documents that Visual Studio's development mod path and the installed
  release ZIP must not be active at the same time.
- Adds a hidden, class-synchronized race appearance layer for all nine races.
- Adds nine race-specific model shapes and nine dedicated texture palettes.
- Adds race-specific silhouette geometry for ears, brows, shoulders, muzzles,
  horns, tusks and tails, as appropriate to each race.
- Adds named race attachment anchors for future race equipment and effects.
- Step-parents all race geometry to the stock Seraph bones, retaining native
  animations, clothing, armor, held-item, first-person and third-person paths.
- Synchronizes race appearance whenever Vintage Story applies a character
  class, including initial selection, `.charsel`, reconnects and multiplayer.

## Development testing

When launching through Visual Studio, remove the Apprentice release ZIP from
the normal Vintage Story Mods folder and use only the generated
`bin/Debug/Mods/mod` copy. When testing the release ZIP, launch the normal game
executable without Visual Studio's `--addModPath` argument.
