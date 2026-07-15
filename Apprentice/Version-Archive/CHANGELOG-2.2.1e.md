# Apprentice 2.2.1e

- Removed all six `/0/enabled` through `/5/enabled` JSON patch operations.
- Added a complete game-domain class compatibility file containing the six
  vanilla class records in their original order with `enabled: false`.
- This remains valid even when an older development origin supplied an empty
  `game:config/characterclasses.json`, so the repeated red patch errors cannot
  occur.
- Retains the 2.2.1d server-side invalid-class migration that prevents the
  `.charsel` crash and opens normal race selection for affected saves.
- Preserves the approved 2.1.14i mastery/skill-tree GUI.
