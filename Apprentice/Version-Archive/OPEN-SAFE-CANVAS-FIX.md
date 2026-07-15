# Open-safe interactive canvas - version 2.1.3

## Runtime symptom

The client terminated when pressing `U`.

## Regression found

The canvas texture was generated during normal GUI composition:

```csharp
ComposeElements(...)
{
    RebuildTexture();
}
```

Then the dialog generated the complete texture a second time from:

```csharp
OnGuiOpened()
{
    canvas.Refresh();
    base.OnGuiOpened();
}
```

That second call happened while `GuiDialog.TryOpen()` was actively opening and
registering the dialog. It repeated the unsafe rebuild-during-open pattern that
caused earlier Apprentice GUI crashes.

## Fix

The `OnGuiOpened` override has been removed.

The initial texture is now generated exactly once through `ComposeElements`.
Later redraws happen only after real state changes such as selection, hover,
zoom, panning, XP sync, or a purchase response.

Rendering now uses the protected base helper:

```csharp
Render2DTexture(
    canvasTexture.TextureId,
    Bounds
);
```

instead of directly calling the render API.

## Diagnostic stages

The first composition writes:

```text
Interactive canvas: ComposeElements begin.
Interactive canvas: bounds calculated.
Interactive canvas: surface size ...
Interactive canvas: Cairo context ready.
Interactive canvas: Cairo drawing complete.
Interactive canvas: GPU texture upload complete.
Interactive canvas: ComposeElements complete.
```

If another native failure occurs, the final line in `client-main.log` will show
the exact boundary.
