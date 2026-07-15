# Skill-tree GUI crash fix — version 2.0.2

## Symptom

The client crashed immediately when pressing `U`.

## Causes removed

### GuiElementStatbar

The skill-tree screen reintroduced:

```csharp
composer.AddStatbar(...)
```

Earlier Apprentice tests already showed that `GuiElementStatbar` was unsafe in
this environment.

It has been replaced with a plain text progress bar:

```text
[██████████······················] 31.3%
```

No custom renderer or stat-bar element is used.

### Rebuilding an active composer during callbacks

The old screen called `Recompose()` directly:

- from `OnGuiOpened`;
- inside class-button callbacks;
- inside node-button callbacks;
- inside the purchase-button callback.

That could dispose the active `GuiComposer` while Vintage Story was still
opening the dialog or processing its input event.

Version 2.0.2 now:

- composes safely before `TryOpen`;
- queues selection/purchase refreshes onto the next main-thread task;
- disposes the old composer only outside the active callback;
- closes the dialog and logs the exception if composition fails.

## Preserved features

- class navigation;
- branching nodes;
- capstones;
- XP and level display;
- server-authoritative purchases;
- skill points;
- mastery effects;
- passive XP HUD;
- exact level-up discovery text.
