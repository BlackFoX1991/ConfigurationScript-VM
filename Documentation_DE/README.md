# ConfigurationScript Dokumentation

Wenn du CFGS zum ersten Mal oeffnest, lies die Seiten am besten in dieser Reihenfolge.

1. [Einstieg und Ausfuehrung](01_einstieg_und_ausfuehrung.md)
2. [Sprache und Daten](02_sprache_und_daten.md)
3. [Kontrollfluss und Fehler](03_kontrollfluss_und_fehler.md)
4. [Funktionen und Aufrufe](04_funktionen_und_aufrufe.md)
5. [Match, Destructuring und Out](05_match_destructuring_und_out.md)
6. [Klassen, Enums, Namespaces und Vererbung](06_klassen_enums_namespaces_und_vererbung.md)
7. [Module, Importe und Exporte](07_module_importe_und_exporte.md)
8. [Async, Await und Yield](08_async_await_und_yield.md)
9. [Standardbibliothek](09_standardbibliothek.md)
10. [HTTP und SQL Plugins verwenden](10_http_und_sql_plugins.md)
11. [Plugins erstellen](11_plugins_erstellen.md)

## Vollstaendiger Feature Index

Die folgenden Themen werden in den Dokumenten komplett abgedeckt.

- Sprachgrundlagen. Kommentare. Literale. Zahlenformate. Strings. Chars. Wahrheitswerte. `null`.
- Variablen und Konstanten. `var`. `const`. Scopes. Schattenbildung. Top Level Regeln.
- Datentypen. Arrays. Dictionaries. Strings. Klasseninstanzen. Enums. DateTime. DirectoryInfo. FileHandle. Exception Objekte. Tasks.
- Operatoren. Arithmetik. Vergleiche. Logik. Bitoperatoren. Potenz. Inkrement. Dekrement. Compound Assignments. Ternary. Null Coalescing.
- Datensyntax. Array Literale. Dictionary Literale. Dot Zugriff. Index Zugriff. Slicing. Slice Replace. Push Syntax. Delete Syntax.
- Kontrollfluss. `if`. `else`. `while`. `do while`. `for`. `foreach`. `break`. `continue`.
- Fehlerbehandlung. `try`. `catch`. `finally`. `throw`.
- Funktionen. Funktionsdeklarationen. Funktionsausdruecke. Closures. Rueckgabewerte. Default Parameter. Named Arguments. Rest Parameter. Spread Arguments. Destructuring Parameter.
- Pattern Matching. `match` als Statement. `match` als Ausdruck. Guards. Wildcards. Literalmuster. Array Muster. Dictionary Muster. `var` Bindungen.
- Destructuring. Deklarationen. Zuweisungen. Parameter. `foreach` Muster.
- `out` Bloecke als Ausdrucksform.
- OOP. Klassen. Konstruktoren. `init`. Objektinitialisierung. Instanzmitglieder. Statische Mitglieder. Sichtbarkeiten. Vererbung. `super`. `type`. `this`. `outer`. Nested Classes. Enums in Klassen. Override Regeln.
- Namespaces. Qualifizierte Namen. Mehrfachdeklarationen. Namensaufloesung.
- Module. `export`. Bare Imports. Named Imports. Alias Importe. Namespace Importe. Einzelsymbol Importe. Dateiimporte. URL Importe. DLL Importe. Header Regeln. Import Aufloesung. Zyklen.
- Async Modell. `async func`. `await`. `yield`. Top Level Await in Ausdruecken. Heisse Starts. Await auf Listen und Dictionaries. Await auf Task und ValueTask aus Plugins.
- Standardbibliothek. Alle eingebauten Builtins. Alle String, Array, Dictionary, DateTime, DirectoryInfo, FileHandle, Exception und Task Intrinsics.
- Offizielle Plugins im Repo. Standard Library. HTTP. SQL.
- Plugin Entwicklung. `IVmPlugin`. `BuiltinDescriptor`. `IntrinsicDescriptor`. Attributbasierte Registrierung. `BuiltinAttribute`. `IntrinsicAttribute`. `SmartAwait`. `NonBlocking`. Packaging und Import.

## Schnellstes moegliches Beispiel

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

func main() {
    print("Hallo aus CFGS");
}

main();
```

Wenn du direkt in die Plugin Themen springen willst, nimm diese beiden Seiten.

- [HTTP und SQL Plugins verwenden](10_http_und_sql_plugins.md)
- [Plugins erstellen](11_plugins_erstellen.md)
