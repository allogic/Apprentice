# Apprentice release checklist

## Automated checks

- Set `VINTAGE_STORY` to the release's game installation and run
  `python tools/validate_assets.py` from the repository root. With the variable
  set, the validator also checks the order-sensitive vanilla class patch.
- Build the solution with the intended Vintage Story installation.
- Run the Cake `Package` target.
- Open the generated ZIP and confirm that `assets`, `modinfo.json` and
  `Apprentice.dll` are at its root.
- Remove every prior Apprentice ZIP/folder from the test `Mods` directory and
  fully restart the client before testing a same-version hotfix.
- Confirm that no files are distributed below `assets/game`.

## Game-version compatibility

- Test against the minimum game version declared in `modinfo.json`.
- Verify that `replace-vanilla-characterclasses.json` still targets the six
  intended vanilla class entries. Its array positions are game-version
  sensitive.
- Enable `/errorreporter 1` and inspect `client-main.txt` and `server-main.txt`
  for missing shapes, textures, skinparts and patch warnings.

## Character creation

- Open and close the race window repeatedly without an exception.
- Verify Race opens first, Skin & Voice remains blocked until race confirmation,
  and either tab then focuses with one click.
- Verify Randomize and Last selection are absent.
- Confirm every race and subclass, then save and reload the world.
- Verify height, thickness, profession, subclass, skin, eye and hair choices;
  specifically select Ruby eyes on Drow.
- Verify Hair Color and Horn Color are below Eye color, and that horn colors and
  ivory-white teeth only appear for eligible races.
- Check every base-skin swatch on Dragonborn, Dwarf, Elf, Gnome and Halfling for
  mismatched green facial geometry.
- At 0%, 50% and 100% height, compare first-person eye level with the model head
  in third person for Gnome, Dwarf, Human, Dragonborn and Goliath; repeat while
  sneaking, sitting, swimming and mounted.
- After Confirm Skin, open the pause menu and click Open to LAN, Command
  Handbook and Save & Leave world through their full button areas.
- Confirm first-person hidden parts remain visible to other players.

## Progression

- Trigger every configured interaction category at least once.
- Confirm only professions configured for the interaction gain experience.
- Confirm the selected profession receives its 10% experience bonus.
- Verify skill purchases, prerequisites, exclusive paths and capstones.

## Grandmaster content and discoveries

- Verify all 18 `UnlockRecipe` outputs resolve and are rejected before their
  corresponding capstone is purchased.
- Purchase every capstone and verify each output recipe becomes craftable on
  the server, not merely visible on the client.
- Unlock all 28 discoveries across representative profession/race/subrace
  combinations; reconnect and restart the server, then verify no unlock is
  revoked or announced twice.
- Change race/profession after a heritage discovery and confirm the discovery
  remains a historical unlock while new discoveries are evaluated from the
  confirmed snapshot.

## Advanced metals

- Seal split-stack Starsteel and Aethersteel charges and confirm the furnace
  consumes exactly the configured fuel and refractory quantities only after
  every validation succeeds.
- Break an unsealed, sealed-incomplete and complete furnace; verify the intended
  refund/consumption behavior and no duplicated output.
- Save/reload before and after completion, claim once, restart again and verify
  no second claim is possible.
- Verify a running charge retains its persisted duration after a config balance
  edit, and that Aethersteel can be worked only on the Starsteel anvil.

## Poison

- Brew every poison tier, coat exactly eight flint arrows per 0.1 litre and
  verify ordinary melee/thrown attacks never apply poison.
- Test weaker, equal and stronger poison reapplication, maximum extension,
  immune entities, PvP disabled/enabled and shooter logout.
- Unload/reload a poisoned entity and restart the server; verify the remaining
  effect persists and expires once without duplicate tick listeners.

## Danger regions, map and ecology

- Record the persisted danger anchor, restart the world and verify tier borders
  do not move when the world spawn later changes.
- Spawn eligible enemies on both sides of each tier boundary; unload/reload them
  and verify health/damage multipliers are applied exactly once.
- Verify traders, players, mounts, pets, livestock and opted-out mod entities
  are not scaled.
- Toggle the Apprentice heatmap independently of other layers and profile map
  pan/zoom; the radial texture must not rebuild per frame.
- Generate identical chunks twice from the same seed and confirm deterministic,
  bounded Apprentice-only plant placement. Existing chunks receive no retrogen.
- Kill tiered enemies and verify only Apprentice bonus drops scale, with one
  loot roll per death and no modification of vanilla/foreign loot tables.

## Client/server lifecycle

- Test a single-player save, quit and reload cycle.
- Test with a dedicated local server using a separate data path.
- Join, disconnect and rejoin without duplicate Harmony callbacks.
- Stop and reload the world, then check that static patch and reflection caches
  were cleared.
