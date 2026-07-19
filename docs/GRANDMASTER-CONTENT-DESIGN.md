# Apprentice Milestone Design: Stability, Grandmaster Content, Poison and Danger Regions

Status: 2.2.4-a through 2.7.0 implemented in the 2.7.0 source candidate;
target-runtime and dedicated-server acceptance remain release gates. The
sections below are both the design contract and the acceptance baseline; a
feature is releasable only when its listed runtime tests pass.

Target game version: Vintage Story 1.22.3.

Related documents:

- [Current developer handoff](DEVELOPER-HANDOFF-2.2.4.md)
- [Server-mod compatibility audit](SERVER-MOD-COMPATIBILITY.md)
- [Release checklist](RELEASE_CHECKLIST.md)
- [Level table](LEVEL-TABLE-1-100.csv)

## Release policy

The current visual and persistence defects must be fixed before Grandmaster
content is added. Do not combine a high-risk dialog, persistence or model fix
with new gameplay systems in one release. Each milestone has an explicit exit
gate; a milestone is not complete until its tests pass on a dedicated server.

The suffixes `-a`, `-b`, and so on are ordered hotfix builds of version 2.2.4.
They do not change save-data ownership or asset IDs unless a migration is
defined in the same milestone.

| Version | Purpose | Release blocker |
| --- | --- | --- |
| 2.2.4-a | Close the character dialog correctly | No invisible dialog may retain mouse/keyboard focus. |
| 2.2.4-b | Persist an exact race and appearance snapshot | Two consecutive reconnects must preserve every selection. |
| 2.2.4-c | Repair tab focus and responsive race layout | One click selects either tab; no trait text overlaps controls. |
| 2.2.4-d | Replace horn-color dropdown and repair its placement | Five swatches render in the Skin & Voice tab without overlap. |
| 2.2.4-e | Remove green facial points/strips | No race model contains the reported green face artifacts. |
| 2.2.5 | Compatibility and data-registry foundation | Optional server mods remain optional; invalid data fails safely. |
| 2.3.0 | Grandmaster item vertical slices | All 18 existing unlock outputs resolve to usable content. |
| 2.3.1 | Hidden profession and heritage discoveries | Unlock evaluation is server-authoritative and migration-safe. |
| 2.4.0 | Tier 6 Starsteel | Cementation survives reload and cannot duplicate output. |
| 2.4.1 | Tier 7 Aethersteel | Tier progression and anvil requirements are enforced server-side. |
| 2.5.0 | Arrow-only liquid poison | Coating and damage-over-time are authoritative and persist correctly. |
| 2.5.1 | Poison content and balancing | Fictional ingredients, recipes and feedback form a complete loop. |
| 2.6.0 | Danger-tier engine | Spawn-based tier scaling is deterministic and stored once per entity. |
| 2.6.1 | Toggleable map heatmap | The overlay is cached, optional and compatible with other map layers. |
| 2.6.2 | Rare ecology and bonus drops | Only Apprentice-owned resources and bonus tables scale with danger. |
| 2.7.0 | Integration, balance and release hardening | Full server matrix, migrations and performance budget pass. |

## 2.2.4-a — Close the configuration dialog properly

### Defect

After **Confirm Skin**, the window is no longer visible but remains the active
input target. Controls behind the former dialog, including the Disconnect
button in the center of the screen, do not respond.

`BeforeSkinConfirmNext` currently queues a reflected `OnConfirm` call. That can
hide or recompose the stock dialog without running the complete public close
lifecycle. Companion dialogs may also remain registered.

### Required implementation

1. Keep confirmation deferred until the next client frame so the mouse handler
   is not executing against a composer that is being disposed.
2. Save/send the final immutable customization snapshot before requesting the
   close. When 2.2.4-b adds a server acknowledgement, close only after a
   positive acknowledgement or after an explicit timeout message; never report
   success for a rejected snapshot.
3. Use the public `GuiDialog.TryClose()` lifecycle on the character dialog.
   Do not treat reflected `OnConfirm`, composer disposal, visibility changes or
   clearing one reference as equivalent to closing the dialog.
4. Make cleanup idempotent. `AfterDialogClosed` must:
   - close and dispose the race-options companion;
   - close and dispose the horn-color companion;
   - clear `activeCharacterDialog` and `pendingSkinConfirmDialog` only when they
     reference the closing instance;
   - remove the instance from `ConfirmedRaceDialogs`;
   - restore the natural skin palette;
   - leave no queued task capable of reopening or recomposing that instance.
