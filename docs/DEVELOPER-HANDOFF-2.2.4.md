# Apprentice 2.2.4-e Developer Handoff

Updated: 2026-07-17.

This handoff describes the 2.2.4-e implementation candidate, its design
decisions, completed static validation and remaining in-game acceptance work.
Read it before editing the character-creation flow or beginning future content
milestones.

## Repository baseline

- Repository: <https://github.com/allogic/Apprentice>
- Working branch at handoff: `codex/2.2.4-e-hotfixes`
- Baseline commit before the hotfix implementation: `9ece43f`
- Mod ID: `apprentice`
- Mod version: `2.2.4-e`
- Author: Kanista
- Target game dependency: Vintage Story `1.22.3`
- Project: `Apprentice/Apprentice.csproj`
- Package project: `CakeBuild/CakeBuild.csproj`

The supplied optimized source and release documents were reconciled with the
current repository while preserving the later health-bar and build-script
changes from `main`.

## Current scope and implemented features

The 2.2.4-e source currently contains:

- profession experience and skill trees;
- 18 selectable professions with a matching-profession `+10% XP` rule;
- race-first character creation;
- base races, required subrace selection where configured and subclass traits;
- height and thickness controls with a neutral Body Stats trait;
- first-person camera, collision and selection heights that follow the
  effective race/height-slider scale without replacing vanilla pose handling;
- race-aware Skin & Voice palettes/defaults;
- horn, tooth and horn-color options for supported races;
- first-person hiding of custom face/horn/teeth geometry while keeping it
  visible to other players;
- persistent race/body packet infrastructure;
- Race Traits as a read-only fourth skill-tree tab between Stats and Hidden;
- hidden-class/capstone infrastructure;
- optimized interaction dispatch and cached reflection;
- asset/configuration validator and Cake packaging.

Do not infer that the Grandmaster items, advanced metals, arrow poison or danger
heatmap exist. They are future milestones in
[GRANDMASTER-CONTENT-DESIGN.md](GRANDMASTER-CONTENT-DESIGN.md).

## Implemented 2.2.4 hotfix line

All five requested fixes are implemented in the source candidate. Static asset,
JSON and C# syntax validation passes; the runtime acceptance matrix below still
requires a Vintage Story 1.22.3 client and dedicated server.

| Hotfix | Implemented change | Main code/assets |
| --- | --- | --- |
| 2.2.4-a | Skin confirmation waits for the matching save acknowledgement, then unfocuses, unregisters and disposes every character-creation input surface. | `RaceAppearanceSystem.Dialog.cs`, `RaceOptionsDialog.cs` |
| 2.2.4-b | A versioned server-owned snapshot atomically validates, persists and restores race, body, class and exact appearance selections. | `RaceBodyPacket.cs`, `RaceAppearanceSystem.Persistence.cs`, `RaceAppearanceSystem.Appearance.cs` |
| 2.2.4-c | Tabs translate display indexes to semantic IDs once; trait and options layout is measured and constrained responsively. | `RaceAppearanceSystem.Dialog.cs`, `RaceOptionsDialog.cs`, `RaceAppearanceSystem.Traits.cs` |
| 2.2.4-d | Hair Color and five localized Horn Color swatches are composed directly below Eye color; the separate HUD is removed. | Skin & Voice composition hooks in `RaceAppearanceSystem.Dialog.cs` |
| 2.2.4-e | Required facial geometry follows the selected base-skin texture, Ruby is registered for Drow eyes, and teeth use ivory-white. | Skinpart config plus Gnome, Halfling, Dwarf, Elf, Dragonborn and teeth shape assets |

The exact acceptance tests remain in the 2.2.4-a to 2.2.4-e sections of the
milestone document. Screenshots 153–157 from the user remain the visual
regression baseline.

## Root-cause findings

### Dialog remains focused

`BeforeSkinConfirmNext` defers the network save until the stock mouse handler
has returned. After the matching server acknowledgement, the implementation
runs `TryClose()`, directly removes the dialog from the client's loaded/opened
GUI lists and clears/disposes its composers. The race-options companion uses
the same explicit removal path, preventing an invisible input surface from
surviving over the pause menu. (`UnregisterOnClose` is read-only in 1.22.3.)

