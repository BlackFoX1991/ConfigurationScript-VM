# Plugins erstellen

## Das Plugin Modell von CFGS

Ein Plugin ist eine .NET Assembly, die der CFGS Runtime neue Builtins und oder neue Intrinsics beibringt. Die Runtime kennt dafuer zwei Wege.

1. Manuelle Registrierung ueber `IVmPlugin`.
2. Attributbasierte Registrierung ueber `BuiltinAttribute` und `IntrinsicAttribute`.

Beide Wege koennen in derselben Assembly gleichzeitig verwendet werden.

## Wie Plugins geladen werden

Ein Skript laedt ein Plugin ueber einen normalen DLL Import.

```cfs
import "dist/Debug/net10.0/MyPlugin.dll";
```

Beim Laden passiert Folgendes.

1. Die Assembly wird ueber einen eigenen Load Context geladen.
2. Alle nicht abstrakten Typen, die `IVmPlugin` implementieren, werden instanziiert.
3. Ihre `Register` Methode wird aufgerufen.
4. Danach scannt der Loader statische Methoden mit `BuiltinAttribute` und `IntrinsicAttribute`.
5. Registrierungen werden zunaechst gestaged.
6. Wenn etwas in der Registrierung fehlschlaegt, wird nichts teilweise uebernommen.

Das ist ein wichtiges Qualitaetsmerkmal. Ein Plugin, das in der Mitte abstuerzt, hinterlaesst nicht versehentlich halbfertige Builtins.

## Projektdatei fuer ein Plugin

Ein minimales Pluginprojekt sieht so aus.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\\CFGS_NE\\CFGS_VM.csproj" />
  </ItemGroup>
</Project>
```

Wenn dein Plugin eigene Abhaengigkeiten mitbringt, ist `CopyLocalLockFileAssemblies` oft sinnvoll, damit alle benoetigten DLLs gemeinsam ausgegeben werden.

## Weg 1. `IVmPlugin`

Das direkte Interface ist die klarste Form.

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public sealed class DemoPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        builtins.Register(new BuiltinDescriptor(
            "demo_sum",
            2,
            2,
            (args, instr) => Convert.ToInt32(args[0]) + Convert.ToInt32(args[1])
        ));

        intrinsics.Register(typeof(string), new IntrinsicDescriptor(
            "demo_wrap",
            2,
            2,
            (recv, args, instr) =>
            {
                string left = args[0]?.ToString() ?? "";
                string right = args[1]?.ToString() ?? "";
                return left + (recv?.ToString() ?? "") + right;
            }
        ));
    }
}
```

### `BuiltinDescriptor`

Ein Builtin Descriptor hat diese Schluesseldaten.

- Name
- minimale Arity
- maximale Arity
- Delegate fuer den eigentlichen Aufruf
- optional `smartAwait`
- optional `nonBlocking`

### `IntrinsicDescriptor`

Ein Intrinsic Descriptor ist dasselbe Prinzip, aber mit Receiver Typ.

- Receiver Type
- Name
- minimale Arity
- maximale Arity
- Delegate mit `receiver`, `args`, `instr`
- optional `smartAwait`
- optional `nonBlocking`

## Weg 2. Attribute

