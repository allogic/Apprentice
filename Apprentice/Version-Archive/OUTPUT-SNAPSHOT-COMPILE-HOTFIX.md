# CompletionEvents Compile-Hotfix 1.2.1

Behobener Compilerfehler:

```text
Die beste Überladung für "OutputSnapshot" enthält keinen Parameter
mit dem Namen "code".
```

`OutputSnapshot` ist definiert als:

```csharp
internal sealed record OutputSnapshot(
    string? Code,
    int StackSize
);
```

Die benannten Argumente wurden fälschlicherweise klein geschrieben:

```csharp
new OutputSnapshot(
    code: null,
    stackSize: 0
);
```

C# unterscheidet bei benannten Argumenten Groß- und Kleinschreibung.

Der Aufruf verwendet nun robuste positionsbasierte Argumente:

```csharp
new OutputSnapshot(
    null,
    0
);
```

Am Verhalten der Interaktionen wurde nichts geändert.
