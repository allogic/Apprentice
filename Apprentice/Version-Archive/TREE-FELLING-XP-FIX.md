# Tree-felling XP fix — version 1.2.3

## Previous behavior

Vintage Story's axe discovers and breaks a complete tree inside
`ItemAxe.OnBlockBrokenWith`. Apprentice's generic `DidBreakBlock` adapter
therefore produced only one Woodworker reward in the reported test.

## New behavior

A dedicated Harmony hook observes the completed axe tree-felling operation.

```text
1 actually broken wood block = 1 configured log reward
Maximum rewarded units per tree = 20
```

With the current Woodworker configuration:

```json
"game:log-*": 1.0
```

the result is:

```text
11 broken wood blocks = 11 XP
20 broken wood blocks = 20 XP
35 broken wood blocks = 20 XP
```

## Accuracy and duplication protection

Before the tree is felled, Apprentice records every discovered tree position.
After Vanilla finishes:

- only positions whose original block was wood are counted;
- only wood blocks that were actually removed are counted;
- leaves do not increase XP;
- the reward is capped at 20 units;
- all blocks from the same tree-felling operation are suppressed in the
  ordinary `DidBreakBlock` handler, preventing duplicate XP.

A manually broken single log still uses the normal one-block reward.