Attribute sind angenehm, wenn du viele kleine Helfer an statischen Methoden haengen willst.

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public static class DemoAttributed
{
    [Builtin("edge_attr_task_value", 1, 1)]
    private static Task<object?> EchoTask(List<object> args, Instruction instr)
    {
        object? value = args[0];
        return Task.FromResult(value);
    }

    [Intrinsic(typeof(string), "demo_suffix", 1, 1)]
    private static object AddSuffix(object recv, List<object> args, Instruction instr)
    {
        return (recv?.ToString() ?? "") + (args[0]?.ToString() ?? "");
    }
}
```

Die Methodensignaturen muessen dabei praktisch so aussehen.

- Builtin Methode. `static object Method(List<object> args, Instruction instr)`
- Intrinsic Methode. `static object Method(object recv, List<object> args, Instruction instr)`

Rueckgabetypen duerfen auch awaitbar sein. Der Loader erkennt `Task`, `Task<T>`, `ValueTask` und `ValueTask<T>`.

## `smartAwait`

`smartAwait` sagt der Runtime, dass das Ergebnis sinnvoll awaitbar behandelt werden darf. Bei den Attributen wird das ausserdem automatisch aktiviert, wenn der Rueckgabetyp ohnehin awaitbar ist.

Praktisch heisst das. Wenn dein Builtin ein `Task<T>` liefert, dann fuehlt es sich in CFGS direkt wie ein nativer awaitbarer Wert an.

## `nonBlocking`

`nonBlocking` ist fuer synchronen Code gedacht, den du bewusst aus dem direkten Ausfuehrungspfad auslagern willst. Die Runtime startet diesen Aufruf dann in einem Hintergrundtask.

Beispiel.

```csharp
[Builtin("edge_attr_nonblocking_sync", 2, 2, NonBlocking = true)]
private static object SlowCall(List<object> args, Instruction instr)
{
    Thread.Sleep(Convert.ToInt32(args[0]));
    return args[1];
}
```

Das ist kein Ersatz fuer echtes asynchrones I O. Es ist eher eine pragmatische Bruecke fuer blockierenden Code.

## Eigene Receiver Typen

Intrinsics muessen nicht auf primitive Typen beschraenkt sein. Du kannst auch eigene Handle Klassen einfuehren.

```csharp
public sealed class CacheHandle
{
    public Dictionary<string, object?> Data { get; } = new();
}
```

Dann registrierst du Intrinsics auf `typeof(CacheHandle)` und gibst ein `CacheHandle` Objekt aus einem Builtin zurueck.

Aus CFGS Sicht sieht das danach sehr natuerlich aus.

```cfs
var cache = make_cache();
cache.set("host", "localhost");
print(cache.get("host"));
```

## Plugin Abhaengigkeiten

Wenn dein Plugin weitere NuGet oder Projektabhaengigkeiten hat, muessen diese DLLs zur Laufzeit neben deinem Plugin verfuegbar sein. Sonst kann der Loader die Assembly zwar eventuell finden, aber einzelne Typen nicht aktivieren.

## Verpacken und Importieren

Der typische Ablauf ist simpel.

1. Plugin bauen.
2. Sicherstellen, dass die Ausgabe DLL und ihre Abhaengigkeiten an einem bekannten Ort liegen.
3. Im Skript per `import "Pfad\\MeinPlugin.dll";` laden.
4. Die registrierten Builtins oder Intrinsics wie normale Sprachfunktionen verwenden.

## Fehlerdiagnose

Wenn ein Plugin nicht sauber laedt, pruefe diese Punkte.

- Stimmt der DLL Pfad.
- Liegen alle Abhaengigkeiten daneben.
- Ist die Assembly wirklich fuer das aktuelle Runtime Ziel gebaut.
- Registriert das Plugin doppelte Namen.
- Wirft `Register` selbst eine Exception.

Fuer mehr Loader Details kannst du die Umgebungsvariable `CFGS_PLUGIN_VERBOSE` auf `1` oder `true` setzen.

## Designempfehlungen aus der Praxis

- Gib Builtins klare, verbale Namen.
- Verwende Intrinsics fuer Receiver zentriertes Verhalten.
- Nutze `Task` oder `ValueTask`, wenn der Call natuerlich async ist.
- Nutze `nonBlocking` nur fuer echte Notfaelle bei blockierendem Fremdcode.
- Registriere keine Namen, die mit vorhandenen Builtins kollidieren.
- Behandle Fehlermeldungen als Teil deiner Plugin API.

## Komplettes Mini Beispiel

### C#

```csharp
using CFGS_VM.VMCore.Plugin;
using CFGS_VM.VMCore.Command;

namespace Demo.Plugin;

public sealed class DemoPlugin : IVmPlugin
{
    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
    {
        builtins.Register(new BuiltinDescriptor(
            "demo_hello",
            1,
            1,
            (args, instr) => "Hallo " + (args[0]?.ToString() ?? "")
        ));

        intrinsics.Register(typeof(string), new IntrinsicDescriptor(
            "surround",
            2,
            2,
            (recv, args, instr) =>
            {
                string left = args[0]?.ToString() ?? "";
                string right = args[1]?.ToString() ?? "";
                return left + (recv?.ToString() ?? "") + right;
            }
        ));
    }
}
```

### CFGS

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/Demo.Plugin.dll";

print(demo_hello("Welt"));
print("core".surround("[", "]"));
```

Damit hast du die gesamte Plugin Oberflaeche des Projekts in der Hand. Wenn du danach echte Skripte bauen willst, ist [HTTP und SQL Plugins verwenden](10_http_und_sql_plugins.md) ein guter naechster Praxisstopp.
