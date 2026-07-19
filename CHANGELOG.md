# Changelog

## 2.7.0 RC27

- Repaired Wild Venomberry's native fruiting-bush integration by matching every
  generated wild/grown state before the vanilla fallback and supplying complete
  Blueberry texture alternates for all leaf, stem, flower, and berry planes.
- Strengthened Advanced Trap capture by clearing both client controls and the
  server's movement-packet position every tick, preventing player packets from
  immediately overriding the trap pin while keeping the block walkable.
- Restored the Composite Bow, Grandmaster spear, fishing rod, and all poison
  arrows to Vintage Story's interactive item classes, native meshes, animations,
  transforms, and valid copper projectile entities.
- Added poison-arrow damage-over-time details to held-item and handbook text,
  restored the four liquid poison names used by sealed barrels, and replaced
  invalid metal sound references with the vanilla anvil sound set.
- Rechecked the supplied RC26 logs: no new runtime crash was recorded. The
  functional warnings were the prior Venomberry fallback and texture failures;
  the remaining historic crash entries predate RC26.

## 2.7.0 RC26

- Fixed the Vintage Story 1.22.3 danger-map Harmony bridge by binding its
  `ChunkMapLayer.Render` arguments positionally, so engine parameter renames
  no longer abort client startup.
- Moved every Apprentice item, block, and block-entity class registration
  ahead of optional map and inspection integrations. Each client patch is now
  isolated, preventing a presentation compatibility failure from causing
  missing-class crashes such as `ApprenticeTrapKit`.
- Corrected Wild Venomberry's final-variant wildcard so all four native
  fruiting-bush states resolve and harvest `apprentice:venomberry` instead of
  the nonexistent `game:fruit-venomberry`.
- Added Vintage Story's required `AnimationAuthoritative` behavior to the
  atlatl spear item definition.

## 2.7.0 RC25

- Fixed the Advanced Trap build failure caused by declaring the `trap` pattern
  variable twice inside one interaction method. The completion lookup now uses
  a distinct variable and cannot be read before assignment.
- Migrated trap capture and pinning from the deprecated `Entity.ServerPos` API
  to the current authoritative `Entity.Pos` API used by Vintage Story 1.22.3.
- Tightened nullable handling for trap and Venomberry block lookups, empty
  danger-map packets, clay recipes, and fruit-press state discovery to remove
  the warnings exposed by nullable analysis.

## 2.7.0 RC24

- Updated Advanced Trap interaction cancellation to Vintage Story 1.22.3's
  five-parameter, boolean `OnBlockInteractCancel` API. Releasing or otherwise
  cancelling the five-second rearm action still clears progress to zero, and
  the project now compiles against the supplied 1.22.3 assemblies.
- Enabled nullable analysis in CakeBuild, removed the danger-map layer's hidden
  inherited `api` field, and updated Cake.Frosting from 6.1.0 to 6.2.0 so its
  dependency graph uses the repaired NuGet 7.6.0 packages instead of the
  vulnerable 7.3.0 pair reported by Visual Studio.
- Expanded the source archive to include CakeBuild, the solution, documentation
  and validation tooling; earlier source ZIPs contained only `Apprentice/`, so
  build-project fixes could not reach a clean checkout.

## 2.7.0 RC23

- Converted Wild Venomberry to Vintage Story's native fruiting-bush asset
  lifecycle: young, mature, flowering, ripening, ripe, harvested regrowth,
  climate dormancy, cuttings, and ordinary ripe-bush harvesting now use the
  base-game block entity and behavior.
- Replaced the broken Blueberry-specific shape and missing Venomberry texture
  paths with the vanilla medium fruiting-bush mesh and its complete stage-aware
  Blueberry material mapping. Venomberry keeps its own harvest item while using
  proven vanilla bush geometry and transforms.
- Added a native Venomberry cutting that grows into a cultivated Venomberry
  bush, and moved all resulting names into Apprentice's language namespace
  instead of distributing a conflicting full `assets/game/lang` file.
- Corrected the Advanced Trap and Starsteel anvil break-sound references and
  rechecked the newest client/server logs; the remaining startup warnings came
  from the prior broken bush asset and an optional handbook diagnostic.

## 2.7.0 RC18h

- Fixed the release packager after the ingots were converted to vanilla metal
  variants. It now validates the vanilla ingot textures and metal patch instead
  of requiring the intentionally removed duplicate item definitions.
- Release packaging still hard-fails when `Apprentice.dll` is absent, preventing
  an asset-only install in which traps, poison, the heatmap, and bush behavior
  appear present but cannot execute.

## 2.7.0 RC18g

- Prevented a freshly armed trap from immediately catching the player who
  armed it; the player remains ignored only until leaving that trap tile.
