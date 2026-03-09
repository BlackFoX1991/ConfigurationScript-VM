# Module, Importe und Exporte

## Grundmodell

CFGS kennt zwei Importwelten.

1. Quellmodule aus `.cfs` Dateien.
2. Plugin Assemblies aus `.dll` Dateien.

Bei `.cfs` geht es um exportierte Sprachelemente. Bei `.dll` geht es um Runtime Erweiterungen wie Builtins und Intrinsics.

## Import Header Regel

Importe muessen im Header des Skripts stehen. Praktisch heisst das. Alle `import` Anweisungen gehoeren an den Anfang der Datei, bevor normale Logik beginnt.

Sauber.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import { add } from "math.cfs";

func main() {
    print(add(2, 3));
}
```

Nicht sauber waere ein Import mitten in einer Funktion oder nach bereits laufender Top Level Logik.

## Exportieren

Exportierbar sind diese Formen.

- `export const`
- `export func`
- `export async func`
- `export class`
- `export enum`
- `export var` mit genau einer einzelnen Variablen

Beispiel.

```cfs
export const base = 100;

export func add(a, b) {
    return a + b;
}

export class Acc(seed) {
    var value = 0;

    func init(seed) {
        this.value = seed;
    }

    func add(v) {
        this.value = this.value + v;
        return this.value;
    }
}
```

`export var` unterstuetzt keine Destructuring Form und nur genau eine Deklaration.

## Bare Import

```cfs
import "helpers.cfs";
```

Das ist nur dann erlaubt, wenn das importierte Modul keine expliziten `export` Anweisungen verwendet. In diesem Fall werden die Top Level Deklarationen materialisiert, als haetten sie direkt im aktuellen Modul gestanden.

Wenn ein Modul explizit `export` nutzt, musst du Named Import oder Namespace Import verwenden.

## Named Import

```cfs
import { base, add, Acc, Mode } from "math.cfs";
import { base as BASE, add as sum } from "math.cfs";
```

Das ist die praeziseste Form, wenn du nur einen Teil der Moduloberflaeche brauchst.

## Einzelsymbol Import

```cfs
import triple from "tools.cfs";
```

Das ist syntaktisch die Kurzform fuer den Import genau eines exportierten Symbols.

## Namespace Import

```cfs
import * as Tools from "tools.cfs";
```

Danach greifst du ueber den Namespace Alias auf exportierte Inhalte zu.

```cfs
Tools["add_base"](5)
Tools["Flag"]["On"]
```

In vielen Faellen ist auch Dot Zugriff moeglich. Die eckige Form ist trotzdem besonders explizit und robust.

## Dateiaufloesung

Relative und einfache Modulpfade werden in dieser Reihenfolge gesucht.

1. Verzeichnis des aktuellen Skripts.
2. Aktuelles Arbeitsverzeichnis.
3. Verzeichnis der gestarteten CLI.

Das gilt fuer `.cfs` und fuer `.dll`.

## Selbstimporte und Zyklen

Die Importlogik erkennt.

- Selbstimporte.
- Importzyklen.
- fehlende Dateien.
- fehlende Exportnamen.

Dadurch bleiben Modulfehler frueh sichtbar und nicht erst irgendwo spaet in der Ausfuehrung.

## Mehrfachimporte

Wiederholte Importe derselben Datei werden idempotent behandelt. Das ist wichtig fuer transitive Modulbaeume und vermeidet doppelte Materialisierung bereits bekannter Inhalte.

## URL Importe

Der Parser kann auch HTTP oder HTTPS Ressourcen importieren, solange der Pfad eine absolute URL ist.

```cfs
import "https://example.com/module.cfs";
```

Wichtige Hinweise dazu.

- Die Ressource wird als Quellmodul behandelt.
- Es gibt Schutz ueber Zeitlimit und Groessenlimit.
- Relevante Umgebungsvariablen sind `CFGS_IMPORT_HTTP_TIMEOUT_MS` und `CFGS_IMPORT_HTTP_MAX_BYTES`.

Diese Form ist praktisch fuer kontrollierte Umgebungen. Fuer produktive Skripte sind lokale Abhaengigkeiten meist einfacher zu auditieren.

## DLL Importe

Plugins werden ueber normale Import Syntax geladen.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Web.Http.dll";
```

Wichtig ist dabei.

- Fuer DLLs gibt es keinen Named Import.
- Fuer DLLs gibt es keinen Namespace Import.
- Der Import laedt und aktiviert Plugins in der Assembly.
- Attributbasierte Builtins und Intrinsics in der Assembly werden ebenfalls registriert.

## Namespaces und Module zusammen

Module koennen Namespaces exportieren, indem sie top level Namespace Deklarationen enthalten. Der Import bringt dann die erzeugten Top Level Symbole in den aktuellen Kontext oder in den Namespace Alias, je nach Importform.

## Praktisches Beispiel

### Modul `mod_math.cfs`

```cfs
export const base = 100;

export func add(a, b) {
    return a + b;
}
```

### Modul `main.cfs`

```cfs
import { base as BASE, add } from "mod_math.cfs";

func main() {
    print(BASE);
    print(add(2, 3));
}

main();
```

## Typische Fehlerbilder

- Bare Import auf ein Modul mit expliziten Exports.
- Import nicht im Header.
- Named Import fuer eine DLL.
- Namespace Import fuer eine DLL.
- Alias Konflikte mit vorhandenen Symbolen.
- Import eines nicht exportierten Namens.

Die Fehlermeldungen der Engine sind an diesen Stellen bereits relativ direkt und nennen in der Regel Symbolname oder Importpfad.

Wenn du Module mit async Aufrufen kombinieren willst, gehe jetzt zu [Async, Await und Yield](08_async_await_und_yield.md). Wenn du direkt die DLL Seite verstehen willst, springe zu [HTTP und SQL Plugins verwenden](10_http_und_sql_plugins.md).
