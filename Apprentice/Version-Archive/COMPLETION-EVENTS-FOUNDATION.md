# Completion-event foundation — version 1.2.0

This build extends the stable native-event foundation with completion-specific
interaction adapters.

## Newly active

### Craft

A successful `GridRecipe.ConsumeInput(...)` awards Craft XP against the
resolved recipe output code.

### Repair

The same successful grid-recipe completion is classified as Repair when the
recipe name contains `repair`, matching Vintage Story's recipe naming rule.

### Smith

XP is awarded only when `BlockEntityAnvil.CheckIfFinished(IPlayer)` has a
matching selected recipe and a real server player completed it.

Helve-hammer completions have no responsible player and therefore receive no
player XP.

### Prospect

- Node mode awards once after a valid propickable sample completes.
- Density mode awards when `PrintProbeResults(...)` is reached, meaning the
  required sample set is complete.
- The propick sample block is suppressed from the generic DestroyBlock reward.

### Milk

XP is awarded after `MilkingComplete(...)` only when the entity's
`lastMilkedTotalHours` value actually advanced.

### Smelt and Cook

Firepit completion is marked on its output stack.

The responsible player receives XP when they collect that marked output:

- cooking-recipe output, meals, bread and pies -> Cook;
- other firepit-smelted output -> Smelt.

This deliberately does not award XP merely because an unattended firepit
finished.

### Process

Quern completion is marked on its output stack. The responsible player receives
Process XP when collecting that output.

## Quantity

Machine outputs can represent several completed operations merged into one
stack. `InteractionContext.Quantity` now multiplies the configured XP reward,
and one notification contains the combined gain.

## Marker safety

Machine completion markers use `ItemStack.TempAttributes`:

- they do not alter normal stack-merging identity;
- they are removed before output enters a player inventory;
- unattended/automated extraction does not receive player XP;
- pending attribution can be lost on server restart, which is preferable to
  crediting the wrong player.

## Harmony lifecycle

The project references:

```xml
$(VINTAGE_STORY)/Lib/0Harmony.dll
```

All patches use the unique ID:

```text
apprentice.interactions.completion
```

Only that ID is unpatched when Apprentice unloads.

## Still pending

These need separate exact-result hooks and are intentionally not faked:

- Pan
- Fish
- Breed
- Shear
- barrel and fruit-press Process
- right-click berry, fruit, honey and carcass Harvest
- bloomery, pit-kiln and other non-firepit Smelt systems

## Test checklist

1. Confirm:

```text
[Apprentice] Client startup: begin — CompletionEvents Foundation, version 1.2.0.
```

2. Check the server log for the number of installed completion patches.

3. Craft an item matching class.json.

4. Complete a repair recipe.

5. Finish an anvil recipe by hand.

6. Complete node-mode and density-mode prospecting.

7. Milk an eligible animal.

8. Smelt one or several matching outputs in a firepit, then collect them.

9. Cook a meal/bread/pie and collect it.

10. Grind an item in the quern and collect its output.

11. Confirm U shows the same stored totals.

12. Confirm automated extraction does not credit a random player.
