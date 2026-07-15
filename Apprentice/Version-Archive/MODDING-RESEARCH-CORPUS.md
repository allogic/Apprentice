# Vintage Story modding research corpus

Research baseline for Apprentice interaction adapters.

## Master index

The complete live `Category:Modding` index is treated as the project-wide
research corpus. At the time this foundation was prepared, the category page
reported 135 pages and 10 subcategories.

Not every page changes every adapter. Model-creator, texture, world-generation
and content-only pages remain indexed for later systems, while server-event,
entity, synchronization and block-interaction pages are the load-bearing
sources for this phase.

## Pages directly applied to the native foundation

- `Modding:Block and Item Interactions`
- `Modding:Code Mods`
- `Modding:Server-Client Considerations`
- `Modding:Entity Behaviors`
- `Modding:World Access`
- `Modding:Modding API Updates`
- `Modding:Using Tags (Code)`

## Source-priority rule

Some wiki tutorials explicitly identify themselves as outdated. For executable
API signatures and current behavior, Apprentice uses this priority:

1. current Vintage Story API documentation;
2. current official Vintage Story source;
3. current wiki conceptual guidance;
4. outdated wiki tutorials only as historical/contextual material.

## Native API facts used

- `DidBreakBlock` fires after a block was broken.
- `DidPlaceBlock` reports the player, replaced block ID, selection and source
  item stack.
- `OnEntityDeath` reports the dead entity and its `DamageSource`.
- `DamageSource.GetCauseEntity()` resolves the responsible player for both
  melee and projectile damage.
- crop properties expose their total number of growth stages.

## Future adapter research areas

The same category corpus remains active for:

- completed crafting and repair;
- smithing;
- smelting, cooking and machine processing;
- prospecting and panning;
- fishing;
- breeding, milking and shearing;
- compatibility and Harmony patch fallback points.

# Completion-event research addendum — 1.2.0

The complete `Category:Modding` index remains the project research corpus.
The pages most directly applied in this phase are the code-mod, interaction,
inventory, server/client and Harmony pages.

Current executable signatures were verified against the API documentation and
official survival-mod source because some wiki tutorials identify themselves
as older material.

## Verified completion points

- `GridRecipe.ConsumeInput(IPlayer, ItemSlot[], int)`
- `BlockEntityAnvil.CheckIfFinished(IPlayer)`
- `ItemProspectingPick.ProbeBlockNodeMode(...)`
- `ItemProspectingPick.PrintProbeResults(...)`
- `EntityBehaviorMilkable.MilkingComplete(...)`
- `BlockEntityFirepit.smeltItems()`
- `BlockEntityQuern.grindInput()`
- `InventoryBase.ActivateSlot(...)`

The full category remains relevant for upcoming Pan, Fish, Breed, Shear,
specialized Harvest and additional processing-machine adapters.

## All-interactions addendum — 1.4.0

The complete `Category:Modding` page list remains the project research corpus.
This phase most directly applies its interaction, inventory, entities,
server/client synchronization, block-entity and Harmony guidance.

Executable completion points are checked against current API documentation and
the official survival-mod source. The relevant boundaries include:

- `BlockPan.CreateDrop`
- `EntityBobber.TryCatchFish`
- `BlockEntityTrough.OnInteract`
- `BlockEntityTrough.ConsumeOnePortion`
- `EntityBehaviorMultiply.TryGetPregnant`
- `ItemShears.OnBlockBrokenWith`
- `EntityBehaviorHarvestable.SetHarvested`
- `BlockEntityBloomery.DoSmelt`
- `BlockEntityPitKiln.KillFire`
- `BlockEntityBeeHiveKiln.ConvertItemToBurned`
- `BarrelRecipe.TryCraftNow`
- `BlockEntityFruitPress.InteractMashContainer`
- `Block.OnBlockInteractStart`

