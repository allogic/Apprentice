# Experience equation fix — version 1.0.2

The former sine/inverse-sine equation used values around 98 billion and then
subtracted nearly equal numbers. That caused catastrophic floating-point
precision loss. Small totals such as 10.4 XP could incorrectly resolve to
levels around 80.

The replacement uses an exact piecewise arithmetic curve.

## XP needed from each current level

```text
Level 1  -> 2:  10 XP
Level 2  -> 3:  20 XP
Level 3  -> 4:  30 XP
...
Level 9  -> 10: 90 XP
Level 10 -> 11: 95 XP
Level 11 -> 12: 100 XP
Level 12 -> 13: 105 XP
...
Level 100 -> 101: 545 XP
```

The curve continues beyond level 100 using the same +5 XP pattern.

## Total XP examples

```text
0 XP     = Level 1
10 XP    = Level 2
10.4 XP  = Level 2, with 0.4 / 20 XP progress
11.4 XP  = Level 2, with 1.4 / 20 XP progress
30 XP    = Level 3
450 XP   = Level 10
545 XP   = Level 11
```

## Public methods

```csharp
GetXpRequiredForNextLevel(currentLevel)
GetTotalExpRequiredForLevel(level)
GetLevel(totalExp)
GetProgress(totalExp)
GetExpUntilNextLevel(totalExp)
```

`GetRequiredExp(level)` remains as a compatibility alias for the cumulative
threshold function.
