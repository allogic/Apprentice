# Apprentice 2.2.1e

Apprentice combines profession mastery, skill trees, hidden-class discoveries,
and a new data-driven fantasy race layer for Vintage Story 1.22.3.

## Build

1. Copy `Directory.Build.props.example` to `Directory.Build.props`.
2. Set `VINTAGE_STORY` to the Vintage Story installation directory.
3. Run:

```text
dotnet build
```

The project targets .NET 10 and Vintage Story 1.22.3, matching the original
project files.

## 2.2.0 race foundation

The vanilla character-class choices are replaced at the asset layer by nine
playable races:

- Dragonborn
- Dwarf
- Elf
- Gnome
- Goliath
- Halfling
- Human
- Orc
- Tiefling

Each race is registered as a mod-owned Vintage Story character class. Its
advantages and drawbacks are defined as ordinary character traits so the
server remains authoritative and existing Apprentice profession data is not
renamed or migrated.

Race files:

- `assets/apprentice/config/characterclasses.json`
- `assets/apprentice/config/traits.json`
- `assets/game/config/characterclasses.json`
- `assets/apprentice/patches/replace-vanilla-characterclasses.json`
- `assets/apprentice/lang/en.json`

The Apprentice-domain character-class file contains only the nine enabled race
choices. A complete game-domain compatibility file retains the six vanilla
records in their original order with `enabled: false`. The obsolete index patch
is empty, so it cannot fail when an older development origin supplies an empty
class file. Only the nine enabled races are offered for a new selection.

Vintage Story 1.22.3 throws while opening `.charsel` when an existing player
still references a removed class code. Apprentice repairs that save state on
the server during player join: valid race choices are preserved, while an
invalid legacy selection is cleared so the normal race selector opens safely.
New players are left to the normal first-join flow.

Distinct race player models are intentionally not part of this first 2.2.0
milestone. Model replacement must preserve the player animation skeleton,
armor and clothing attachment points, held-item transforms, camera position,
and multiplayer synchronization.

## Configuration ownership

### `assets/apprentice/config/class.json`

This is the only registry for class-specific information:

- which classes exist;
- class IDs;
- display names;
- interaction names;
- asset-code patterns;
- XP reward amounts.

There is no hard-coded class list in C#.

### `ModConfig/ApprenticeConfig.json`

This is created automatically and contains only generic mod behavior:

- whether creative mode can earn XP;
- notification animation timings;
- optional XP logging.

`ApprenticeConfig.example.json` shows the defaults.

## Matching rule

Each class is checked independently.

For one class and one interaction, only the matching pattern with the longest
configured pattern string grants XP. Equal-length ties keep the first entry in
`class.json`.

Example:

```json
"game:ore-*": 1.0,
"game:ore-bituminouscoal-*": 0.5
```

Breaking bituminous coal grants Miner `0.5` XP, because the second matching
pattern is longer.

A different class may also receive its own configured XP from the same action
when one of its patterns matches.

## Progression data

The server stores and synchronizes:

```text
apprentice/classes/{classId}/experience
```

Level values are calculated from `ExpEquation.cs`; levels are not duplicated in
storage.

## XP notification

Every awarded class XP event is sent from the server to the affected client and
placed into a FIFO queue.

Each notification displays:

- skill/class display name;
- animated current level;
- XP gained;
- an XP progress bar.

When a threshold is crossed, the progress bar fills and resets according to the
supplied equation, the displayed level changes, and the level text blinks.
After the configured hold time, the notification vanishes and the next queued
award is shown.

## Compatibility

The Apprentice profession IDs, experience storage, skill-tree IDs, hidden-class
IDs, and approved U-window layout are unchanged from 2.1.14i. The race choice
is the Vintage Story character-class choice; it is separate from Apprentice's
profession progression.