5. If `TryClose()` fails, log one actionable warning and run a narrowly scoped
   fallback cleanup. Do not swallow the failure silently.
6. Protect the queued callback with both instance identity and an idempotent
   `closeRequested` state so double-clicking Confirm Skin cannot close a newer
   dialog or send duplicate packets.

### Acceptance tests

- Confirm Skin closes the dialog once and `IsOpened()` becomes false.
- The center-screen Disconnect, inventory and handbook controls respond on the
  first click immediately after confirmation.
- Mouse capture, keyboard focus and normal camera control are restored.
- Reopening the character configuration creates one clean dialog.
- Double-clicking Confirm Skin sends at most one final save request.
- Escape, X, normal confirmation and server rejection all leave no companion
  composer in the client input stack.

## 2.2.4-b — Exact race, subrace and appearance persistence

### Defect

Race and Skin & Voice choices return to subrace defaults after leaving and
rejoining the server. The current flow persists multiple fields but subrace
default application and delayed class/model restoration can run after the
saved appearance has been restored.

### Required packet and storage contract

Introduce a versioned, immutable `RaceCustomizationSnapshot` (or extend
`RaceBodyPacket` with the same contract). It must contain:

- schema version and monotonically increasing client request ID;
- base race/class code;
- subrace code;
- profession code;
- height and thickness values, clamped to the server range;
- horn style, tooth style and horn-color code where supported;
- exact Skin & Voice part codes:
  `baseskin`, `eyecolor`, `underwear`, `voicetype`, `voicepitch`, `hairbase`,
  `hairextra`, `facialexpression`, `mustache`, `beard`, and `haircolor`;
- no client-calculated trait or stat totals. The server recalculates those.

Store one serialized snapshot under an Apprentice-owned player mod-data key.
Do not independently overwrite fragments in different callbacks and assume
their final order is stable.

### Required save sequence

1. The client reads controls only after the Skin & Voice selections have been
   applied to the player entity.
2. The client sends one snapshot with a request ID.
3. The server validates every code against the selected race/subrace allow-list
   and validates numeric ranges.
4. The server persists the snapshot atomically, marks the relevant attributes
   dirty, applies it, and replies with `RaceSaveResultPacket` containing the
   request ID and success/error information.
5. The client closes the dialog only for the matching successful request.

Invalid or removed asset codes must not crash login. Migrate them to the first
valid option for that specific part, record the corrected snapshot, and log the
old and replacement codes once.

### Required restore sequence

Perform restoration under a `restoreInProgress` guard:

1. Read and migrate the persisted snapshot.
2. Restore base class/race, subrace, profession and body values.
3. Restore all exact appearance part codes.
4. Restore horn, teeth and horn color only for races supporting those parts;
   explicitly clear stale hidden parts for other races.
5. Refresh hidden race elements and model shape.
6. Recalculate body/race traits and stats on the server.
7. Mark attributes dirty once, then release `restoreInProgress`.

Any postfix or delayed callback that normally applies subclass defaults must
return without changing appearance while restoration is active. Subrace
defaults are allowed only when that subrace is selected for the first time and
there is no valid saved choice. They are never reapplied simply because a
player joined, changed dimension or reloaded a class model.

If delayed retries remain necessary because Vintage Story builds the player
model asynchronously, every retry must reapply the same immutable snapshot and
must not rebuild a packet from partially restored live state.

### Migration and compatibility

- Read the legacy body packet once and convert it to the new schema.
- Preserve unknown future fields when practical or reject a newer schema with
  a precise log instead of silently resetting the player.
- After ChangeClass completes an external class reset, refresh Apprentice's
  snapshot from the confirmed player choice. Apprentice's internal restore must
  never look like a new ChangeClass choice.
- Do not create a dependency on ChangeClass or any other server mod.

### Acceptance matrix

For every base race and subrace:

1. Select non-default race, subrace, profession, sliders and every available
   appearance control.
2. Confirm, disconnect, reconnect, verify all fields and the rendered model.
3. Save and stop the server, restart it, reconnect, verify again.
4. Repeat the disconnect/reconnect a second time to catch a default value that
   was written during the first restore.
5. Test two players with different selections at the same time.
6. Test a dedicated server, singleplayer, death/respawn and ChangeClass when
   installed.

No selection may change unless it is invalid under the current data files and
the migration log names the replacement.

## 2.2.4-c — One-click tabs and non-overlapping trait layout

