# Apprentice 2.1.14f — Larger Fixed Dialog

## Interface

- Enlarged the fixed Apprentice content area from `1120 x 682` to
  `1280 x 780` dialog units.
- Preserved the original `1120 x 682` design aspect ratio, so the whole
  interface grows uniformly instead of stretching horizontally or vertically.
- Kept sizing independent of the game-window and render-frame dimensions.
- Kept the three top-level tabs compact; they do not span the window.
- Kept the third tab label as **HIDDEN** while leaving the Hidden page title,
  class names, IDs, and mechanics unchanged.
- The complete canvas transform continues to cover tabs, panels, text, icons,
  spacing, hit regions, middle-button dragging, and tree zoom targeting.

## Compatibility

- Existing saves remain compatible.
- Tree IDs, node IDs, hidden-class IDs, and tab IDs are unchanged.
