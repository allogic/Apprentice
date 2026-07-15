# Tank entity-damage restriction — version 1.3.1

Tank XP is now awarded only when actual health loss was caused by an entity.

## Counts

- hostile animals;
- drifters and other creatures;
- players;
- melee attacks;
- arrows and other projectiles;
- thrown weapons.

Projectile damage counts because Apprentice first resolves
`DamageSource.GetCauseEntity()` and then falls back to `SourceEntity`.

## Does not count

- starvation;
- fall damage;
- drowning;
- temperature damage;
- environmental damage;
- other damage with no entity source.

## Shield interaction

A fully blocked entity attack can still award Shield XP while awarding no Tank
XP because no health was lost.

A partially blocked entity attack can award:

```text
Shield XP
Tank XP for the remaining health loss
```

Environmental damage does not award Shield or Tank XP unless the game reports
a real entity source.