### Tab focus defect

The tab array is deliberately displayed as Race then Skin & Voice, while the
stock dialog stores `GuiTab.DataInt` values in the opposite semantic order.
The current code repairs the visual selection after the stock click handler,
so a click can first operate on the wrong tab and require a second click.

### Required tab implementation

1. Treat displayed array index and `GuiTab.DataInt` as separate types even
   though both are integers.
2. At the click boundary, translate the clicked array index to
   `tabs[index].DataInt` exactly once before the stock state transition, or own
   the whole tab transition and skip the stock transition.
3. Store only the semantic tab ID in `curTab`.
4. Compose the selected page once, set the visual index without triggering a
   second callback, and update companion dialogs after composition.
5. The race-first guard must test the semantic Skin & Voice ID, not array index
   zero by assumption.
6. Remove any post-click correction that causes a second state transition.

### Trait/layout defect

`RaceOptionsDialog` starts controls at a fixed `optionsTop = 260`. Wrapped
descriptions vary by race, subrace, body statistics, localization and UI scale,
so trait text can overlap **Body & Race Options**.

### Required responsive layout

1. Compose the rich-text trait block first.
2. Read its calculated/rendered bottom bound after `SetNewText` and layout.
3. Set the options top to `max(minimumTop, traitBottom + verticalGap)`.
4. Reserve the confirm-button area before positioning controls.
5. If the remaining area is too small, apply fallbacks in this order:
   - use the existing concise trait text variants;
   - reduce only the trait detail font in small bounded steps;
   - place the trait block in a clipped/scrollable region;
   - never overlap or move controls outside the dialog.
6. Recalculate on race, subrace, body value, language, window-size and GUI-scale
   changes. Do not keep a companion dialog at a stale fixed offset.
7. Keep title, positive trait, subrace trait, neutral body stats and negative
   trait visually distinct even when the details wrap.

### Acceptance tests

- A single click switches Race to Skin & Voice and back.
- The active-tab styling always matches the visible content.
- The race-first warning appears only before race confirmation.
- Test every race and subrace at 80%, 100%, 125% and 150% GUI scale and the
  smallest supported window.
- No trait glyph, text line, Body & Race Options title, selector, slider or
  Confirm Race button intersects another element.
- Long translations stay readable through wrapping or scrolling.

## 2.2.4-d — Horn-color swatches in Skin & Voice

### Defects

Horn color is a dropdown rather than a color selector and the companion control
uses fixed coordinates, placing it over unrelated Skin & Voice controls.

### Required implementation

1. Remove the horn-color dropdown.
2. Add one color-swatch row with the five existing texture-backed choices:

   | Code | Label |
   | --- | --- |
   | `dark-gray` | Dark gray |
   | `light-gray` | Light gray |
   | `white` | White |
   | `light-pink` | Light pink |
   | `yellowish-white` | Yellowish white |

3. Render each actual horn texture/color in a square swatch, with a visible
   selected border and a localized tooltip. Do not rely on color alone for the
   selected state.
4. Place the row inside the Skin & Voice right column below Hair Color, using
   bounds derived from the composed stock controls. Do not use fixed screen
   offsets such as `optionsLeft = 500` or `optionsTop = 390`.
5. If a companion dialog remains necessary, its bounds must contain only the
   swatch row and be recomputed from the parent dialog on scale/resize.
6. Show the row only for a race with visible horn options. Hide and release its
   input bounds for every other race.
7. Persist the swatch code through the 2.2.4-b snapshot. Changing the swatch
   updates the preview immediately and only the horn texture.

### Acceptance tests

- Tiefling and Dragonborn display five swatches in the intended right column.
- Other races have no horn-color label, swatches or invisible input region.
- All five selections survive two reconnects.
- Hair, beard and horn controls never overlap at any supported GUI scale.
- Keyboard/controller navigation and tooltips identify every color.

## 2.2.4-e — Remove green facial points and strips

### Defect

Screenshots 153–156 show bright green marks across the lower face of Gnome,
Halfling and Dwarf models in both race preview and Skin & Voice preview. The
marks are geometry/UV artifacts, not intended facial decoration.

The current shapes contain small custom face elements such as Gnome
`pointL`/`pointR` and several nose/cheek elements that sample `#seraph` or other
atlases. A skin or eye palette can therefore expose an unrelated green atlas
region on those cubes.

### Required asset repair

1. In a debug build, disable suspect face elements one named group at a time to
   identify every source; do not hide the problem with a color override.
