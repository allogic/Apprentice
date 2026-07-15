# XP popup rebuild — Phase 1B

Version: 1.0.4

The text-only Apprentice HudElement still caused a hard client crash when the
first matching block was broken. Therefore this phase removes all custom
Apprentice popup UI.

XP notifications are passed to Vintage Story's existing discovery-message HUD
through:

```csharp
api.TriggerIngameDiscovery(sender, eventCode, message);
```

## This build contains no popup-specific

- HudElement;
- GuiDialog;
- GuiComposer;
- dynamic text element;
- stat bar;
- game-tick listener;
- custom renderer.

The U progression window remains unchanged because it already works safely.

## Test

1. Build and launch version 1.0.4.
2. Confirm:

```text
[Apprentice] Client startup: begin — VanillaPopup Phase 1B, version 1.0.4.
```

3. Break sand and an ore block.
4. Confirm the client remains connected.
5. Confirm Vintage Story displays a fading XP message.
6. Press U and verify stored XP.

A failure in the built-in discovery notification falls back to a normal client
chat message.