- Replaced the armed trap grate with an octagonal, raised-jaw foothold model
  with a central pressure plate and inward teeth.
- Applied captured poison-arrow effects independently of reflected creature
  health fields and added a server notification when poison is accepted.
- Removed the duplicate `apprentice:ingot-*` family so recipes and forge work
  use only the native `game:ingot-*` metal variants.
- Converted both metal-ingot textures to RGBA for forge work-item rendering.
- Restored all Venomberry fruiting-bush geometry across growth stages and kept
  harvested bushes in the vanilla regrowth lifecycle.
- Restored the Grandmaster spear as a visible ordinary item and moved the
  Composite Bow to the vanilla recurve-bow mesh.

## 2.7.0 RC18f

- Changed Advanced Trap rearming to an uninterrupted five-second right-click
  hold and enforced an empty runtime collision box for every trap state.
- Preserved poison identity from the projectile stack, accepted hits whose
  projectile no longer retains its shooter reference, and moved Grandmaster
  authorization to crafting/unlocking instead of silently cancelling damage.
- Slimmed poison-arrow heads, coatings and fletching toward vanilla arrow
  proportions; gave the Grandmaster spear native spear behavior/transforms and
  corrected Composite Bow inventory and hand transforms.
- Pointed both custom ingot definitions at the additive vanilla metal-ingot
  textures used by forge rendering instead of unrelated custom textures.
- Added a compatibility behavior that reports health/growth state on legacy
  Venomberry blocks and migrates them to the native fruiting-bush lifecycle on
  harvest.

## 2.7.0 RC18c

- Corrected native ore host textures to the vanilla `{rock}1` naming scheme,
  removing the question-mark fallback from every Starsteel and Aethersteel ore
  grade and host-rock variant.
- Applied Venomberry variant, shape, and texture mappings on the client as well
  as the server, while retaining its vanilla fruiting-bush lifecycle.
- Allowed validated ecology entries to target existing namespaced game blocks,
  so the native Venomberry bush is no longer disabled during registry loading.
- Limited universal poisoned-food/drink Harmony hooks to concrete method
  implementations, eliminating the repeated inherited-method startup errors.

## 2.7.0 RC18

- Made prepared poison applicable at runtime to every nutrition-bearing item
  and drink from the base game or a loaded mod, including water, wine and
  distilled liquor stored in bottles, bowls, barrels or tanks. Poison metadata
  follows the contained liquid and triggers the server-authoritative poison
  scheduler when the poisoned portion is consumed.
- Patched every loaded collectible implementation of the consumption method,
  rather than only the base class, so modded foods and drinks with overrides
  cannot bypass poison damage.
- Converted Wild Venomberry to the native fruiting-bush lifecycle with vanilla
  growth, harvest and regrowth behavior.
- Added native poor, medium, rich and bountiful Starsteel/Aethersteel ores for
  every vanilla host rock, plus deep deposit definitions modeled after the
  supplied Uranium Expanded implementation.
- Restored vanilla metalbit, workitem and metalblock variant generation and
  supplied the required additive metal textures instead of suppressing the
  missing variants at startup.
- Removed stale vanilla-character-class replacement patches, obsolete harvest
  hooks and hard-coded optional-mod barrel recipes that produced startup errors.
- Hardened asset validation for native additive game-domain deposits, external
  item texture slots and multi-variant ore textures.

## 2.7.0 RC17

- Rebuilt the Advanced Trap as a collisionless, server-authoritative foothold trap: living entities can walk into it, take 1.5 flat piercing damage, and remain pinned until it is rearmed.
- Rearming a triggered trap now requires an uninterrupted five-second left-mouse hold. Cancelling the block-breaking gesture discards all progress, while completion preserves the trap and plays a staged closed-to-open jaw animation.
- Added persisted trap/captive state and explicit armed, opening, and triggered meshes without opaque or solid block faces, so neither movement nor the supporting terrain is hidden.
- Switched Starsteel and Aethersteel to the vanilla ingot mesh so only their materials differ from base-game ingots.
- Switched poison arrows, the composite bow and Grandmaster spear to vanilla arrow, recurve-bow and spear meshes; their custom identity now comes from materials and gameplay data.
- Fixed poison-arrow hit attribution by checking both source and cause projectile entities plus damage-source projectile-stack fields.
- Fixed trap ground-face culling, Gloamcap transparency, kit badge orientation, spear materials and first-person composite-bow placement.

## 2.7.0 RC16

- Replaced the upside-down crossed-plane Gloamcap world model with an upright, fully three-dimensional stem and layered cap.
- Matched the Gloamcap selection box to its new world geometry.

## 2.7.0 RC15

