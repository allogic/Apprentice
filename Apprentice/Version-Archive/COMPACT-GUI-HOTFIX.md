# Compact skill-tree GUI hotfix - version 2.0.4

The uploaded logs confirm that version 2.0.2 loaded correctly, but the process
ended without a managed Apprentice exception and without a new crash entry.

That pattern is consistent with a hard/native failure during GUI composition.

## Removed

The previous GUI composed all of these at once:

- 18 class buttons;
- seven node buttons;
- multiple static and dynamic text elements;
- a large fixed dialog;
- full composer disposal and reconstruction;
- Unicode progress characters.

## New structure

The 2.0.4 GUI follows the earlier stable Apprentice progression-dialog pattern:

- one `GuiComposer`;
- one `GuiElementDynamicText`;
- five ordinary navigation buttons;
- one title-bar close button;
- autosized dialog bounds;
- ASCII-only generated text;
- no stat bar;
- no composer rebuilding;
- no composer disposal while the dialog is open.

## Controls

- Previous class
- Next class
- Previous node
- Next node
- Spend skill points

The selected class, XP, passive mastery, all seven tree nodes, node
requirements, effects and purchase feedback are shown in the text panel.

## Diagnostic logging

Pressing U now logs each stage:

```text
U hotkey received
creating dialog
composition begin
composition complete
opening dialog
TryOpen returned True
```

If the process stops again, the final printed stage identifies the exact native
boundary.
