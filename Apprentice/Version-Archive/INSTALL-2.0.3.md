# Required installation for version 2.0.3

The uploaded logs showed that Vintage Story still loaded:

```text
MasterySkillTree Compile Fix, version 2.0.1
```

and reported two copies of the same mod:

```text
apprentice_2.0.1.zip
mod
```

Because both copies were version 2.0.1, Vintage Story selected the old ZIP.

## Installation

1. Close Vintage Story.
2. Replace the complete Visual Studio project with this 2.0.3 project.
3. Delete the project's `bin` and `obj` folders.
4. Delete `apprentice_2.0.1.zip` from both:
   - `%appdata%\Vintagestory\Mods`
   - `%appdata%\VintagestoryData\Mods`
5. Rebuild the project.
6. Start Vintage Story.

Before pressing U, client-main.log must contain:

```text
[Apprentice] Client startup: begin — MasterySkillTree GUI Safe Forced Build, version 2.0.3.
```

Do not test the GUI when the log says version 2.0.1 or 2.0.2.
