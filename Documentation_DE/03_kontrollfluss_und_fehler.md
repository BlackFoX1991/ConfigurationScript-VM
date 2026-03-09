# Kontrollfluss und Fehler

## Grundidee

CFGS trennt Deklaration und Ausfuehrungslogik recht sauber. Alles, was nach echtem Programmfluss riecht, gehoert in Funktionen oder Methoden.

## `if` und `else`

```cfs
if (x > 10) {
    print("gross");
} else {
    print("klein");
}
```

`else if` schreibst du einfach als verkettetes `else` mit einem weiteren `if`.

```cfs
if (score > 90) {
    print("A");
} else if (score > 75) {
    print("B");
} else {
    print("C");
}
```

## `while`

```cfs
var i = 0;
while (i < 3) {
    print(i);
    i = i + 1;
}
```

## `do while`

Hier wird der Block mindestens einmal ausgefuehrt.

```cfs
var count = 0;
do {
    count = count + 1;
} while (count < 2);
```

## `for`

Die `for` Schleife in CFGS ist etwas spezieller als in vielen C Sprachen. Der Initialteil und der Increment Teil sind selbst wieder Statements. Deshalb endet auch der Increment Teil mit einem Semikolon innerhalb der Klammern.

```cfs
for (var i = 0; i < 5; i = i + 1;) {
    print(i);
}
```

Das wirkt ungewoehnlich. Es ist aber korrekt und wichtig. Ohne dieses letzte Semikolon vor `)` ist die Schleife nicht syntaktisch vollstaendig.

Auch diese Formen sind moeglich.

```cfs
for (; i < 10; i++;) {
    print(i);
}

for (var i = 0; ; i++;) {
    if (i == 3) {
        break;
    }
}
```

## `foreach`

### Einfache Iteration

```cfs
foreach (var v in [10, 20, 30]) {
    print(v);
}
```

### Array als Index und Wert

```cfs
foreach (var idx, val in [10, 20, 30]) {
    print(idx);
    print(val);
}
```

### Dictionary als Key und Wert

```cfs
foreach (var key, value in {"a": 1, "b": 2}) {
    print(key);
    print(value);
}
```

### Destructuring in `foreach`

```cfs
foreach (var [x, y] in [[1, 2], [3, 4]]) {
    print(x + y);
}

var a = 0;
var b = 0;
foreach ([a, b] in [[5, 6], [7, 8]]) {
    print(a * b);
}
```

## `break` und `continue`

Beide Anweisungen funktionieren wie erwartet und sind nur innerhalb von Schleifen erlaubt.

```cfs
foreach (var n in [1, 2, 3, 4, 5]) {
    if (n % 2 == 0) {
        continue;
    }
    if (n > 4) {
        break;
    }
    print(n);
}
```

## `try`, `catch`, `finally`

CFGS unterstuetzt den kompletten Dreiklang.

```cfs
try {
    risky();
} catch(e) {
    print(e.message());
} finally {
    print("cleanup");
}
```

Ein `try` braucht mindestens `catch` oder `finally`. Ein leeres `try` ohne Handler ist ungueltig.

### Das Catch Objekt

Im `catch` Block ist das Fehlerobjekt ein Runtime Exception Objekt mit Intrinsics wie `message()`, `type()`, `file()`, `line()`, `col()` und `stack()`. Die komplette Liste steht in [Standardbibliothek](09_standardbibliothek.md).

## `throw`

Du kannst praktisch jeden Wert werfen. In der Praxis sind Strings oder strukturierte Dictionaries oft am einfachsten.

```cfs
throw "etwas ist schiefgelaufen";
```

Oder etwas strukturierter.

```cfs
throw {"kind": "ConfigError", "message": "host fehlt"};
```

## Fehler in Async Code

Wenn ein `await` auf eine fehlgeschlagene Task trifft, wirft CFGS ein Exception Objekt mit Typen wie `AwaitError` oder `AwaitCanceled`. Das ist besonders bei Plugin Calls wichtig.

```cfs
try {
    var _ = await some_task();
} catch(e) {
    print(e.type());
    print(e.message());
}
```

## Was am Top Level nicht geht

Die folgenden Formen sind am Top Level nicht erlaubt.

- `if (...) { ... }`
- `while (...) { ... }`
- `for (...) { ... }`
- `foreach (...) { ... }`
- `match (...) { ... }`
- `try { ... }`
- `throw ...;`
- `delete ...;`
- `break;`
- `continue;`

Die uebliche Loesung ist immer dieselbe. Packe die Programmlogik in `main()` und rufe sie dann am Ende auf.

## Zusammengesetztes Beispiel

```cfs
func main() {
    var total = 0;

    try {
        for (var i = 1; i <= 5; i = i + 1;) {
            if (i == 4) {
                continue;
            }
            total = total + i;
        }
    } catch(e) {
        print("Fehler: " + e.message());
    } finally {
        print("Total = " + str(total));
    }
}

main();
```

Die naechsten beiden Sprachbereiche, die man meist direkt nach dem Kontrollfluss braucht, sind Funktionsaufrufe und Pattern Matching. Dafuer sind diese Kapitel da.

- [Funktionen und Aufrufe](04_funktionen_und_aufrufe.md)
- [Match, Destructuring und Out](05_match_destructuring_und_out.md)
