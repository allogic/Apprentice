# Apprentice 2.2.4 Server-Mod Compatibility Audit

Status: design and test baseline for the current 2.2.4 source and all future
content milestones.

## Scope and audit method

The supplied server archive contained 50 nested mod packages. Two packages are
not part of the server anymore and are intentionally excluded:

- Aldi's Classes 2.2.2;
- Aldi's Classes Homesteader 0.2.2.

That leaves 48 active compatibility targets. The excluded packages must not be
added to Apprentice metadata, compatibility assets or tests.

The audit safely extracted every nested package and examined:

- `modinfo.json` identity, version, side and declared dependencies;
- asset domains, item/block/entity definitions and recipe folders;
- JSON patch targets and optional-dependency conditions;
- DLL and PDB symbols where source was not included;
- map, network, spawn, inventory, world-generation, liquid and metal surfaces;
- the single included C# source file in Explosives.

The archive contained 12,237 files and 44 mod DLLs. Eight DLLs are bundled
Watersheds libraries rather than separate mods. A manifest description is not
treated as proof of an implementation detail; uncertain findings are marked as
inferences from assets or assembly symbols.

## Compatibility contract

Apprentice remains usable when none of the 48 mods are installed.

1. Do not add an active server mod to `modinfo.json` dependencies.
2. Do not add references to an active mod DLL in `Apprentice.csproj`.
3. Keep Apprentice-owned content in the `apprentice` asset domain.
4. Use vanilla API contracts, asset attributes and event interfaces.
5. Query optional mod IDs and collectible codes at runtime. Missing optional
   content disables only that integration.
6. Use JSON patch `dependsOn` for an optional asset patch. Never publish an
   unconditional patch targeting a collectible owned by another mod.
7. Never call another mod's private methods or depend on names discovered only
   through reflection.
8. Do not replace another mod's map layer, entity behavior, inventory class,
   spawn policy, world generator or liquid class.
9. Log one concise compatibility warning when an optional target has changed;
   do not crash world loading.
10. Validate Apprentice alone, with each shared-system mod, and with the full
    48-mod pack on a separated client and dedicated server.

## Active mod inventory and decisions