2. Delete purely decorative point/strip geometry that has no required facial
   function.
3. For required nose, cheek or brow geometry, use a race-owned texture key with
   the correct `textureSizes` entry and UV coordinates inside that texture.
4. Ensure eyebrows and beards use the chosen hair color; skin geometry must use
   the chosen skin palette. Never sample the generic Seraph atlas accidentally.
5. Run `tools/validate_assets.py`, then visually inspect because structural
   validation cannot prove that a UV points at the intended pixels.
6. Check front, both profiles, animation poses and dressed/undressed previews.

### Acceptance tests

- Gnome, Halfling and every Dwarf subrace have no green point or strip with any
  allowed skin, eye, hair and expression combination.
- The audit is repeated for every other race so the fix is not limited to the
  three screenshots.
- Eyebrows, moustaches and beards match Hair Color where the lore rules require
  it.
- No required eye, nose, mouth, tusk, tooth or horn element disappears.

## 2.2.5 — Compatibility and registry foundation

Implement future systems without hard dependencies on the server's other mods.
The complete 48-mod inventory, the two user-excluded Aldi packages, interaction
risks and the dependency-free rules are in
[SERVER-MOD-COMPATIBILITY.md](SERVER-MOD-COMPATIBILITY.md).

Required foundation:

- Apprentice metadata has no dependency on any audited gameplay mod.
- No foreign DLL is referenced and no private foreign class/method is called.
- Optional integrations use mod-ID/asset checks or JSON `dependsOn` and fail
  closed when the external asset is absent.
- Network channels, watched attributes, map layers and asset IDs use unique
  Apprentice namespaces.
- Data registries validate at startup. A bad definition disables only that
  definition and logs its file, code and reason.
- Server systems avoid client rendering classes; client systems never decide
  damage, drops, unlocks or furnace output.

## 2.3.0 — Grandmaster item vertical slices

The Race Traits skill-tree tab already reads synchronized race data and remains
read-only. Race customization remains the single writer.

Every existing `UnlockRecipe` output must become real content. Implement one
complete vertical slice at a time: assets, language, recipe, authorization,
behavior, persistence where needed and multiplayer test.

| Profession | Existing unlock output | Required feature |
| --- | --- | --- |
| Miner | `apprentice:mastersurveykit` | Durable survey kit that condenses prospecting samples into a vein report. |
| Woodworker | `apprentice:joinersbench` | Station for precision beams, fitted frames and replaceable handles. |
| Farmer | `apprentice:graftingkit` | Durable grafting tool for fruit-tree and high-tier seed work. |
| Builder | `apprentice:architectstable` | Station for arches, structural templates and decorative variants. |
| Blacksmith | `apprentice:masterforgeplans` | Durable authorization tool for advanced furnace charges and metal patterns. |
| Cook | `apprentice:mastercookbook` | Reusable recipe tool for complete meals and preserved travel rations. |
| Potter | `apprentice:masterkiln` | Controller for refractory liners, crucibles and furnace repair parts. |
| Leatherworker | `apprentice:saddlersbench` | Station for harnesses, reinforced packs and fitted armor straps. |
| Tailor | `apprentice:sewingkit` | Durable tool for liners, padded clothing and repair patches. |
| Hunter | `apprentice:advancedtrapkit` | Recoverable visible traps with server-owned capture and cooldown. |
| Warrior | `apprentice:masterweaponpatterns` | Durable pattern folio for advanced melee weapon heads. |
| Ranger | `apprentice:compositebow` | Repairable composite bow with replaceable string and limbs. |
| Fisher | `apprentice:masterfishingrod` | Repairable rod with line, hook and lure variants. |
| Animal husbandry | `apprentice:veterinarykit` | Durable diagnosis/treatment tool using consumable medicines. |
| Beekeeper | `apprentice:framehive` | Hive block entity with removable frames and persistent colony state. |
| Spearman | `apprentice:atlatl` | Launcher for dedicated darts; ordinary spears remain useful. |
| Shield | `apprentice:towershield` | Strong frontal defense with explicit movement and stamina costs. |
| Tank | `apprentice:armorpaddingkit` | Fitting tool for replaceable, consumable armor liners. |

### Authorization contract

1. `UnlockRecipe` authorizes the Grandmaster artifact output on the server.
2. Downstream recipes consume or require that artifact as a durable recipe
   tool/station component.
