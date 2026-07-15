# Build note

The project is updated to release 2.2.2 (`2.2.2` in `modinfo.json`).

For a clean local build, set `VINTAGE_STORY` to the Vintage Story 1.22.3
installation directory and run:

```powershell
dotnet build Apprentice.csproj -c Debug
```

The C# source retains the complete approved 2.1.14i interface baseline. This
includes the compact tabs, whole-dialog scaling, fixed larger content area,
raised skill-tree labels, centered profession names, and centered level labels.

The race foundation is implemented through content assets, language files,
and the built-in character-trait system. A complete game-domain compatibility
file provides the six disabled vanilla records, while the old index patch is
inert. A small
server-side migration clears invalid legacy class-selection state before the
base CharacterSystem handles player join; valid Apprentice race selections
are preserved.

Race-specific player rendering is compiled into this milestone. A hidden
`apprenticerace` skin part is added to the player entity and synchronized with
the selected race by `RaceAppearanceSystem`. Each race has its own additive
shape, texture palette, visual proportions and named attachment anchors. The
shapes extend the stock Seraph skeleton rather than replacing it, which keeps
the normal animation, clothing, armor, held-item, first-person and third-person
rendering pipelines intact.
