# Async, Await und Yield

## Async Funktionen

Eine Async Funktion wird mit `async func` deklariert.

```cfs
async func delayed_inc(x) {
    yield;
    return x + 1;
}
```

Der Aufruf liefert ein Task Objekt. In CFGS sieht das per `typeof` meist wie `Task<Object>` aus.

```cfs
var task = delayed_inc(9);
print(typeof(task));
print(await task);
```

## `await`

`await` entpackt awaitbare Werte. Dazu gehoeren.

- CFGS interne Tasks.
- .NET `Task`
- .NET `Task<T>`
- .NET `ValueTask`
- .NET `ValueTask<T>`
- Collections, deren Elemente wiederum awaitbar sind

### Await auf normalen Werten

Normale Werte duerfen ebenfalls durch `await` gehen. In diesem Fall kommt der Wert einfach unveraendert wieder heraus.

```cfs
async func demo() {
    return await 5;
}
```

### Await auf Listen

```cfs
async func demo(timer) {
    var a = timer.delay(25, 2);
    var b = timer.delay(5, 4);
    var result = await [a, b, 8];
    return result;
}
```

Die Liste wird elementweise aufgeloest. Nicht awaitbare Werte bleiben einfach stehen.

### Await auf Dictionaries

```cfs
async func demo(timer) {
    return await {"x": timer.delay(5, 10), "y": 3};
}
```

Auch hier werden awaitbare Werte rekursiv aufgeloest.

## `yield`

`yield` ist nur in `async func` erlaubt und nimmt keinen Wert an.

```cfs
async func step() {
    yield;
    return 1;
}
```

Praktisch ist `yield` ein kooperativer Unterbrechungspunkt. Die Funktion kann frueh starten, gibt den Kontrollfluss aber bewusst wieder frei.

## Heisser Start von Async Funktionen

CFGS startet Async Funktionen sofort. Das ist wichtig. Ein Aufruf fuehrt also schon Code aus, bevor du spaeter `await` darauf machst.

```cfs
var trace = "";

async func hot() {
    trace = trace + "S";
    yield;
    trace = trace + "R";
    return 11;
}

var t = hot();
trace = trace + "C";
print(trace);
print(await t);
```

Vor dem Await ist `trace` hier bereits bei `SC`. Der Start ist also heiss und nicht komplett lazy.

## Wo `await` erlaubt ist

Innerhalb synchroner Funktionen ist `await` nicht erlaubt.

```cfs
func bad() {
    return await 1;
}
```

Am Top Level ist ein nacktes `await` Statement ebenfalls ungueltig. Ein eingebettetes Await in einer Zuweisung oder in einem identifiergefuehrten Statement ist dagegen moeglich.

Sauber.

```cfs
var _ = await main();
```

Nicht sauber.

```cfs
await main();
```

## Async Fehler

Fehlgeschlagene Await Operationen werden als Exception Objekte in CFGS sichtbar. Typische Typen sind `AwaitError` und `AwaitCanceled`.

```cfs
try {
    var _ = await something();
} catch(e) {
    print(e.type());
    print(e.message());
}
```

## Task Namespace aus der Standardbibliothek

Die Standardbibliothek liefert mit `task()` einen kleinen Helfer fuer Async Szenarien.

```cfs
var t = task();
var value = await t.delay(50, 123);
```

Mehr dazu steht in [Standardbibliothek](09_standardbibliothek.md).

## Async I O und Plugins

Viele Builtins aus der Standardbibliothek und aus den Plugins liefern direkt Tasks. Das gilt etwa fuer.

- `sleep`
- `nextTick`
- `getlAsync`
- `getcAsync`
- `readTextAsync`
- `writeTextAsync`
- `appendTextAsync`
- `http_get`
- `http_post`
- `http_download`
- `sql_connect`
- alle `_async` SQL Intrinsics

## Typisches Muster fuer `main`

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

async func main() {
    var t = task();
    return await t.delay(10, "fertig");
}

var result = await main();
print(result);
```

## Wann du `yield` statt `await` nimmst

`await` nutzt du, wenn du wirklich auf einen awaitbaren Wert wartest. `yield` nutzt du, wenn du innerhalb einer Async Funktion bewusst einen Scheduling Punkt setzen willst, auch ohne externen Task.

## Weiterfuehrung

Die Async Sprachebene ist eng mit der Laufzeit verbunden. Deshalb lohnt sich direkt der Blick in [Standardbibliothek](09_standardbibliothek.md), besonders auf `task`, `sleep` und die asynchronen Datei Builtins.