3. Skill-bound stations validate the interacting player server-side.
4. Packet manipulation cannot craft a locked output.
5. Whether a traded artifact grants downstream access is an explicit server
   configuration, never an accidental client-only consequence.

### Per-slice completion definition

- collectible/block resolves without missing assets;
- icon, shape, texture, language and handbook grouping are complete;
- recipe consumes the intended ingredients and tool durability;
- unlock checks are server-side;
- inventory/progress state survives reload where applicable;
- two players cannot duplicate output from the same station;
- optional mods absent/present do not change core behavior unexpectedly.

## 2.3.1 — Hidden profession and heritage discoveries

### Multi-profession Grandmaster discoveries

| Discovery | Required Grandmasters | Identity |
| --- | --- | --- |
| Master Smelter | Miner + Blacksmith + Potter | Faster advanced furnace cycles and lower refractory wear; enables Starsteel. |
| Siegewright | Builder + Blacksmith + Woodworker | Defensive structures and durable mechanisms. |
| Field Chirurgeon | Cook + Tailor + Leatherworker + Animal Husbandry | Improved crafted bandages, splints and treatment efficiency. |
| River Warden | Fisher + Hunter + Ranger | Aquatic tracking, durable tackle and wet-environment ranged handling. |
| Hive Alchemist | Beekeeper + Cook + Potter | Wax-sealed vessels, medicinal honey and efficient hive products. |
| Caravan Master | Animal Husbandry + Leatherworker + Builder | Reinforced packs, harness durability and safer transport. |
| Trail Engineer | Ranger + Miner + Builder | Survey markers, climbing aids and reduced remote tool wear. |
| Quartermaster | Tank + Shield + Tailor + Leatherworker | Replaceable armor liners and lower defensive-equipment wear. |

### Profession plus race/subrace discoveries

| Discovery | Profession | Heritage | Supporting mastery | Identity |
| --- | --- | --- | --- | --- |
| Runeforger | Blacksmith | Mountain Dwarf or Duergar | Miner | Refractory/anvil specialist. |
| Nightstalker | Ranger | Drow | Hunter | Cooldown-limited low-light tracking and first-shot bonus. |
| Grove Warden | Farmer | Wood Elf | Ranger | Sapling, graft and forage specialist. |
| Gearwright | Woodworker | Rock Gnome | Builder | Precision mechanisms and low-waste joinery. |
| Deep Artificer | Miner | Deep Gnome | Blacksmith | Underground maintenance and rusty-gear recovery. |
| Scale Smith | Blacksmith | Metallic Dragonborn | Tank | Plate and scale armor specialist. |
| Prism Warden | Potter | Gem Dragonborn | Shield | Refractory ceramics and gem-themed defensive fittings. |
| Ashbound Smith | Blacksmith | Infernal Tiefling | Potter | Furnace control and heat-resistant components, not fire immunity. |
| Glacier Sentinel | Tank | Frost Giantkin | Shield | Cold-region defense and lower armor movement penalty. |
| Iron Reaver | Warrior | Orog | Miner | Heavy mining tools gain controlled combat utility. |
| Trailblazer | Fisher | Lightfoot Halfling | Ranger | Travel tackle, low detection and recoverable lures. |
| Adaptive Master | Any | Human | Any two other Grandmasters | One small bonus chosen from mastered disciplines. |

Extend `HiddenClassDefinition` with data fields rather than hardcoding names:

```text
RequiredClasses       all listed capstones must be unlocked
RequiredProfession    optional selected profession code
AllowedRaces          optional base-race allow-list
AllowedSubraces       optional subrace allow-list
Effects               existing effect definitions
SchemaVersion         migration discriminator
```

Evaluation occurs server-side after capstone purchase and after validated race
state is restored/confirmed. Store the resulting discovery permanently in the
existing watched-attribute tree and mark the exact path dirty. Cosmetic changes
must not revoke a legitimate discovery. The Hidden tab conceals undiscovered
names/requirements and reveals the cause only after discovery.

## 2.4.0 — Tier 6 Starsteel cementation

This is a sealed cementation-furnace process, not a crucible alloy. Use a custom
Apprentice charge registry and narrowly scoped processor instead of a broad
patch to every metal or furnace from other mods.

One exact 16-ingot charge:

| Input | Count |
| --- | ---: |
| Steel ingot | 8 |
| Meteoric iron ingot | 4 |
| Nickel ingot | 2 |
| Silver ingot | 1 |
| Gold ingot | 1 |

Requirements and process:

