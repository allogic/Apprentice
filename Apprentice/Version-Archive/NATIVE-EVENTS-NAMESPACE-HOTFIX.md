# Native events namespace hotfix — 1.1.2

## Fixed compiler errors

```text
The type or namespace name "Entity" could not be found.
The type or namespace name "DamageSource" could not be found.
```

When `EntityDeathInteractionAdapter` was merged into
`InteractionEventBridge.cs`, its original namespace imports were omitted.

Added:

```csharp
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
```

These imports provide:

- `DamageSource`
- `Entity`
- `EntityPlayer`

No event behavior changed.
