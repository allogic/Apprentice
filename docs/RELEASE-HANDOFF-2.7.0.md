# Apprentice 2.7.0 release handoff

RC13 closes two presentation lifecycle regressions found in live testing: danger heatmap state is retained across map-layer recreation, and the Composite Bow now has a dedicated 3D held model instead of reusing its flat inventory icon.

## Status

This repository is the 2.7.0 source candidate for Vintage Story 1.22.3. The
data/assets pass the repository validator and the C# sources pass syntax
parsing. A release ZIP is not approved until the project has been compiled
against the target game installation and the dedicated-server matrix in
`RELEASE_CHECKLIST.md` has passed.

The 2.2.4-e character creation, exact appearance persistence, one-click tabs,
skin-colored facial geometry, ivory Dragonborn teeth, height-scaled first-person
camera and pause-menu input cleanup remain part of this candidate.

## Implemented 2.7 systems

- A namespaced, data-driven content registry validates Grandmaster artifacts,
  hidden discoveries, cementation charges, poison, danger settings and ecology.
  Invalid child entries are disabled individually and logged.
- All 18 skill-tree `UnlockRecipe` outputs resolve to Apprentice collectibles or
  blocks and are protected by the existing server recipe gate.
- Twenty new discoveries join the eight legacy discoveries. Unlock evaluation
  is server-owned, uses confirmed race/profession state and migrates existing
  unlocks without revocation.
- Starsteel and Aethersteel use a custom exact-charge block entity. Inputs,
  fuel, refractory costs, operator, start time, effective duration and claim
  guard are persisted. Aethersteel smithing additionally requires the exact
  Starsteel anvil.
- Poison is represented by dedicated coated-arrow variants. Damage-over-time is
  applied only after a confirmed projectile hit and is stored on the target
  entity with bounded replacement/extension rules.
- Danger tiers use one persisted world anchor and one persisted entity roll.
  The map overlay uses a cached radial texture above terrain and below waypoint
  icons, and its server-owned state is resent through the world-map protocol on
  every map open. The client also retries an explicit request while state is
  missing. New-chunk ecology generation is deterministic, bounded and limited
  to Apprentice-owned blocks.
- The complete 30-item flat inventory-art set uses coherent transparent
  128x128 pixel art; the four poison-arrow tiers and the two advanced-metal
  families remain visually distinct at inventory scale. Their shared shape has
  one GUI-facing textured plane, preventing reverse-UV duplicate rendering.
  The Starsteel anvil uses a separate opaque block material rather than an
  inventory-icon texture.

## Failure prevention decisions

- No optional gameplay-mod DLL or private type is referenced.
- New IDs, watched attributes, save keys and the map layer are Apprentice
  namespaced.
- The metal registration patch only targets the verified 1.22 metal world
  property. It does not include a stale ingot-pile path or replace vanilla
  metal assets.
- Advanced work items do not impersonate vanilla metal-domain items; this avoids
  the vanilla `workitem-*`/`ingot-*` lookup crashes common to custom metals.
- The Tower Shield uses the fixed native shield class. The variant-driven round
  shield class is deliberately avoided because it throws without complete
  construction/material tables.
- Furnace validation counts split inventory stacks before consuming anything.
  Running duration is persisted so balance changes do not alter active jobs.
- Poison uses item variants instead of arbitrary stack attributes, avoiding
  attribute loss during bow/projectile/pickup serialization.
- Danger scaling stores its schema and original health once per entity, so
  chunk reloads cannot multiply stats again.
- Ecology does no world scan or retrogen and attempts at most the configured
  small count per newly generated chunk.
- Packaging runs `tools/validate_assets.py` before publishing and never ships a
  replacement file in the `game` asset domain. It also refuses to create an
  assets-only release when the freshly published `Apprentice.dll` is absent.
- `AssetsFinalize` redundantly exposes every Apprentice collectible in the
  standard creative categories and a dedicated Apprentice tab, so an unrelated
  asset patch cannot hide the complete 2.7 catalog. Startup logs the resolved
  collectible count and confirms that the danger heatmap layer was instantiated.
- The optional enemy health-bar renderer loads
  `apprentice:shaders/apprenticehealthbar.*`; failure disables only that renderer
  and does not abort the rest of client startup. MSBuild rejects stale source
  overlays that still contain the removed full game-domain class replacement.

## Target build

Set `VINTAGE_STORY` to a complete Vintage Story 1.22.3 installation, then run:

```sh
python tools/validate_assets.py
dotnet run --project CakeBuild -- --target=Package
```

The installable result must be `Releases/apprentice_2.7.0.zip` with
`Apprentice.dll`, `assets` and `modinfo.json` at the archive root. Do not package
or rename an older 2.2.4 DLL as 2.7.0.
