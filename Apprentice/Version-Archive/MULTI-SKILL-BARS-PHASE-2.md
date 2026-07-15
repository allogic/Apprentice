# Multi-skill XP bars — Phase 2

Version: 1.0.5

## Behavior

### Normal XP gain

A compact progress row appears in the top-left:

```text
Miner — Level 2
+1 XP   •   8.4 / 20 XP
[=======================-----]
```

The bar animates from the previous total to the new total, remains visible for
the configured hold duration, and then disappears.

### Level-up

No progress row is shown for that packet. Vintage Story's built-in discovery
notification is used instead:

```text
Miner — LEVEL UP!
Level 1 → 2
```

### Multiple skills

Every class that receives XP gets its own simultaneous progress row. Packets no
longer replace each other or wait behind one shared bar.

If several classes level up together, their discovery messages are queued and
shown one after another.

## Important crash fix

Custom GUI creation and updates now run through:

```csharp
api.Event.EnqueueMainThreadTask(...)
```

The old versions accessed popup GUI code directly from the network packet
handler. This build moves all GUI work onto the Vintage Story client main
thread.

## Test plan

1. Confirm the startup marker:

```text
[Apprentice] Client startup: begin — MultiSkillBars Phase 2, version 1.0.5.
```

2. Gain normal XP without leveling:
   - top-left bar appears;
   - skill name and current level are correct;
   - progress animates and disappears.

3. Gain XP in multiple matching skills:
   - one row appears for every rewarded skill;
   - all rows remain visible simultaneously.

4. Cross a level threshold:
   - no normal bar appears for that packet;
   - the discovery message displays the level-up.

5. Press U:
   - stored XP matches the popup values.

6. Reconnect:
   - XP remains stored.