| Mod | What the audited package does | Apprentice contact point | Compatibility decision |
| --- | --- | --- | --- |
| A Culinary Artillery 2.0.0-dev.21 | Food-tool and block framework used by food mods. | Liquids, containers, food tools and recipes. | Poison liquids use Apprentice items and vanilla liquid properties. No ACA classes or recipe outputs are required. |
| Animal Hitch 1.3.0 | Tethers animals to fence posts; an animal can break free. | Entity classification for danger scaling. | Tethered, owned, domesticated and livestock entities are excluded from hostile scaling by default. Never alter hitch behavior. |
| Animal Trader 1.2.1 | Adds a livestock trader that sells young animals and buys animal products. | NPC and livestock classification. | Exclude traders, villagers and traded/domesticated animals from danger scaling and bonus hostile drops. |
| Attribute Rendering Library 3.1.5 | Provides attribute-driven render variants. | Player appearance and attribute rendering. | Apprentice keeps its existing skin-part and watched-attribute implementation. Do not require ARL or register its classes. |
| Auto Map Markers 5.0.3 | Adds configurable waypoints after object interactions. | World-map UI and map-layer collection. | Register one uniquely named Apprentice heatmap layer. Do not patch marker dialogs, waypoint methods or its settings. |
| A Wearable Light 1.2.2 | Adds wearable light sources. | Player equipment only. | No integration is needed. Poison handling and race traits must not inspect or rewrite unrelated wearables. |
| BedSpawn 1.7.1 | Changes a player's return point after sleeping. | Spawn-coordinate semantics. | The heatmap anchor is the persisted server/world spawn, never a player's bed or return point. |
| BetterJungles 0.1.9 | Patches jungle trees, weather and some mob/crop/plant spawning. | Worldgen and entity spawn conditions. | Compute danger from coordinates and add only Apprentice resources. Do not patch its plants, weather or spawn values. |
| BetterRuins 0.6.3 | Adds many surface and underground worldgen structures. | Structure/worldgen passes. | Do not alter structure placement. Apprentice resources must tolerate occupied blocks and skip invalid placements. |
| Blush and Bins 1.3.3 | Adds immersive storage and display blocks; uses ARL. | Grandmaster station inventories and display attributes. | Use standard Vintage Story inventories and item attributes. Do not register Blush and Bins classes or assume its displays exist. |
| Butchering 1.13.5 | Lets players carry carcasses and process them through butchering. | Entity death, carcasses and loot. | Danger bonus loot runs once from the original living entity and ignores carcass entities. Never multiply Butchering outputs. |
| Carry On 1.14.2 | Lets players carry blocks, containers and entities. | New Grandmaster block entities. | Give every station explicit break/carry behavior and serialize its inventory. No Carry On API calls; test carried and non-carried states if the mod recognizes the block. |
| ChangeClass 1.1.1 | A book opens character creation so a player can choose a class again. | Apprentice race class and persistent race packet. | This is the only active class-flow risk. A completed class change must refresh the saved Apprentice race packet, while internal restore calls must not overwrite it. Test race, subrace, profession and appearance across relog. |
| Desire Paths 0.5.1 | Converts frequently walked ground into paths over time. | Runtime block changes. | Heatmap resource nodes must not use ordinary path blocks and must tolerate later neighboring block changes. |
| DEUSEX 1.0.0 | The package contains five tension sounds and, by assembly-symbol inspection, a server command that sends a play-sound packet to a selected player. | Network channel and audio only. | Use distinct Apprentice channel and packet IDs. No audio or command integration is required. |
| Eternal Seraph Backpacks 3.3.2 | Adds backpack items for the Eternal Seraph server. | Inventory capacity and item attributes. | Do not assume vanilla inventory size or bag codes. Poisoned arrows and tools must work in ordinary item slots without scanning private bag inventories. |
| Expanded Foods 2.0.0-dev.12 | Adds a large set of food recipes and liquids; depends on ACA. | Barrel recipes, liquids, meals and food storage. | Apprentice poison has no nutrition properties and is never a meal ingredient. Optional food integration is asset-code based and off by default. |
| Expanded Matter 3.7.0 | Content library adding materials and variants to base systems. | Metal variants and recipe wildcards. | Starsteel and Aethersteel must be complete in the Apprentice domain and must not require `em`. Recipe wildcards need allow-lists so Expanded Matter does not broaden them accidentally. |
| Explosives 0.2.1 | Adds explosive items/blocks and ignition behavior. | Entity damage and high-tier resource blocks. | Danger scaling changes entity base health/outgoing damage, not the explosion system. Resource blocks define explicit blast resistance and drops. |
| FoodShelves 3.0.4 | Adds food shelves and food storage, mainly for Expanded Foods. | Poison storage classification. | Poison containers and coated arrows are not food and must not advertise shelf/meal attributes unless Apprentice explicitly supplies a safe display transform. |
| Footprints 1.2.4 | Makes players and animals leave footprints. | Entity movement/render effects. | Do not scale model size or rewrite movement controllers for danger tiers; health/damage scaling avoids footprint and animation distortion. |
| Grapes and Wine 1.4.0 | Adds grapes, vines/bushes and wine. | Barrel solvents and plant worldgen. | Core poison recipes use Apprentice ingredients and do not require wine. A later optional wine solvent may use a conditional asset patch, never a hard dependency. |
| Herbarium 1.4.3 | Library of reusable plant classes. | Poison plant implementation. | Apprentice poison plants use vanilla block/item contracts. Herbarium support may be added through optional assets, but core plants cannot inherit its classes. |
| Immersive Fibercraft 1.2.12 | Adds spinning and weaving mechanics. | Tailor Grandmaster content. | Apprentice sewing and armor-liner recipes use vanilla inputs by default. Optional fibers are resolved by code and never replace the core recipe. |
| Japanese Architecture 0.9.7 | Adds Japanese walls, doors and architectural variants. | Builder/woodworker content and recipe namespace. | Use Apprentice codes and standard material wildcards with allow-lists. No architectural assets are copied or required. |
| Material Needs: Flowers 1.0.0 | Adds 28 flower varieties. | Rare ecology and poison ingredient discovery. | Do not classify unknown flowers as poison automatically. Optional ingredients require an explicit, reviewed code list. |
| Molds Expanded 1.2.0 | Adds casting molds for items normally made by smithing. | Starsteel/Aethersteel production rules. | T6/T7 ingots and components are not castable unless Apprentice deliberately adds a balanced mold. Default progression remains cementation plus smithing. |
| Natural Fertilizer 1.5.1 | Creates fertilizer from animal droppings. | Rare plant growth. | Apprentice plants use vanilla nutrient/growth properties and accept normal fertilization behavior. No special multiplier is added for this mod. |
| NDL Wooden Torch Holders 3.0.2 | Adds inexpensive wooden torch holders. | Light and SafeHouse interaction. | No direct integration. Danger scaling never forces a spawn that another mod denied because of light or rooms. |
| Plains and Valleys 1.0.13 | Patches landforms and story-structure worldgen. | Terrain shape and map distance. | Heatmap tiers use horizontal world coordinates, not terrain height, climate map or landform IDs. |
| Player Corpse 1.14.0 | Stores a dead player's inventory in a corpse. | Entity death and player drops. | Danger loot excludes players and corpse entities. Apprentice persistence stays in player moddata, not inventory items. |
| Primitive Survival 5.0.6 | Adds traps, fishing and many survival systems. | Hunter/fisher Grandmaster items, traps and liquids. | New Apprentice items use unique codes and independent logic. Do not patch Primitive Survival traps or multiply their catches/drops. |
| ProspectTogether 2.2.1 | Stores, displays and shares prospecting results on an ore map layer. | World-map overlays, textures and networking. | Register a separate heatmap layer; never patch `OreMapLayer`. Cache heatmap textures and keep network channels unique. |
| Real Smoke 1.3.1 | Adds physics-based smoke. | Grandmaster kiln/cementation furnace emissions. | Furnaces expose ordinary combustion/smoke data where supported. Do not call Real Smoke or require its behaviors. |
| Safe House 1.0.1 | Rejects hostile/drifter spawns in enclosed or lit areas during storms. | Hostile spawn pipeline. | Apprentice listens after a successful spawn and never overrides `OnTrySpawnEntity`; a denied spawn remains denied. |
| Scaffolding 1.3.1 | Adds scaffolding blocks. | Builder progression only. | No integration is needed. Recipes and interactions use unique Apprentice IDs. |
| Sortable Storage 3.0.0 | Adds a sort button to storage containers. | New station inventories and GUIs. | Use standard inventory/slot contracts and deterministic slot roles. Sorting must be disabled for recipe-locked processing slots if reordering would change a running job. |
| Th3Dungeon 1.0.1 | Adds dungeon variation to world generation. | Rare resources and danger loot. | Do not modify schematics or dungeon placement. Coordinate danger applies consistently regardless of structure source. |
| Translocator Engineering Redux 1.6.6 | Allows construction, deconstruction and linking of static translocators. | Long-distance travel across danger tiers. | Entity tier is locked when the entity spawns. Players are never stat-scaled, so teleporting cannot duplicate or repeatedly rescale entities. |
| UraniumExpanded 2.0.1 | Adds uranium generation, alloys, tools and many metal patches. | T6/T7 metals, tool tiers and shared vanilla metal variants. | Apprentice uses unique metal codes and narrowly scoped patches. Startup validation detects code/variant collisions; Uranium content is never a required ingredient. |
| VintageCanvas 1.1.1 | Adds in-world painting. | GUI/rendering only. | No direct integration. Heatmap rendering is confined to the world map and does not patch general canvas rendering. |
| VS Airship Mod 1.1.4 | Adds lore-friendly airships. | Fast travel and entity classification. | Exclude vehicles/mounts from danger scaling. Distance tiers are coordinate based and work at any altitude. |
| VS Roofing Mod 1.6.2 | Adds gridless roof construction with dynamic slopes/corners. | Builder content and block interaction. | Apprentice stations use ordinary collision/selection boxes and do not patch roof placement or shape generation. |
| VS Village 6.0.1 | Adds villages, villagers, jobs, workstations, schedules, pathfinding and optional village generation. | NPCs, workstations and entity scaling. | Exclude village NPCs and guards by default. Apprentice stations do not register as village workstations unless an optional, separately tested adapter is enabled. |
| Watersheds 6.4.3 | Replaces major worldgen behavior with watershed-derived terrain, streams and related runtime spawn rules. | Worldgen passes, chunks and aquatic spawns. | Danger uses persisted spawn coordinates and deterministic chunk coordinates. Do not replace Watersheds maps, stream blocks or spawn handlers. |
| Waypoint Beacon 1.7.2 | Renders configurable 3D beams and labels for waypoints. | Map waypoint objects and client rendering. | The heatmap does not create waypoints or 3D beacons. It renders only inside its map layer and disposes its own textures. |
| Waypoint Together Reborn 2.4.2 | Shares waypoints with other players. | Map networking and waypoint dialogs. | The heatmap sends only anchor/config data and never waypoint packets. Use unique channel/message types. |
| Wildcraft: Fruits and Nuts 1.4.72 | Adds many fruits, berries, nuts, plants, foods and barrel recipes; depends on Herbarium. | Poison ingredients, rare plants, liquids and worldgen. | Core poison uses fictional Apprentice plants. Optional Wildcraft ingredients require an explicit code allow-list and `dependsOn`; never infer toxicity from a berry name. |

