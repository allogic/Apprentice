# Client connection hotfix

## Root cause

`ExperienceNotificationHud` inherited from `HudElement` but overrode:

```csharp
public override string ToggleKeyCombinationCode =>
    "apprentice-experience-notification";
```

No matching hotkey was registered.

When Vintage Story finishes loading client block textures during connection,
`GuiDialog.OnBlockTexturesLoaded()` reads `ToggleKeyCombinationCode` and calls
`SetHotKeyHandler()` for every non-null value. A HUD with no toggle key should
use the default `HudElement` behavior, which returns `null`.

## Changes

1. Removed the HUD `ToggleKeyCombinationCode` override.
2. Removed the unused HUD hotkey constant.
3. Removed the duplicate manual handler for the `U` progression dialog.
   The key remains registered, and `GuiDialog` installs its own toggle handler
   during the normal client lifecycle.

## Expected result

The client should pass the block-texture/world connection phase without trying
to register a nonexistent HUD hotkey.