1. Require Master Smelter, Master Forge Plans and the Grandmaster refractory
   components when the operator seals/starts the charge.
2. Compare the full unordered input multiset; reject partial quantities and
   substitutions without consuming anything.
3. Burn for a configurable starting target of 7.5 in-game days.
4. Produce exactly 16 `apprentice:blister-starsteel` work items.
5. Smith each blister into an ingot using the intended anvil rule.

Initial balance targets relative to steel, subject to playtesting:

- tool tier 6;
- durability `1.35x`;
- mining speed `1.12x`;
- weapon damage `1.10x`;
- modest armor improvement with meaningful weight.

Persist charge recipe ID/schema, exact inventory, sealed state, start time,
progress, fuel/refractory state, operator authorization and `outputClaimed`.
Output production must be idempotent after save/reload and chunk unload.

## 2.4.1 — Tier 7 Aethersteel cementation

One exact 16-ingot charge:

| Input | Count |
| --- | ---: |
| Starsteel ingot | 6 |
| Meteoric iron ingot | 3 |
| Nickel ingot | 2 |
| Copper ingot | 1 |
| Tin ingot | 1 |
| Bismuth ingot | 1 |
| Silver ingot | 1 |
| Gold ingot | 1 |

Requirements and process:

1. Require the Tier 6 Starsteel anvil, upgraded refractory liner and Miner,
   Blacksmith and Potter Grandmaster progression.
2. Burn for a configurable starting target of 10 in-game days with higher fuel
   and refractory cost than Starsteel.
3. Produce 16 `apprentice:blister-aethersteel` work items.
4. Smith them only on a Starsteel anvil.

Initial targets relative to Starsteel: tier 7, `1.25x` durability, `1.10x`
mining speed and `1.08x` weapon damage. Never grant instant mining, infinite
durability or blanket immunity.

For both metals, add one coordinated asset unit: metal/world property, ingot
and work-item variants, supported plates/chains/scales, textures, shapes,
language, workability, smithing recipes, supported tools/armor/anvil, handbook
groups and validated charge data. Missing collectibles disable the affected
definition with a precise log; they do not crash world loading.

## 2.5.0 — Server-authoritative liquid poison for arrows only

The uploaded `Apprentice_Liquid_Poison_System_Design` is the conceptual base,
but the current product decision narrows delivery to arrows. Do not implement
poisoned melee weapons, traps, food or drink.

### Safety and content boundary

- Ingredients and processing are fictional game mechanics, not real-world
  toxicology instructions.
- Core ingredients are Apprentice-owned poisonous berries and mushrooms.
- Wildcraft, Herbarium, Expanded Foods, A Culinary Artillery and Grapes and
  Wine may coexist but are not dependencies and are not required ingredients.
- Poison liquids have no nutrition/meal behavior and cannot enter normal food
  recipes.

### First technical spike

Before creating all content, verify whether namespaced item-stack attributes
survive:

- inventory stack merging/splitting;
- arrow loading and bow use;
- projectile entity creation;
- pickup/recovery;
- save/reload;
- client/server serialization.

If the coating attribute survives every test, store a versioned namespaced
payload on the arrow stack/projectile. If it does not, use dedicated poisoned
arrow variants or a custom arrow collectible/projectile. Do not build production
content on an unverified attribute path.

### Authoritative flow

1. A normal barrel recipe converts fictional berries/mushrooms plus liquid into
   an Apprentice poison liquid.
2. A server-validated coating action consumes a configured liquid amount and
   coats only supported arrow stacks.
3. Projectile spawn copies the validated coating payload.
4. On a confirmed server-side hit, apply or update the poison effect.
5. A server tick listener processes effects once per second; no per-frame
   entity scan is allowed.
6. The client displays particles/status information only from synchronized
   state and never decides damage.

Stacking rule: a stronger effect replaces a weaker one; equal strength refreshes
duration up to a cap; a weaker effect is ignored. Damage ownership, immunity
tags, PvP configuration, death attribution and offline/reload behavior must be
defined before release.

Suggested first playtest values, not final balance:

| Tier | Damage/second | Duration | Total |
| --- | ---: | ---: | ---: |
| Mild | 0.2 HP | 8 s | 1.6 HP |
| Standard | 0.3 HP | 12 s | 3.6 HP |
| Potent | 0.4 HP | 16 s | 6.4 HP |
| Grandmaster | 0.5 HP | 20 s | 10 HP |

## 2.5.1 — Poison content, feedback and balance

