# Chat-safe XP hotfix 1.0.1

## What the logs showed

- Apprentice client startup completed.
- Breaking a matching block was the first time the XP notification path ran.
- `client-crash.log` did not receive a new managed exception.
- The client still had both `apprentice_1.0.0.zip` and the development `mod`
  folder installed.
- The previous `false` notification setting was checked too late: the custom
  HUD constructor ran before the setting was checked.

## What this build changes

- Version is now `1.0.1`, so it wins over an old `1.0.0` ZIP.
- `OverlayManager` never constructs `ExperienceNotificationHud`.
- XP gains are displayed through `api.ShowChatMessage`.
- When `EnableExperienceNotifications` is false, packet handling returns before
  touching any client UI.
- Packet handling has an outer exception guard.

## Expected log marker

```text
[Apprentice] Client startup: begin — ChatSafeHotfix 1.0.1.
```

## Test

1. Build this project.
2. Start the game and confirm the marker above appears in `client-main.log`.
3. Break sand.
4. The client should remain connected.
5. With notifications enabled, chat should show the Miner XP gain.
6. Press U and confirm the stored Miner XP increased.

Once this path is stable, the animated HUD can be rebuilt separately without
risking the server-authoritative progression system.