### Settings revert after reconnect

`RaceBodyPacket` currently carries body/race values plus a dictionary of 11
appearance parts. `ApplyRacePacket`, class restoration, subclass defaults,
facial identity and delayed restoration callbacks can run in an order that
reapplies defaults after the saved parts.

The correct boundary is one validated, versioned immutable customization
snapshot. Persist it atomically on the server; restore race/body first and exact
appearance second under a guard that suppresses default application. Add a
request/result acknowledgement so the client does not close on a failed save.

Current persisted appearance codes are:

```text
baseskin, eyecolor, underwear, voicetype, voicepitch,
hairbase, hairextra, facialexpression, mustache, beard, haircolor
```

Horn style, teeth, horn color, race, subrace, profession, height and thickness
are separate `RaceBodyPacket` fields.

### Race tab semantic ID

The displayed tab array is reordered to Race then Skin & Voice. Stock semantic
IDs remain Race `DataInt = 1` and Skin `DataInt = 0`.
`GuiElementHorizontalTabs.SetValue()` already converts its array index to
`DataInt`, so `BeforeTabClicked` consumes that semantic ID directly. A second
translation was the reason returning from Skin & Voice failed.

### Traits overlap controls

`RaceOptionsDialog.cs` uses fixed `optionsTop = 260`. Trait height changes with
race, subrace, body values, localization, dialog width and UI scale. Position
the options after measured rich-text bounds, reserve the confirm-button region,
and use scrolling/controlled font reduction only if the measured content does
not fit.

### Horn color location and control

The removed `HornColorDialog` used fixed coordinates in a companion HUD. The
current implementation suppresses the stock Hair Color row and recomposes Hair
Color and five Horn Color swatches below Eye color inside the stock composer.
It leaves the stock Voice pitch part visible so its `Colbreak` still moves Hair
type and the remaining face controls into the right column, then shifts only
the existing left-column Underwear and Voice controls below the inserted color
rows. Horn Color exists only for Tiefling/Dragonborn or any future race
explicitly supporting horns.

Current horn-color codes and textures:

```text
dark-gray, light-gray, white, light-pink, yellowish-white
```

### Green face strips

The custom Gnome, Halfling, Dwarf, Elf and Dragonborn shapes contain required
ear/nose/cheek/jaw geometry. Those elements previously mixed a fixed race
texture with the generic Seraph atlas. A hidden `apprenticefaceskin` texture
part now targets `raceskin`, mirrors every supported base-skin variant and is
selected before the race shape. Custom skin geometry samples that channel;
Dragonborn eye geometry continues to sample the live eye atlas.

## Runtime architecture and file map

### Startup and progression

- `Apprentice/src/ApprenticeModSystem.cs` — mod lifecycle, progression network
  channel and client/server handlers.
- `Apprentice/src/ApprenticeConstants.cs` — shared progression names/keys.
- `Apprentice/src/ClassConfig*.cs`, `BaseConfig*.cs`, `SkillTreeConfig*.cs` —
  config models and loaders.
- `Apprentice/src/ClassesManager.cs` — class/profession progression behavior.
- `Apprentice/src/SkillTreeManager.cs`, `ProgressionData.cs`,
  `SkillMasteryPatches.cs` — skill purchase/mastery state and recipe unlocks.
- `Apprentice/src/HiddenClassSystem.cs` — hidden discovery infrastructure.

### Interaction and experience

- `Apprentice/src/interaction/` — generic interaction bridge, event adapters,
  completion/death patches and reflection cache.
- `Apprentice/src/experience/` — experience equations, awards, notification
  packet and progress dialog.
- `Apprentice/src/NativeInteractionClassifier.cs` and
  `BlockInteractionAdapter.cs` — engine action classification.

### Character creation and appearance

- `RaceAppearanceSystem.cs` — system lifecycle, profiles, codes, Harmony patch
  installation and shared appearance constants.
