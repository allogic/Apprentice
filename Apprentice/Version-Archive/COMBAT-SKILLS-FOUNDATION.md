# Combat skills foundation — version 1.3.0

## Skill model

### Hunter

Hunter remains victim-based. Its existing KillPvE patterns still inspect the
defeated creature code.

### Warrior

Warrior now receives weapon-based kill XP for one-handed melee families:

```text
sword
falx
knife
axe
club
mace
cleaver
sabre
```

### Spearman

Spearman receives weapon-based kill XP for:

```text
spear
javelin
pike
```

Both melee and thrown spear kills are supported.

### Ranger

Ranger receives weapon-based kill XP for:

```text
bow
crossbow
sling
arrow fallback
bolt fallback
```

Projectile attribution first checks the projectile's WeaponStack, then its
ProjectileStack, then the projectile entity code.

### Shield

Shield receives one configured reward whenever a held shield's durability
decreases during a damage event.

This means the block must actually succeed. Merely holding or raising a shield
does not grant XP.

Active and passive successful shield protection are both eligible because the
test is based on the real durability result.

### Tank

Tank receives XP equal to actual health lost after the game's damage delegates:

```text
3.5 health lost = 3.5 Tank XP
```

The default config uses one XP per health point. Environmental damage such as
falls also counts because the requested rule is damage taken, not only combat
damage.

A completely blocked hit gives Shield XP but no Tank XP. A partially blocked
hit can give both Shield XP and Tank XP for the remaining health loss.

## Independent rewards

One action can reward several skills:

```text
Kill a hare using a bow:
- Hunter: victim reward
- Ranger: weapon reward

Kill an enemy using a sword while carrying a shield:
- Warrior: kill reward
- Shield: rewards only for successful blocks during the fight
```

## New interaction names

```text
WeaponKillPvE
WeaponKillPvP
ShieldBlock
DamageTaken
```

Existing `KillPvE` and `KillPvP` remain victim-based.

## Attribution lifetime

The most recent damaging player/weapon pair is remembered for up to two minutes
per victim. This helps attribute delayed deaths while preventing permanent
stale records.

## Test checklist

1. Confirm startup marker:

```text
[Apprentice] Client startup: begin — CombatSkills Foundation, version 1.3.0.
```

2. Confirm the server reports the health-damage Harmony hook.

3. Kill an animal with:
   - sword or falx -> Warrior;
   - spear in melee -> Spearman;
   - thrown spear -> Spearman;
   - bow -> Ranger;
   - sling -> Ranger.

4. Repeat against another player to test WeaponKillPvP.

5. Kill a configured Hunter creature with a ranged weapon:
   - Hunter and Ranger should both gain XP.

6. Successfully block damage with a shield:
   - Shield gains one configured reward;
   - Tank gains only the health damage that still passed through.

7. Take 3.5 actual damage without blocking:
   - Tank gains 3.5 XP.

8. Raise a shield without being hit:
   - no Shield XP.

9. Receive a hit that the shield fails to block:
   - no Shield XP;
   - Tank receives actual health loss.

## Mod compatibility

Weapon classification is controlled entirely through class.json target-code
patterns. Additional modded weapon families can be added without changing C#.
