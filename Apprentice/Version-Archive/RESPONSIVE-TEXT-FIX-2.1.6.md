# Apprentice 2.1.6 — responsive text fix

- Text size is calculated from the current game framebuffer and canvas size.
- Vintage Story GUI scaling is normalized so fonts and fixed canvas coordinates use the same scale.
- The header subtitle and XP line are merged, removing the top-left overlap.
- Long class/node/sidebar labels shrink to fit their available width.
- Wrapped text and right/center alignment use the same scaled-font measurements as drawing.