- Replaced invalid `faces.all` entries in five 3D shapes with the six face keys required by Vintage Story, removing the white question-mark fallback models.
- Rebuilt Venomberry as a full bush with ordinary foliage and individually scaled berries instead of enlarging its inventory artwork into a world plant.
- Corrected the Advanced Trap drop quantity to a `NatFloat` object so the block loads and places normally.
- Added `dependsOn` guards to every optional Wildcraft Fruit, Expanded Foods, and Butchering poison recipe, eliminating missing-mod recipe floods without losing installed-mod support.
- Excluded Starsteel and Aethersteel from unintended vanilla metal-bit, work-item, sheet, and block variant templates until their complete 2.9 smithing families are available.
- Replaced the flat-card Starsteel/Aethersteel firepit representation with a proper six-faced ingot model and material mapping.
- Made the custom anvil non-occluding so supporting block faces remain visible around its narrower base.
- Clean the generated debug-mod asset directory before copying current assets, preventing deleted legacy overrides from surviving incremental Visual Studio builds.

## 2.7.0 RC14

- Scanned the supplied mod collection for actual negative-health foods and added exact low-, medium-, and high-poison compatibility lists for Wildcraft Fruit and Expanded Foods without accepting harmless wildcard variants.
- Expanded poison brewing to the requested wine/distilled-alcohol families, added the four-part Grandmaster formula, and allowed coating vanilla and Butchering arrow families.
- Replaced poison-arrow cards with a shared volumetric arrow model, tier-matched poison colors, and calibrated inventory, ground, and hand transforms.
- Made completed cementation blisters remain forge-hot and require any native or subclassed tong item for safe removal; destroyed completed furnaces also preserve output temperature.
- Added retained danger-heatmap state, sealed the Starsteel anvil base, repaired plant bounds and hand models, and strengthened projectile-stack recovery for poison damage.

## 2.7.0 RC13

- Retained the latest authoritative danger-state packet independently of the world-map layer, then hydrate layers created after packet delivery on construction, map open, and render.
- Replaced the Composite Bow's flat inventory-card mesh with a dedicated volumetric bow shape, material texture, and first-/third-person transforms.

All notable changes to Apprentice are documented here.

## 2.7.0

### Grandmaster progression

- Added the 18 capstone-authorized Grandmaster collectibles and stations with
  recipes, language, handbook assets and server-side recipe gates.
- Expanded hidden discoveries from the legacy eight to 28 data-driven
  profession/heritage combinations, with server evaluation, pagination and
  idempotent unlock-schema migration.

### Advanced metals and poison

- Added exact 16-piece Starsteel and Aethersteel cementation charges, persisted
  fuel/refractory costs, balance-stable completion deadlines and once-only
  output claiming.
- Added tier-6 Starsteel and tier-7 Aethersteel metal data, a Starsteel anvil
  gate and Apprentice-owned smithing inputs that avoid unsafe vanilla-domain
  work-item assumptions.
- Added four fictional liquid-poison tiers, dedicated coated-arrow items and a
  server-owned damage-over-time scheduler with immunity, PvP, persistence and
  bounded stacking rules.

### Danger regions and ecology

- Added a permanent spawn-centered danger anchor with ten distance tiers,
  one-time entity health/damage scaling and one-time bonus-drop rolls.
- Added an optional cached world-map heatmap and bounded deterministic
  new-chunk generation for Apprentice-owned Venomberry and Gloamcap plants.

### Release hardening

- Added a validated 2.7 content registry whose malformed child definitions fail
  independently without introducing hard dependencies on optional mods.
- Extended asset validation across collectibles, recipes, textures, language,
  discoveries, exact charges and Grandmaster unlock outputs.
- Made packaging run the complete asset validator and corrected the fire-clay
  brick reference to the verified `game:burnedbrick-fire` asset.
- Made packaging independent of the caller's working directory, removed a stale
  ingot-pile patch target, and changed the Tower Shield to the fixed native
  shield class so incomplete round-shield variant tables cannot crash loading.
- Added a dedicated Apprentice creative tab, a finalization-time visibility
  fallback for every Apprentice collectible, and explicit localization for the
  Danger heatmap world-map tab. Startup now reports incomplete/stale installs
  instead of silently presenting a 2.2.4 catalog under a 2.7 source tree.
- Deferred first-time danger-anchor creation until the first player is fully
  ready, avoiding Vintage Story's uninitialized `PlayerSpawnPos` during new
  world loading. The anchor is persisted immediately and already loaded
  creatures are processed through the idempotent tier path.
- Made danger heatmap delivery explicit on the gameplay network channel and
  switched the overlay to Vintage Story's premultiplied-alpha texture path.
  Selecting the heatmap now preserves the Terrain base layer, and the tint is
  rendered at depth 55: above terrain chunks at 50 and below waypoint
  icons at 60. This prevents the overlay from disappearing behind the map or
  replacing it with an opaque dialog-colored rectangle. The server also
  resends the authoritative snapshot on every map open, closing the startup
  race where the first gameplay-channel packet arrived before the map layer.
  The client now additionally requests and retries the snapshot while the map
  is open, so early or dropped one-way delivery cannot leave an inert tab.