Add the complete fictional ingredient loop, icons/models, barrel recipes,
handbook information, arrow appearance, effect feedback and server config.
Grandmaster poison may require Hive Alchemist or another documented discovery,
but basic poison must be testable without unrelated optional mods.

Acceptance tests:

- unsupported arrows and every melee weapon reject coating without item loss;
- exact liquid amount is consumed once;
- save/reload and projectile pickup preserve or intentionally clear coating
  according to the documented rule;
- repeated hits follow the replacement/refresh rules;
- protected/friendly/PvP-disabled targets obey server configuration;
- the DoT cannot run twice after reconnect or entity reload;
- external food mods cannot treat poison as a meal ingredient.

## 2.6.0 — Spawn-centered danger-tier engine

The world/server spawn is the permanent center. Player beds and personal spawn
points never move the heatmap. On first world initialization, persist the
anchor and configuration version in save-game mod data; later changes require
an explicit administrator migration command.

Use horizontal distance only:

```text
tier = clamp(ceil((distanceFromAnchor - baseRadius) / ringWidth), 0, 10)
```

Starting defaults for playtesting: `baseRadius = 4000` blocks and
`ringWidth = 2000` blocks. Both are server configuration, not balance promises.

### Tier ownership and entity rules

- The server calculates and stores an entity's tier once when an eligible
  entity successfully spawns. Crossing a ring later does not rescale it.
- Scale only hostile/wild creatures allowed by explicit entity tags/config.
- Exclude players, traders, villagers, tamed animals, livestock, mounts,
  vehicles, corpses and scripted/story entities by default.
- Store original values, applied tier and schema in namespaced attributes so
  chunk reload cannot multiply the multiplier again.
- Do not override `OnTrySpawnEntity`; apply scaling only after another system,
  including Safe House, allowed the spawn.

Suggested first tuning, to be replaced by measured playtests:

```text
healthMultiplier = 1 + 0.35 * tier
damageMultiplier = 1 + 0.18 * tier
```

Additional speed, armor, perception or behavior changes should be sparse,
bounded and data-driven; raising every stat each tier creates unreadable and
unfair encounters.

## 2.6.1 — Toggleable heatmap map layer

Use one uniquely named Apprentice map layer. It must not patch waypoint,
prospecting or ore-map layers. The client receives the anchor, ring dimensions,
enabled flag and palette, then draws/caches the overlay. The server remains the
authority for actual entity/resource tiers.

| Tier | Meaning | Color |
| ---: | --- | --- |
| 0 | Base game region | `#165A2A` dark green |
| 1 | Danger 1 | `#62B44B` light green |
| 2 | Danger 2 | `#3F8F3B` green |
| 3 | Danger 3 | `#2F6F2F` deeper green |
| 4 | Danger 4 | `#B7C83D` yellow-green |
| 5 | Danger 5 | `#E5D744` yellow |
| 6 | Danger 6 | `#F3B33D` amber |
| 7 | Danger 7 | `#ED7A2F` orange |
| 8 | Danger 8 | `#D94A2D` red-orange |
| 9 | Danger 9 | `#B3232E` red |
| 10 | Maximum danger | `#650F24` dark red |

The map menu gets a persistent per-client toggle. Cache geometry/texture by
anchor, ring config, zoom bucket and map bounds; do not rebuild the whole
overlay every frame. Include a legend and enough non-color cues for users who
cannot distinguish the palette.

## 2.6.2 — Rare ecology and bonus materials

Higher danger may increase only Apprentice-owned rare plants, mushrooms,
berries, ore and bonus loot definitions. Do not multiply arbitrary base-game or
other-mod drops; that would destabilize server balance and create dependencies.

- Worldgen placement is deterministic per world seed, chunk and danger tier.
- Worldgen code is thread-safe and does not access client APIs.
- Existing chunks do not retro-generate by default. If retro-generation is
  later added, it needs an administrator command, processed-chunk marker,
  budget and backup warning.
- Strong creature bonus materials come from a separate Apprentice loot table
  evaluated once on server-authoritative death.
- Tier and eligibility are stored so moving or reloading an entity cannot reroll
  the reward multiplier.
- Every rare material has a use before it is enabled in world generation.

## 2.7.0 — Integration and release hardening

### Cross-system tests

- New and existing worlds load with no missing-asset errors.
- 2.2.4 customization snapshots migrate and survive reconnects.
- Race Traits reflects race, subrace, profession and body values after relog.
- Grandmaster output recipes cannot be bypassed with crafted packets.
- Cementation inventories/progress survive restart; concurrent players cannot
  duplicate output.
