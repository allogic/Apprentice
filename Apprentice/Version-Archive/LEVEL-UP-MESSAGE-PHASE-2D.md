# Level-up discovery message — Phase 2D

Version: 1.0.8

The vanilla discovery notification now displays:

```text
Level up to 12
```

The number is replaced with the skill's new level.

Unchanged:

- passive top-left XP HUD;
- simultaneous skill rows;
- text progress bars;
- normal gameplay input while the HUD is visible;
- main-thread GUI dispatch;
- queued level-up discoveries;
- stable experience equation.

The chat fallback message is unchanged; only the vanilla discovery message was
requested to change.
