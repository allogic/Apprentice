# Dialog text bars — Phase 2B

Version: 1.0.6

The graphical HudElement/GuiElementStatbar implementation still caused a hard
client crash on the first normal XP award.

This build uses the already proven GuiDialog path instead.

## Level-up

Uses Vintage Story's built-in discovery notification only.

## Normal XP

One non-focusable GuiDialog appears in the top-left. It contains every active
skill and renders each progress bar as ordinary text:

```text
Miner — Level 2   +1 XP
[########----------------] 8.4 / 20 XP

Stoneworker — Level 1   +0.5 XP
[###---------------------] 1.5 / 10 XP
```

## Removed

- HudElement progress rows;
- GuiElementStatbar;
- one-dialog-per-skill composition;
- custom renderer code.

## Retained

- main-thread packet dispatch;
- simultaneous skills;
- animated progress calculation;
- automatic disappearance;
- vanilla discovery level-ups;
- chat fallback.

## Test

Confirm:

```text
[Apprentice] Client startup: begin — DialogTextBars Phase 2B, version 1.0.6.
```

Then break sand and ore. When multiple skills receive XP from one action, all
of them should be shown in the same top-left dialog.
