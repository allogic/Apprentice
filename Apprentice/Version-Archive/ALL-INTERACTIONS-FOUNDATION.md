# All remaining interaction types — version 1.4.0

This build completes the base-game interaction names currently present in
`class.json`.

## Pan

`BlockPan.CreateDrop` is the completion boundary after the full panning action.
One completed pan action awards one configured Pan reward against the source
material code. The reward does not depend on winning a random loot roll.

## Fish

`EntityBobber.TryCatchFish` is observed on the server.

- real fish use their entity/item code;
- virtual fish use `game:fish-catch`;
- junk catches are ignored;
- inventory gains are preferred, with real-fish fallback when the inventory is
  full and the fish is dropped nearby.

## Breed

Player attribution follows the food:

1. a player adds food to a trough;
2. an animal consumes a portion from that trough;
3. the animal stores the feeder UID temporarily;
4. `EntityBehaviorMultiply.TryGetPregnant` awards Breed XP only when pregnancy
   really begins.

No nearest-player guess is used. Unattributed or expired feeding does not grant
XP.

## Shear

Base-game shears do not expose a generic animal-shearing event. Version 1.4.0
covers the actual vanilla shears completions:

- successful foliage multi-breaks;
- successful berry-bush pruning.

Each completed shears use awards one configured Shear reward. Modded animal
shearing can use the same `Shear` interaction name and target-code patterns.

## Harvest

Harvest now covers:

- mature crops broken by the player;
- hive/skep/honey-related block harvests;
- right-click berry, fruit, mushroom and similar resource collection;
- generated carcass contents from `EntityBehaviorHarvestable.SetHarvested`.

Right-click and carcass rewards use positive inventory/output changes, so
opening an interface or beginning an action does not award XP.

Carcass outputs are awarded by actual stack code and quantity, allowing Hunter
patterns such as hide and bone to match independently.

## Smelt

In addition to the existing firepit path:

- bloomery output is marked when smelting completes and credited to the player
  who breaks/collects it;
- pit-kiln converted stacks are marked before the kiln becomes ground storage;
- beehive-kiln converted slots are marked after conversion.

Temporary markers are consumed by the existing player-pickup attribution
system. Automated extraction does not credit an arbitrary player.

## Process

In addition to the existing quern path:

- successful barrel recipe output is marked by `BarrelRecipe.TryCraftNow`;
- completed fruit-press mash removal awards Process XP against the resulting
  juice code.

Barrel output remains player-attributed at pickup because sealed recipes finish
asynchronously.

## Interaction coverage

```text
DestroyBlock  PlaceBlock  Plant       Harvest
Craft         Repair      Smith       Smelt
Cook          Process     Prospect    Pan
KillPvE       KillPvP     WeaponKill  Fish
Breed         Milk        Shear       ShieldBlock
DamageTaken
```

## Startup marker

```text
[Apprentice] Client startup: begin — AllInteractions Foundation, version 1.4.0.
```

## Important design rules preserved

- the server is authoritative;
- every configured class is checked independently;
- the longest matching target pattern wins inside each class/interaction;
- specialized actions avoid false attribution;
- asynchronous production rewards the player who collects the output;
- passive XP HUD and exact level-up message are unchanged.
