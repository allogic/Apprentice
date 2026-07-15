# Interactive Skill Tree Canvas - version 2.1.0

## Architecture

The skill-tree screen now uses one custom `GuiElementSkillTreeCanvas`.

The composer contains only:

- one shaded dialog background;
- one normal title bar;
- one interactive canvas.

The canvas owns all drawing and input. It does not create a separate GUI
element for every class or node.

## Visual structure

The canvas draws four regions:

1. Header
   - class identity;
   - level;
   - available and spent points;
   - mastery rank;
   - textured XP progress bar.

2. Profession navigation
   - Combat;
   - Gathering;
   - Crafting;
   - Production;
   - class list with live class levels.

3. Skill tree
   - curved prerequisite paths;
   - circular foundation/expert nodes;
   - diamond specialization nodes;
   - large octagonal capstone;
   - selected-path highlighting.

4. Node details
   - description;
   - current and next-rank effects;
   - live requirement checks;
   - purchase state;
   - server feedback.

## Node states

- dark gray: locked;
- gold: available;
- teal: purchased;
- bright teal: maximum rank;
- red: blocked by an exclusive specialization;
- gold glow: selected or available;
- large octagon: capstone.

## Interaction

- hover classes and nodes for visual feedback;
- left-click categories, classes, nodes, purchase, and reset;
- mouse wheel over the tree to zoom;
- middle-mouse drag to pan;
- server-authoritative skill purchases;
- purchase-result feedback without rebuilding the composer.

## Crash-safety rules

The 2.0.4 safety rules remain:

- no `GuiElementStatbar`;
- no composer reconstruction while open;
- no disposal of the active composer;
- no large collection of individual buttons;
- one bounded 1120 x 682 texture;
- texture redraw only when state, hover, pan, or zoom changes.

## Startup marker

```text
[Apprentice] Client startup: begin - Interactive SkillTree Canvas, version 2.1.0.
```
