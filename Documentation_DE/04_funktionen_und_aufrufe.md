# Funktionen und Aufrufe

## Funktionsdeklarationen

Normale Funktionen werden mit `func` deklariert.

```cfs
func add(a, b) {
    return a + b;
}
```

Rueckgaben passieren ueber `return`. Ohne explizite Rueckgabe landet am Ende effektiv `null` als Ergebnis.

## Async Funktionen

Async Funktionen werden mit `async func` deklariert.

```cfs
async func fetch_value() {
    yield;
    return 42;
}
```

Eine `async func` liefert beim Aufruf ein Task Objekt zurueck. Mehr dazu steht in [Async, Await und Yield](08_async_await_und_yield.md).

## Funktionsausdruecke

Funktionen sind in CFGS Werte. Du kannst sie also anonym erstellen, speichern und weiterreichen.

```cfs
var twice = func(v) {
    return v * 2;
};

print(twice(5));
```

Dasselbe geht auch async.

```cfs
var work = async func(v) {
    yield;
    return v + 1;
};
```

## Closures

Funktionswerte koennen aeussere Variablen capturen.

```cfs
func make_multiplier(factor) {
    return func(v) {
        return v * factor;
    };
}

var mul3 = make_multiplier(3);
print(mul3(7));
```

Captures sind lebendig. Wenn sich die Variable aendert, sieht die Closure spaetere Werte.

```cfs
var captured = 2;
var add_captured = func(v) { return v + captured; };

print(add_captured(5));
captured = 9;
print(add_captured(5));
```

## Default Parameter

Parameter duerfen Default Werte haben.

```cfs
func open_port(host, port = 80) {
    return host + ":" + str(port);
}
```

Wichtig ist, dass Default Werte auf vorherige Parameter zugreifen duerfen.

```cfs
func with_dep(a, b = a * 2, c = b + 1) {
    return a + b + c;
}
```

Ein Parameter ohne Default darf nicht hinter einem Parameter mit Default auftauchen.

## Named Arguments

Aufrufe koennen benannte Argumente verwenden.

```cfs
func sum3(a, b = 10, c = 100) {
    return a + b + c;
}

sum3(1, c: 5)
sum3(1, c: 5, b: 2)
sum3(a: 1, b: 2, c: 3)
```

Wichtige Regeln dazu.

- Ein benanntes Argument darf nur einmal vorkommen.
- Nach dem ersten benannten Argument darf kein normales Positionsargument mehr folgen.
- Unbekannte Parameternamen erzeugen einen Fehler.
- Rest Parameter duerfen nicht als Named Argument uebergeben werden.

## Rest Parameter

Mit `*name` sammelst du ueberzaehlige Positionsargumente in ein Array.

```cfs
func collect(a, b = 2, *rest) {
    return a + b + len(rest);
}
```

Rest Parameter haben diese Regeln.

- Es darf nur einen geben.
- Er muss der letzte Parameter sein.
- Er darf keinen Default Wert haben.

## Spread Arguments

Ein Array kann beim Aufruf wieder aufgefaltet werden.

```cfs
func add3(a, b, c) {
    return a + b + c;
}

var values = [2, 3, 4];
print(add3(*values));
```

Spread funktioniert auch in Kombination mit Named Arguments, solange die Reihenfolge sauber bleibt.

```cfs
collect(*[1], b: 7)
```

Nach Named Arguments darf nichts Positionsartiges mehr kommen. Ein spaeterer Spread zaehlt ebenfalls als Positionsargument und ist dann ungueltig.

## Methoden folgen denselben Aufrufregeln

Methoden koennen ebenfalls Default Parameter, Rest Parameter und Named Arguments nutzen.

```cfs
class CallBox(seed) {
    var seed = 0;

    func init(seed) {
        this.seed = seed;
    }

    func mix(a, b = 5, *rest) {
        return this.seed + a + b + len(rest);
    }
}

var box = new CallBox(10);
print(box.mix(a: 2, b: 6));
```

## Destructuring Parameter

Parameter duerfen direkt destrukturiert werden. Das ist eines der staerkeren Sprachfeatures von CFGS.

```cfs
func sum_pair([a, b]) {
    return a + b;
}

func read_point({x, y: yy}) {
    return x * 10 + yy;
}
```

Auch hier sind Defaults und Folgeabhaengigkeiten moeglich.

```cfs
func order_from_destructure([sx] = [7], sy = sx) {
    return sy;
}
```

## Rueckgabe und Scope

`return` ist nur in Funktionen erlaubt. In `out` Bloecken ist `return` ausdruecklich nicht erlaubt, weil `out` selbst schon ein Ausdruck ist.

## Typische Einstiegsmuster

### Klassisches `main`

```cfs
func main() {
    print("start");
}

main();
```

### Async `main`

```cfs
async func main() {
    yield;
    return 1;
}

var _ = await main();
```

### Funktionen als Pipeline Bausteine

```cfs
func apply_twice(f, v) {
    return f(f(v));
}

var inc = func(x) { return x + 1; };
print(apply_twice(inc, 5));
```

Wenn du Funktionen mit Pattern Matching und Destructuring kombinieren willst, gehe direkt weiter zu [Match, Destructuring und Out](05_match_destructuring_und_out.md).
