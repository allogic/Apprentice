# Smithing and clay-forming fix — version 1.2.2

## Smithing

The smithing hook was targeting the correct completion method, but the
Blacksmith configuration did not match normal smithing output codes.

For example:

```text
game:knifeblade-tinbronze
```

does not match:

```text
game:toolhead-*
```

The Blacksmith Smith configuration now supports actual vanilla families:

```text
*head-*
*blade-*
shears-*
wrench-*
plate-*
chain-*
scales-*
nailsandstrips-*
shieldboss-*
shieldhoop-*
```

A low-value `game:*` fallback ensures modded smithing recipes still produce
some Blacksmith XP.

## Clay forming

Clay forming does not use the grid crafting system.

A new Harmony adapter patches:

```text
BlockEntityClayForm.CheckIfFinished(IPlayer, int)
```

The prefix captures the responsible server player and selected recipe output.
The postfix awards Craft XP only when vanilla cleared `SelectedRecipe`, which
happens after the clay pattern actually completed and the output was created.

Potter patterns now support raw clay-form output families such as:

```text
bowl-*-raw*
claypot-*-raw*
crock-*-raw*
jug-*-raw*
crucible-*-raw*
toolmold-*-raw*
ingotmold-*-raw*
anvilmold-*-raw*
```

## Expected tests

Tin-bronze knife blade:

```text
Blacksmith — Smith XP
```

Raw red-clay bowl:

```text
Potter — Craft XP
```