- `RaceAppearanceSystem.Dialog.cs` — reordered tabs, click/confirm hooks,
  companion-dialog lifecycle.
- `RaceAppearanceSystem.Persistence.cs` — packet validation, save/restore,
  profession/subclass controls.
- `RaceAppearanceSystem.Appearance.cs` — palettes, default/allowed appearance
  parts and visual application.
- `RaceAppearanceSystem.Traits.cs` — trait display and body/race stat updates.
- `RaceBodyPacket.cs` — current protobuf persistence/network payload.
- `RaceOptionsDialog.cs` — subclass/profession/sliders/horn/tooth companion GUI.

### Skill-tree GUI

- `GuiElementSkillTreeCanvas.cs` and partial files — canvas lifecycle, tabs,
  panels, rendering, text, node icons, profession icons and Race Traits panel.
- `OverlayManager.cs`, `InterfaceManager.cs` — HUD/dialog integration.

### Assets

- `assets/apprentice/config/characterclasses.json` — race class definitions.
- `assets/apprentice/config/seraphskinnableparts.json` — race/subrace palette
  and part options.
- `assets/apprentice/config/traits.json` — trait definitions.
- `assets/apprentice/config/class.json` and `skilltrees.json` — progression.
- `assets/apprentice/shapes/entity/humanoid/races/` — race shapes.
- `assets/apprentice/shapes/entity/humanoid/races/options/` — horn/teeth shapes.
- `assets/apprentice/textures/entity/humanoid/races/` — race textures, skins
  and horn palettes.
- `assets/apprentice/patches/replace-vanilla-characterclasses.json` —
  order-sensitive patch of the six vanilla class entries.
- `assets/apprenticecompat/config/characterclasses.json` — compatibility-domain
  class data; do not add a full replacement below `assets/game`.

## Existing network and persistence boundaries

- progression channel: `apprentice-progression`;
- race/body channel: `apprentice-racebody`;
- player progression root: `apprentice`;
- class tree key: `classes`;
- race-specific constants and mod-data keys are defined near the top of
  `RaceAppearanceSystem.cs`.

When adding packets:

- keep existing protobuf member numbers stable;
- append new fields rather than reusing numbers;
- version persistent payloads explicitly;
- validate every client value on the server;
- send results/acknowledgements to the requesting player;
- never reference GUI/rendering types from dedicated-server startup.

## Server-mod compatibility contract

The uploaded `all_Mods` archive contained 50 packages. The user removed Aldi's
Classes and Aldi's Classes Homesteader, so both are excluded. The remaining 48
active mods were statically audited and are documented in
[SERVER-MOD-COMPATIBILITY.md](SERVER-MOD-COMPATIBILITY.md).

Non-negotiable rules:

- no optional gameplay mod in `modinfo.json` dependencies;
- no foreign DLL references;
- no copied private foreign implementation;
- optional assets only through explicit code checks or JSON `dependsOn`;
- no replacement of foreign map layers, world generators, spawn policies,
  inventories or liquid classes;
- one concise warning when an optional target changed, never a startup crash.

Important collision points:

- ChangeClass can re-enter character creation and must not overwrite an internal
  restoration snapshot.
- Safe House may deny a hostile spawn; danger scaling must run only after a
  successful spawn.
- map/waypoint mods require a uniquely named cached Apprentice heatmap layer.
- food mods must never classify poison as food or a meal ingredient.
- Expanded Matter/UraniumExpanded must not broaden or satisfy T6/T7 charges.
- Watersheds changes worldgen heavily; danger uses stored coordinates and
  Apprentice-owned deterministic resource passes.

## Future milestone decisions already made

### Grandmaster content

- Implement the 18 existing `UnlockRecipe` outputs as complete vertical
  slices, not empty codes.
- Server authorization is mandatory.
- Durable artifacts/stations gate downstream recipes.
- Eight multi-profession and twelve profession-plus-heritage discoveries are
  designed, but their effects need playtesting after their content exists.

### Advanced metals

- Tier 6 Starsteel is a 16-ingot cementation charge:
  8 steel, 4 meteoric iron, 2 nickel, 1 silver, 1 gold.
