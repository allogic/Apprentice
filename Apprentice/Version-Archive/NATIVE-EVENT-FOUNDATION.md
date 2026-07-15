# Native event foundation — version 1.1.0

This build adds the first server-authoritative interaction adapters.

## Implemented native interactions

### DestroyBlock

Triggered by `DidBreakBlock` for normal blocks.

### PlaceBlock

Triggered by `DidPlaceBlock` for ordinary placed blocks. The target code is
the actual block present at the selected position after placement.

### Plant

Triggered by `DidPlaceBlock` when:

- the source item code contains `seed`; or
- the source item code contains `sapling`; or
- the placed block has crop properties.

The source item code is used as the XP target whenever available, so
`class.json` can reward individual seeds and saplings.

Planting is exclusive with PlaceBlock: one placement cannot receive both
interaction types.

### Harvest

Triggered when a mature crop block is broken. The crop's numeric growth stage
is compared with its configured `CropProps.GrowthStages`.

Mature harvesting is exclusive with DestroyBlock: one crop break cannot
receive both rewards.

This first native phase does not yet claim right-click berry, fruit, honey,
carcass or tool-specific harvesting. Those require before/after result
adapters so XP is awarded only after a real output was obtained.

### KillPvE and KillPvP

Triggered by `OnEntityDeath` when the responsible cause is a server player.

`DamageSource.GetCauseEntity()` attributes:

- direct melee kills;
- projectile kills to the player who launched the projectile.

A dead player is classified as KillPvP. Every other dead entity is classified
as KillPvE. Self-kills are ignored.

## Multi-skill rule

ExperienceManager still evaluates every configured class independently.
Therefore one death or placement can reward every matching skill.

## Architecture

```text
InteractionEventBridge
├── BlockInteractionAdapter
└── EntityDeathInteractionAdapter
        ↓
InteractionContext
        ↓
ExperienceManager
```

Future mechanics can be added as independent disposable adapters without
turning the bridge into a large class.

## Test checklist

1. Confirm the client startup marker:

```text
[Apprentice] Client startup: begin — NativeEvents Foundation, version 1.1.0.
```

2. Confirm the server log contains:

```text
[Apprentice] Native interaction foundation ready:
DestroyBlock, PlaceBlock, Plant, Harvest, KillPvE and KillPvP.
```

3. Place stone:
   - Builder gains PlaceBlock XP.
   - Farmer does not gain Plant XP.

4. Plant a seed and a sapling:
   - Farmer gains Plant XP.
   - Builder does not gain generic PlaceBlock XP for the same action.

5. Break an immature crop:
   - handled as DestroyBlock.

6. Break a fully mature crop:
   - handled as Harvest;
   - no additional DestroyBlock reward for that action.

7. Kill an animal with melee and then with a projectile:
   - matching PvE classes receive XP both times.

8. Kill another player:
   - matching PvP classes receive XP.

9. Cause your own death:
   - no PvP reward is granted.
