# Apprentice 2.2.1d

- Fixed the `.charsel` crash on worlds whose player still had a vanilla or
  removed mod-class code saved in `characterClass`.
- Added a server-side join migration that clears only invalid legacy class
  selection data. Existing valid Apprentice race selections are preserved.
- New players are not modified by the migration.
- Restored the base-game character-class asset and now hides its six entries
  with focused `enabled` patches. This removes the unsafe empty game-domain
  asset override used by the earlier 2.2 builds.
- The repaired player is sent through Vintage Story's normal character-creation
  flow, so race traits and appearance are applied by the base game.