- Replaced ingredient artwork used as poison-liquid surfaces with four clean,
  dedicated fluid textures. Poison now uses ten 100 mL portions per litre, so
  the 0.1 L arrow-coating recipe consumes exactly one visible portion.
- Corrected the shared 2.7 item-icon UV orientation and excluded Starsteel and
  Aethersteel from the vanilla ingot variant template, removing the mirrored
  display and unintended untextured `game:ingot-*` duplicates.
- Replaced all 30 flat 2.7 inventory textures with a coherent transparent
  128x128 pixel-art set. Poison arrows are now distinct item silhouettes,
  metal families share consistent materials, and kits, plans, equipment and
  organic samples no longer use malformed pseudo-3D source artwork. The
  shared icon shape now has one GUI-facing plane instead of two oppositely
  mapped faces rendering on top of each other. Flat art now fills more of its
  slot; poison arrows keep their intended diagonal orientation; and the two
  custom ingots match vanilla ingot handedness.
- Replaced the Starsteel anvil's transparent ingot-face mapping with a dedicated
  opaque, tileable forged-Starsteel block texture, eliminating fragmented
  inventory and world rendering.
- Corrected the enemy health-bar shader domain and gave the program a unique
  name. Shader initialization is now optional and cannot abort the remaining
  client UI or packet handlers. Builds also reject the obsolete full
  `assets/game/config/characterclasses.json` override left by source overlays.
- Removed the development `--addOrigin` argument because the project already
  copies assets into the debug mod directory; debug sessions now load each
  asset and JSON patch exactly once, matching the packaged release.
- Retained every 2.2.4-e character creation, persistence, camera and pause-menu
  repair.

## 2.2.4-e

### Fixed

- Made Skin confirmation wait for an acknowledged server save, close through
  the public dialog lifecycle, and report rejection or timeout instead of
  leaving an invisible focused dialog.
- Replaced fragmented reconnect state with a validated, versioned, atomic
  customization snapshot that restores race, subrace, body, class and all
  eleven appearance selections without reapplying defaults.
- Corrected the Race/Skin tab index-to-ID translation so either tab focuses
  with one click.
- Removed Randomize and Last selection from Skin & Voice composition.
- Registered Ruby as a selectable eye color so Drow can use the configured red
  eye option.
- Measured and constrained trait text before placing responsive Body & Race
  Options, preventing wrapped text from overlapping the controls.
- Placed Hair Color and the five localized Horn Color swatches directly below
  Eye color while preserving the stock Voice pitch column break, so Hair type,
  Hair extra, Facial expression, Mustache and Beard remain in the right column.
- Bound Gnome, Halfling, Dwarf, Elf and Dragonborn facial geometry to the
  currently selected base-skin texture instead of fixed green atlas pixels.
- Rebound tooth geometry to a dedicated `toothwhite` atlas key and ivory-white
  material so race/horn texture aliases cannot recolor Dragonborn teeth.
- Scaled the vanilla per-frame eye and hitbox targets by the effective race and
  height-slider value, keeping the first-person camera inside the scaled head
  while retaining vanilla pose, mount and view-bobbing adjustments.
- Explicitly unfocused, unregistered and disposed character-creation dialogs so
  no invisible input surface blocks the pause-menu center buttons.
- Added a pause-menu safety sweep that removes stale character, race-options and
  legacy horn-color dialogs from both client GUI input lists.

### Compatibility and maintenance

- Migrated legacy `apprenticeRaceSelection-v1` data to the version 2 snapshot
  format and normalized stale option codes safely.
- Preserved the 2.2.4 asset, startup, notification, validation and packaging
  fixes while reconciling the optimized source with the current repository.
- Updated the mod version and validator for the `2.2.4-e` hotfix suffix.

## 2.2.4

### Fixed

- Restored asset copying for direct builds and publishes.
- Removed the conflicting full replacement in the `game` asset domain.
- Restored defensive client startup and main-thread UI notification handling.

### Optimized

- Indexed profession reward handlers by interaction name.
- Cached reflective member discovery and cleared the cache on unload.
- Split the race appearance and interaction systems into focused source files.
- Removed three unused legacy HUD implementations.
- Decoupled the packaging project from the Vintage Story API assembly.
- Preserved older release archives when packaging a new version.

### Validation and maintenance

- Added cross-file configuration, shape, texture, language, metadata and level
  table validation.
- Added GitHub Actions asset validation.
- Added release documentation and corrected mod metadata.
