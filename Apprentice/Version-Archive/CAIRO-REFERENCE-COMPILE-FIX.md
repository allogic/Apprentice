# Cairo compile-reference fix - version 2.1.1

## Compiler symptom

Visual Studio reported `CS0246` for:

```text
Cairo
Context
ImageSurface
```

The repeated `Context` errors were cascading from the first missing assembly
reference.

## Fix

Added this direct project reference:

```xml
<Reference Include="cairo-sharp">
  <HintPath>$(VINTAGE_STORY)/Lib/cairo-sharp.dll</HintPath>
  <Private>false</Private>
</Reference>
```

The interactive canvas directly uses the `Cairo` namespace, so referencing
`VintagestoryAPI.dll` alone is not enough for compilation.

Also changed:

```csharp
Format.Argb32
```

to:

```csharp
Format.ARGB32
```

to match the current Cairo API spelling used by Vintage Story's source.

## Expected location

With `VINTAGE_STORY` pointing at the Vintage Story installation folder, the
assembly should resolve from:

```text
$(VINTAGE_STORY)/Lib/cairo-sharp.dll
```

No GUI behavior or skill balance was changed.