## Dependency-free patterns learned from the pack

These are architectural lessons, not copied code:

| Audited pattern | Apprentice-owned implementation |
| --- | --- |
| Auto Map Markers, ProspectTogether and waypoint mods separate client rendering from server packets. | Send only the persisted heatmap anchor and tier configuration; calculate and cache colored map tiles client-side in an Apprentice map layer. |
| Wildcraft and Expanded Foods express barrel transformations and liquids as assets. | Define fictional poison liquids and ordinary barrel recipes in Apprentice JSON; add C# only for arrow coating, validation and DoT. |
| UraniumExpanded demonstrates that a new metal touches many vanilla variant arrays and tool assets. | Maintain a reviewed metal-coverage checklist and validate every Apprentice variant, recipe, texture and tier before enabling a metal. |
| Safe House cancels invalid spawns before the entity enters normal play. | Scale only successfully spawned entities; never create replacement spawns or override another mod's rejection. |
| Watersheds and terrain content mods substantially change worldgen. | Derive danger from coordinates and keep rare content in Apprentice-owned generation passes so no foreign climate/landform schema is assumed. |
| Attribute Rendering Library uses item/entity attributes for variants. | Store poison identity, danger tier and race data in namespaced attributes using only base-game serialization contracts. |
| Carry On, storage mods and station-heavy mods stress inventory persistence. | Use standard inventories, persist running jobs, make output claiming idempotent and define carry/break behavior explicitly. |
| VS Village and livestock mods add friendly non-player entities. | Use configurable include/exclude code patterns and entity attributes rather than assuming every non-player entity is hostile. |
| ChangeClass re-enters vanilla character creation. | Treat the authoritative character-class setter as the integration boundary and protect internal restore operations from accidental re-persistence. |

