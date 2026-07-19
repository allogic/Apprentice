# Apprentice

Apprentice is a Vintage Story progression and character-creation mod by
Kanista. It provides profession experience, skill trees, fantasy races and
subclasses, scalable bodies, persistent appearance choices, horns, teeth and
race-aware skin palettes. Version 2.7.0 adds Grandmaster artifacts and hidden
discoveries, exact-charge Starsteel/Aethersteel progression, fictional
arrow-only poison, persistent danger regions, a cached map heatmap and
Apprentice-owned danger ecology.

## Requirements

- Vintage Story 1.22.3 or newer compatible release
- .NET SDK matching the target framework in `Apprentice/Apprentice.csproj`
- A `VINTAGE_STORY` environment variable pointing to the game installation

## Installation

Download the release ZIP and place it in the Vintage Story `Mods` directory.
Do not extract individual files from the archive: `assets`, `modinfo.json` and
`Apprentice.dll` must remain at the archive root.

Source archives are not installable mods because they do not contain a compiled
`Apprentice.dll`. When replacing a hotfix with the same version number, remove
every older Apprentice ZIP/folder from `Mods`, install the newly built release
ZIP, and fully restart the game; loaded mod assemblies are not hot-replaced.

## Development

Validate all repository assets without a game installation:

```sh
python tools/validate_assets.py
```

Build and package with the configured Vintage Story installation:

```sh
dotnet run --project CakeBuild -- --target=Package
```

The package is written to `Releases/apprentice_<version>.zip`. Existing release
versions are preserved; rebuilding the same version replaces only that archive.

## Project structure

- `Apprentice/src`: runtime systems, interaction adapters and GUI code
- `Apprentice/assets/apprentice`: configuration, patches, shapes and textures
- `CakeBuild`: validation, build and release packaging
- `tools/validate_assets.py`: cross-file asset and configuration validation
- `docs`: reference tables and the release checklist

## Testing

Before releasing, complete [the release checklist](docs/RELEASE_CHECKLIST.md).
Race persistence and interaction rewards must be tested in both single-player
and a separated local client/server session.

## Design and handoff documents

- [Current 2.7.0 developer handoff](docs/RELEASE-HANDOFF-2.7.0.md)
- [2.2.4 customization handoff](docs/DEVELOPER-HANDOFF-2.2.4.md)
- [Stability and content milestones](docs/GRANDMASTER-CONTENT-DESIGN.md)
- [Optional server-mod compatibility audit](docs/SERVER-MOD-COMPATIBILITY.md)

## Repository

https://github.com/allogic/Apprentice
