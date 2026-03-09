# Match, Destructuring und Out

## Match als Statement

Das Statement `match` ist fuer verzweigende Musterpruefung gedacht.

```cfs
func describe(v) {
    var result = "none";

    match (v) {
        case [var a, var b] if (a < b): {
            result = "asc:" + str(a + b);
        }
        case [var a, var b]: {
            result = "pair:" + str(a * b);
        }
        default: {
            result = "other";
        }
    }

    return result;
}
```

Ein `match` Statement verwendet `case` fuer normale Arme und `default` fuer den Fallback.

## Match als Ausdruck

Als Ausdruck ist `match` besonders kompakt.

```cfs
func classify(v) {
    return v match {
        [var a, var b] if (a == b): "same",
        {"tag": "num", "value": var n} if (n > 10): "big",
        {"tag": "num", "value": var n}: "small",
        _ if (v == null): "nil",
        _: "other"
    };
}
```

Hier gibt es kein `default`, sondern `_` als Wildcard Arm. Ein unbewachter `_` Arm ist der eigentliche Fallback.

## Verfuegbare Match Muster

Die Sprache unterstuetzt diese Pattern Formen.

- Literale wie Zahlen, Strings, Chars, `true`, `false`, `null`
- `_` als Wildcard
- `var name` als Bindung
- Array Muster wie `[var a, var b]`
- Dictionary Muster wie `{"kind": "pair", "left": var x, "right": var y}`
- Beliebige Verschachtelung dieser Formen

## Guards

Ein Pattern kann zusaetzlich mit `if (...)` abgesichert werden.

```cfs
case [var a, var b] if (a < b): { ... }
```

Ein Guard wird nur dann ausgewertet, wenn das eigentliche Pattern schon passt.

## Destructuring in Deklarationen

### Arrays

```cfs
var arr = [10, [20, 30], 40];
var [a, [b, c], _] = arr;
```

### Dictionaries

```cfs
var obj = {"x": 1, "y": 2, "pair": [7, 8], "inner": {"z": 9}};
const {x, y: yy, pair: [p0, p1], inner: {z}} = obj;
```

Das bedeutet.

- `x` bindet direkt auf den gleichnamigen Key.
- `y: yy` liest den Key `y` und speichert ihn unter dem lokalen Namen `yy`.
- Verschachtelte Muster funktionieren fuer Arrays und Dictionaries.
- `_` ignoriert einen Wert.

## Destructuring in Zuweisungen

Bestehende Variablen koennen spaeter wieder per Muster befuellt werden.

```cfs
var q = 0;
var r = 0;
[q, r] = [3, 4];

var dx = 0;
var dy = 0;
({x: dx, y: dy} = {"x": 1, "y": 2});
```

Die geklammerte Form bei Dictionary Zuweisungen ist wichtig, damit der Parser die Syntax sauber erkennt.

## Destructuring in Parametern

```cfs
func sum_pair([m, n]) {
    return m + n;
}

func read_obj({x, y: y2}) {
    return x * 10 + y2;
}
```

Auch Defaults und Folgeabhaengigkeiten sind moeglich.

```cfs
func order_from_destructure([sx] = [7], sy = sx) {
    return sy;
}
```

## Destructuring in `foreach`

```cfs
foreach (var [x, y] in [[1, 2], [3, 4]]) {
    print(x + y);
}

foreach (var {x, y: yy} in [{"x": 1, "y": 7}, {"x": 2, "y": 8}]) {
    print(x + yy);
}
```

## `out` als Ausdrucksblock

`out` ist eine Ausdrucksform mit eigenem Block. Das Ergebnis ist der letzte ausgewertete Ausdruck im Block.

```cfs
var result = out {
    var a = 4;
    a;
};
```

Wenn kein letzter Ausdruck vorhanden ist, liefert `out` den Wert `null`.

```cfs
var result = out {
    var a = 1;
};
```

Das ist praktisch fuer lokale Mini Berechnungen, bei denen du keinen kompletten Funktionsrahmen aufmachen willst.

## Wichtige Regeln fuer `out`

- `return` ist in einem `out` Block nicht erlaubt.
- Der Block hat seinen eigenen Scope.
- Der Rueckgabewert kommt aus dem letzten Ausdruck, nicht aus einem `return`.

## Gute Einsatzfelder

`match` ist stark bei strukturierten Daten. `destructuring` ist stark beim Entpacken. `out` ist stark fuer kompakte Berechnungsinseln. Zusammen ergibt das eine Sprache, die fuer Konfigurations und Transformationsskripte sehr angenehm ist.

## Kombiniertes Beispiel

```cfs
func normalize(msg) {
    var header = out {
        var raw = msg["headers"];
        raw["Content-Type"] ?? "text/plain";
    };

    return msg match {
        {"kind": "pair", "left": var x, "right": var y} if (x == y): {
            "same:" + str(x + y);
        },
        {"kind": "pair", "left": var x, "right": var y}: {
            "diff:" + str(x - y);
        },
        _: {
            "fallback:" + header;
        }
    };
}
```

Das naechste Kapitel oeffnet die OOP Seite der Sprache. Dort kommen dann Klassen, Sichtbarkeit, Vererbung, Namespaces und die speziellen Receiver `this`, `type`, `super` und `outer`.