- Poison is arrow-only, server-owned and compatible with optional food mods.
- Danger scaling applies once, respects protected spawns and excludes NPCs,
  livestock and vehicles.
- Heatmap coexists with Auto Map Markers, ProspectTogether, Waypoint Beacon and
  Waypoint Together Reborn.
- Dedicated-server startup never loads client-only GUI/render classes.
- Profile hot paths have no full-world or full-entity per-frame scans.

### Performance budgets

- Poison: one scheduled server tick over active poison records, removing
  expired/dead entities immediately.
- Danger: calculate tier at spawn and death; no continuous distance polling.
- Map: cached overlay invalidated only by configuration/anchor/map changes.
- Worldgen: deterministic chunk-local work with bounded candidate counts.
- Data: load and validate definitions once at startup.

### Packaging and migration

- Keep all IDs namespaced under `apprentice`.
- Add schema versions to customization, discovery, poison, furnace and danger
  persistence records.
- Every migration is idempotent and logs a summary once.
- Do not delete legacy keys until at least one successful release has read and
  rewritten them.
- Update `modinfo.json`, changelog and release checklist together.
- Run `python3 tools/validate_assets.py` and the target Vintage Story/Cake build
  before packaging.

## Documentation sources and lessons learned

The Vintage Story [Modding category](https://wiki.vintagestory.at/Category:Modding)
was used as the source index. Its `Modding:` subpages were inventoried, with
implementation-relevant groups checked in detail. Some wiki pages describe
older APIs, so signatures must be verified against the target 1.22.3 assemblies
and the current [API documentation](https://apidocs.vintagestory.at/) before
coding.

Primary implementation references:

- [Server/client considerations](https://wiki.vintagestory.at/Modding:Server-Client_Considerations): gameplay authority belongs on the server.
- [Network API](https://wiki.vintagestory.at/Modding:Network_API): packets synchronize requests/results, not trusted outcomes.
- [TreeAttribute](https://wiki.vintagestory.at/Modding:TreeAttribute) and [entity instance attributes](https://wiki.vintagestory.at/Modding:Entity_Instance_Attributes): persistent/synchronized state needs namespaced paths and explicit dirty marking.
- [SaveGame ModData](https://wiki.vintagestory.at/Modding:SaveGame_ModData): the heatmap anchor and world-level schema belong in save data.
- [JSON Patch reference](https://wiki.vintagestory.at/Modding:JSON_Patch_Reference): `dependsOn` supports optional asset patches without mandatory dependencies.
- [Barrel recipes](https://wiki.vintagestory.at/Modding:Asset_Type_-_Recipes_(Barrel)): ordinary fictional poison processing should stay data-driven.
- [Item JSON properties](https://wiki.vintagestory.at/Modding:Item_Json_Properties/en) and [Block JSON properties](https://wiki.vintagestory.at/Modding:Block_Json_Properties): use JSON for static content and container/interaction metadata.
- [Block entities](https://wiki.vintagestory.at/Modding:Block_Entity): timed cementation stations need persistent server state.
- [Alloy recipes](https://wiki.vintagestory.at/Modding:Asset_Type_-_Recipes_(Alloy)) and [smithing recipes](https://wiki.vintagestory.at/Modding:Asset_Type_-_Recipes_(Smithing)): crucible alloys, cementation and smithing are separate stages.
- [World generation concept](https://wiki.vintagestory.at/Modding:WorldGen_Concept), [worldgen API](https://wiki.vintagestory.at/Modding:WorldGen_API) and [simple worldgen](https://wiki.vintagestory.at/Modding:Simple_WorldGen): rare resources must be deterministic, chunk-local and thread-safe.
- [Modding API updates](https://wiki.vintagestory.at/Modding:Modding_API_Updates): review breaking API changes before each target-game update.
- [Steel making](https://wiki.vintagestory.at/Steel_making), [metal](https://wiki.vintagestory.at/Metal) and [anvil](https://wiki.vintagestory.at/Anvil): advanced metals must follow coherent furnace, work-item and anvil progression.

The uploaded server archive was inspected locally to understand active mod
behavior and collision points. It is not copied, referenced or required by
Apprentice. The two Aldi's Classes packages are explicitly excluded because the
user removed them from the server. The reusable lesson is to copy architectural
ideas only: scoped events, namespaced data, conditional assets and cached map
layers—not another mod's private code or binaries.
