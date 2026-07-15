# GlobalConstants compile fix — version 2.0.1

Fixed compiler error in `SkillMasteryPatches.cs`, line 42:

```text
Der Name "GlobalConstants" ist im aktuellen Kontext nicht vorhanden.
```

`GlobalConstants` belongs to:

```csharp
Vintagestory.API.Config
```

Added:

```csharp
using Vintagestory.API.Config;
```

The recipe-lock notification remains:

```csharp
serverPlayer.SendMessage(
    GlobalConstants.GeneralChatGroup,
    $"This recipe requires the Apprentice capstone '{requiredNode}'.",
    EnumChatType.Notification
);
```

No skill-tree behavior or balance values were changed.
