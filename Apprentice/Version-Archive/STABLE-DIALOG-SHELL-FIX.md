# Stable dialog-shell fix - version 2.1.4

## What the log proved

The 2.1.3 log ended after:

```text
Interactive skill-tree GUI: composition begin.
```

It did not reach:

```text
Interactive canvas: ComposeElements begin.
```

Therefore the hard failure happened before the canvas drawing method, Cairo
surface creation, or GPU texture upload.

## Bounds regression

The 2.1.x dialog used:

- a fixed root dialog;
- a background configured as `Fill` and `FitToChildren`;
- the same `canvasBounds` instance both as the background sizing child and as
  the custom element's own bounds.

The working Compact GUI 2.0.4 instead used:

- `ElementStdBounds.AutosizedMainDialog`;
- a dedicated content bounds object for background sizing;
- separate bounds for every actual GUI element.

## Fix

Version 2.1.4 restores the proven shell pattern:

```csharp
ElementBounds dialogBounds =
    ElementStdBounds
        .AutosizedMainDialog
        .WithAlignment(EnumDialogArea.CenterMiddle);

ElementBounds contentBounds =
    ElementBounds.Fixed(0, 40, 1120, 682);

ElementBounds canvasBounds =
    ElementBounds.Fixed(0, 40, 1120, 682);

backgroundBounds.WithChildren(contentBounds);
```

`contentBounds` and `canvasBounds` are now different objects.

## Composer stage diagnostics

The constructor now logs every stage separately:

```text
creating canvas object
canvas object ready
creating composer
composer created
background added
title bar added
canvas element added
composer Compose begin
Interactive canvas: ComposeElements begin
...
composer Compose complete
```

This removes ambiguity if another native boundary fails.