## Required compatibility test matrix

Run these tests for every milestone that touches a shared system:

1. Apprentice alone on a new single-player world.
2. Apprentice alone on a separated dedicated server and client.
3. Apprentice plus the one relevant shared-system mod.
4. The complete 48-mod server pack.
5. Save, stop, reload and verify persisted state.
6. Remove the optional mod and reload the same copied test world.
7. Re-add the optional mod and verify there is no duplicated registration.
8. Review server-main, server-debug, client-main and client-debug logs.

Specific release gates:

- ChangeClass: changed race/subrace/profession/appearance survives two relogs.
- Safe House: Apprentice never spawns an entity in a location Safe House denied.
- map pack: all four map/waypoint mods and the heatmap toggle coexist.
- food pack: poison is never edible, cookable, shelf-classified as food or used by
  a wildcard meal recipe.
- metal pack: Starsteel/Aethersteel codes do not collide with Expanded Matter or
  UraniumExpanded and foreign metals do not satisfy Apprentice charges.
- worldgen pack: a new Watersheds world generates without Apprentice exceptions,
  deterministic duplicate nodes or structure replacement.

## Audit limitation

Static inspection proves namespace/dependency boundaries and identifies likely
shared surfaces. It cannot prove runtime compatibility with Vintage Story
1.22.3. The final gate is a full-pack dedicated-server test using the exact
server configurations and mod builds listed above.
