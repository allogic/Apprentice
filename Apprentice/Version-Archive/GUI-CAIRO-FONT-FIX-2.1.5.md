# GUI Cairo font fix - version 2.1.5

## Root cause

`TextDrawUtil.DrawTextLine()` does not call `CairoFont.SetupContext()`.
Version 2.1.4 cloned a font, called `DrawTextLine()`, and then disposed the
clone while its internal `CairoFontOptions` field was still null. This threw a
`NullReferenceException` during the first header draw and left the dialog body
blank.

The canvas also disposed five template fonts during shutdown. A template that
had never run `SetupContext()` failed for the same reason.

## Fix

- Every one-line draw now uses a temporary font clone.
- The clone calls `SetupContext(ctx)` before `DrawTextLine()`.
- The Cairo context is saved/restored around each draw.
- Text measuring and wrapped text use temporary initialized clones.
- Template fonts are no longer disposed because they never own native Cairo
  resources.
- Canvas disposal is idempotent.

The mod metadata and startup log are bumped to 2.1.5.
