# Race Design — 2.2.0 Foundation

## Scope

This milestone replaces the visible vanilla character-class roster with nine
mod-owned fantasy races while preserving all Apprentice professions, skill
trees, hidden classes, and progression keys.

The initial roster follows the nine species in the 2024 D&D Basic Rules:
Dragonborn, Dwarf, Elf, Gnome, Goliath, Halfling, Human, Orc, and Tiefling.
Descriptions and mechanical themes are paraphrased and adapted to Vintage
Story rather than copied from tabletop rules.

## Balance approach

- Every race has one positive and one negative trait.
- Existing Vintage Story character attributes are used so the server applies
  the modifiers through its normal character-class system.
- Bonuses are intentionally modest. Strong combat or health bonuses are paired
  with hunger, speed, accuracy, or size-related drawbacks.
- Apprentice profession IDs are not race IDs. A Dwarf can still progress every
  profession, including Miner, Warrior, Cook, and Tailor.

## Player-model boundary

Distinct race models are deferred until the model pipeline can be validated
against the installed game assets. Each model must preserve:

- the complete player animation hierarchy;
- armor and clothing attachment points;
- held-item and tool transforms;
- first- and third-person camera behavior;
- hitbox and eye-height agreement;
- multiplayer visibility and reconnect persistence.

Scaling only the rendered mesh would be incorrect because it would leave the
camera, collision box, clothing, and held items at the default human size.
