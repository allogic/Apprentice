# LoadedTexture ref compile fix - version 2.1.2

## Compiler error

```text
CS0192:
Ein schreibgeschütztes Feld kann (außer in einem Konstruktor)
nicht als ref- oder out-Wert verwendet werden.
```

The failing call was:

```csharp
generateTexture(
    surface,
    ref canvasTexture
);
```

`generateTexture` requires the texture variable by `ref`, but the field was
declared `readonly`.

## Fix

Changed:

```csharp
private readonly LoadedTexture canvasTexture;
```

to:

```csharp
private LoadedTexture canvasTexture;
```

No GUI behavior, skill logic, balance, or networking was changed.
