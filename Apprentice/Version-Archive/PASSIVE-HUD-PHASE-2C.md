# Passive XP HUD — Phase 2C

Version: 1.0.7

Version 1.0.6 displayed the XP rows correctly, but Vintage Story treated the
open GuiDialog as a normal menu and suppressed gameplay controls.

## Fix

The same working text-bar display is now declared as:

```csharp
public override EnumDialogType DialogType =>
    EnumDialogType.HUD;
```

Additional pass-through safeguards:

```csharp
public override bool Focusable => false;
public override bool PrefersUngrabbedMouse => false;
public override bool DisableMouseGrab => false;
public override double InputOrder => 9999;

public override bool ShouldReceiveMouseEvents() => false;
public override bool ShouldReceiveKeyboardEvents() => false;
public override bool CaptureAllInputs() => false;
public override bool CaptureRawMouse() => false;
```

The HUD is also explicitly unfocused after opening.

## Expected behavior

While the XP display is visible, the player can still:

- walk and sprint;
- move the camera;
- mine blocks;
- interact with blocks and items;
- use the hotbar;
- open normal game interfaces.

Normal XP still shows all rewarded skills together. Level-ups still use the
built-in discovery message.

## Startup marker

```text
[Apprentice] Client startup: begin — PassiveHud Phase 2C, version 1.0.7.
```
