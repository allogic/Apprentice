# Native events compile hotfix — 1.1.1

## Fixed error

```text
The type or namespace name "EntityDeathInteractionAdapter" could not be found.
```

`InteractionEventBridge.cs` was compiled, but the separate
`EntityDeathInteractionAdapter.cs` file was not included by the user's local
project configuration.

## Robust fix

`EntityDeathInteractionAdapter` now lives inside
`InteractionEventBridge.cs` as a second class in the same namespace.

The standalone source file was removed, so:

- the adapter cannot be accidentally excluded while the bridge is included;
- no extra `.csproj` Compile entry is needed;
- no duplicate class definition is possible.

No gameplay behavior was changed.