- Tier 7 Aethersteel is a 16-ingot charge:
  6 Starsteel, 3 meteoric iron, 2 nickel, and one each copper, tin, bismuth,
  silver and gold.
- Both use a data-driven Apprentice processor with exact multiset matching and
  idempotent output. They are not ordinary crucible alloys.

### Poison

- Liquid poison applies only to arrows.
- Core ingredients are fictional Apprentice berries/mushrooms processed in
  barrels.
- The server validates coating, projectile transfer, hit and one-second DoT.
- First run an item-attribute survival spike; use dedicated arrow variants if
  stack/projectile attributes do not survive every transition.
- No melee poison, food poison or hard dependency on food/plant mods.

### Danger heatmap

- The persisted server/world spawn is the center; beds never move it.
- Tier 0 is base game, with ten increasingly dangerous rings.
- Eligible entity tier is stored once at successful spawn.
- The server owns scaling and loot; the client owns a toggleable cached overlay.
- Rare content and bonus drops are Apprentice-owned; never multiply arbitrary
  external/base drops.

## Build, validation and packaging

From the repository root:

```sh
python3 tools/validate_assets.py
```

With a compatible Vintage Story installation:

```sh
export VINTAGE_STORY=/absolute/path/to/VintageStory
dotnet build Apprentice.sln
dotnet run --project CakeBuild -- --target=Package
```

Expected package: `Releases/Apprentice_<version>.zip`, with `assets`,
`modinfo.json` and `Apprentice.dll` at the archive root.

The asset validator can run without the game, but it cannot validate the exact
order-sensitive vanilla class patch unless `VINTAGE_STORY` points at the target
assets. A successful local compile without the target game assemblies is not a
substitute for the game build.

## Minimum test matrix for every hotfix

1. Run asset validation.
2. Test a new singleplayer world.
3. Test an existing singleplayer world and save/quit/reload.
4. Test a separated dedicated server/client and reconnect twice.
5. Test every race/subrace involved in the change.
6. Test supported GUI scales and a small window.
7. Test first-person self visibility and third-person/other-player visibility.
8. Test Apprentice alone and then the full current 48-mod pack.
9. Inspect client/server main and debug logs with error reporting enabled.
10. Update the changelog only with behavior that passed these tests.

For the current hotfix line, explicitly verify:

- every exact appearance selection survives two reconnects;
- Race and Skin & Voice each focus with one click;
- no trait/control overlap;
- horn-color swatches are correctly placed and persisted;
- no green facial marks remain;
- first-person eye level stays inside each scaled head at the minimum, midpoint
  and maximum height-slider values, including vanilla pose transitions;
- Confirm Skin releases all focus and input capture.

## Release-candidate checklist

1. Build against the Vintage Story 1.22.3 assemblies and package the mod.
2. Run the new-world, existing-world and dedicated-server reconnect tests.
3. Compare every affected race against screenshots 153–157 at supported GUI
   scales and in first- and third-person views.
4. Verify rejection and timeout paths leave no focused or orphan companion
   dialog, then inspect client and server logs.
5. Repeat the matrix with Apprentice alone and with the current 48-mod pack.
6. Tag the candidate as released only after the runtime matrix passes.
7. Only then begin the 2.2.5 registry and compatibility foundation.

Static validation demonstrates source and asset consistency, not runtime
acceptance. The game client, dedicated server and full mod pack remain the
release authority.

## Documentation and research sources

- [Vintage Story Modding category](https://wiki.vintagestory.at/Category:Modding)
- [Vintage Story API documentation](https://apidocs.vintagestory.at/)
- The implementation references and lessons are collected at the end of
  [GRANDMASTER-CONTENT-DESIGN.md](GRANDMASTER-CONTENT-DESIGN.md).
- The exact active-mod findings and test matrix are in
  [SERVER-MOD-COMPATIBILITY.md](SERVER-MOD-COMPATIBILITY.md).

The wiki guides architecture, but some pages target older APIs. Verify all
method signatures against the 1.22.3 assemblies before implementation.
