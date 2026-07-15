# Lazy UI client hotfix

The current logs show:

1. Only one Apprentice mod is now loaded.
2. The client reaches `Starting system: ApprenticeModSystem`.
3. Logging stops inside Apprentice client startup.
4. The old `client-crash.log` is not updated.

This hotfix removes all GUI composition from the connection phase.

- The progression dialog is created only when U is pressed.
- The XP HUD is created only when the first XP notification arrives.
- Both GUI paths catch and log normal exceptions.
- Client startup contains explicit `[Apprentice]` checkpoints.

After rebuilding, `client-main.log` should contain:

```text
[Apprentice] Client startup: begin.
[Apprentice] Client startup: class config ready.
[Apprentice] Client startup: network ready.
[Apprentice] Client startup: base config ready.
[Apprentice] Client startup: hotkey ready.
[Apprentice] Client startup: lazy HUD ready.
[Apprentice] Client startup: complete.
```
