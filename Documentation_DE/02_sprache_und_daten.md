# Sprache und Daten

## Kommentare

CFGS kennt zwei Kommentarformen.

- Einzeilige Kommentare beginnen mit `#`.
- Blockkommentare beginnen mit `#+` und enden mit `+#`.

```cfs
# Einzeilig

#+
Mehrzeilig.
Auch ueber mehrere Zeilen.
+#
```

## Top Level Regeln

Am Top Level sind vor allem diese Dinge sinnvoll und erlaubt.

- Leere Statements mit `;`
- `import`
- `namespace`
- `export`
- `var` und `const`
- `func` und `async func`
- `class` und `enum`
- Statements, die mit einem Identifier anfangen. Zum Beispiel `main();` oder `x = 1;`

Direkte Kontrollfluss Statements wie `if`, `for`, `foreach`, `while`, `match`, `try`, `throw`, `break`, `continue` und `delete` sind am Top Level nicht erlaubt.

Ein wichtiger Spezialfall ist `await`. Ein nacktes Top Level Statement wie `await work();` ist nicht erlaubt. Ein eingebettetes Await in einem Ausdruck ist dagegen moeglich. Deshalb sieht man oft dieses Muster.

```cfs
var _ = await main();
```

## Literale

### Null und Booleans

```cfs
var a = null;
var b = true;
var c = false;
```

### Zahlen

CFGS unterstuetzt mehrere Zahlenformate.

- Dezimal. `42`
- Negative Zahlen ueber den unaren Operator. `-42`
- Floating Point. `3.14`
- Exponentialschreibweise. `6.02e23`
- Unterstriche zur Lesbarkeit. `1_000_000`
- Hexadezimal. `0xFF`
- Binaer. `0b1010`
- Oktal. `0o755`

Je nach Groesse werden Werte intern als `int`, `long`, `double`, `decimal` oder `BigInteger` behandelt.

### Strings

Strings stehen in doppelten Anfuehrungszeichen.

```cfs
var text = "Hallo";
```

Unterstuetzte Escape Sequenzen sind unter anderem diese.

- `\n`
- `\t`
- `\r`
- `\\`
- `\"`
- `\u263A`
- `\x41`

### Chars

Char Literale stehen in einfachen Anfuehrungszeichen.

```cfs
var letter = 'A';
var newline = '\n';
```

Ein Char Literal muss genau ein Zeichen enthalten.

## Variablen und Konstanten

### `var`

`var` erzeugt eine veraenderbare Bindung.

```cfs
var port = 8080;
port = 9090;
```

### `const`

`const` verlangt immer einen Initialwert und kann spaeter nicht neu gebunden werden.

```cfs
const appName = "Demo";
```

### Schattenbildung

Lokale Namen koennen aeussere Namen ueberschreiben. In Methoden gilt praktisch diese Aufloesungslogik.

1. Lokale Variable oder Parameter.
2. Passendes Mitglied der Klasse.
3. Global sichtbarer Name.

Das ist wichtig, weil Methoden Felder oft ohne `this.` lesen koennen.

## Wahrheitswerte und Truthiness

In Bedingungen gelten diese Werte als falsch.

- `null`
- `false`
- numerische `0`
- leere Strings
- leere Arrays
- leere Dictionaries

Die meisten anderen Werte sind wahr. Auch NaN wird in CFGS als wahr behandelt.

## Operatoren

CFGS deckt die ueblichen Operatorfamilien ab.

- Arithmetik. `+`, `-`, `*`, `/`, `%`, `**`
- Vergleich. `==`, `!=`, `<`, `>`, `<=`, `>=`
- Logik. `!`, `&&`, `||`
- Null Coalescing. `??`
- Ternary. `bedingung ? dann : sonst`
- Bitoperatoren. `&`, `|`, `^`, `<<`, `>>`
- Inkrement und Dekrement. `++x`, `x++`, `--x`, `x--`
- Compound Assignments. `+=`, `-=`, `*=`, `/=`, `%=`

Potenz ist rechtsassoziativ. `2 ** 3 ** 2` wird also als `2 ** (3 ** 2)` ausgewertet.

## Arrays

Array Literale sehen so aus.

```cfs
var arr = [1, 2, 3];
```

Elemente werden ueber Indexzugriff gelesen und geschrieben.

```cfs
arr[1] = 20;
print(arr[1]);
```

### Push Syntax

Die Sprache besitzt eine eigene Kurzform zum Anhaengen.

```cfs
arr[] = 4;
```

Das ist syntaktischer Zucker fuer ein Append auf das Array.

## Dictionaries

Dictionary Literale sehen so aus.

```cfs
var cfg = {"host": "localhost", "port": 80};
```

Zugriff ist ueber eckige Klammern und oft auch ueber Dot Zugriff moeglich.

```cfs
print(cfg["host"]);
print(cfg.host);
```

### Dictionary Push

Auch Dictionaries koennen die Push Kurzform verwenden.

```cfs
cfg[] = {"mode": "safe"};
```

Wenn du genau ein Schluessel Wert Paar uebergibst, wird es in das Dictionary gemerged. Das ist die uebliche und saubere Form.

## Slicing

Arrays und Strings unterstuetzen Slices mit `~`.

```cfs
var arr = [10, 20, 30, 40];
var part = arr[1~3];
var text = "abcdef";
var cut = text[2~5];
```

Die rechte Grenze ist exklusiv. Es gelten also dieselben Gedanken wie bei vielen modernen Sprachen.

Auch offene Grenzen sind moeglich.

```cfs
arr[~2]
arr[2~]
arr[~]
```

### Slice Zuweisung

Arrays erlauben auch das Ersetzen eines Bereichs.

```cfs
arr[1~3] = [111, 222];
```

Strings sind dagegen unveraenderlich. Du kannst Strings slicen. Du kannst aber keine String Slices in place ueberschreiben.

## Delete

`delete` ist ein eigenes Sprachfeature fuer Arrays und Dictionaries.

```cfs
delete arr[1];
delete arr[1~3];
delete arr;

delete cfg["host"];
delete cfg.host;
delete cfg;
```

Die Bedeutung ist wie folgt.

- `delete arr[1];` entfernt genau ein Element.
- `delete arr[1~3];` entfernt einen Bereich.
- `delete arr;` leert das komplette Array.
- `delete cfg["k"];` oder `delete cfg.k;` entfernt genau einen Schluessel.
- `delete cfg;` leert das komplette Dictionary.

`delete` auf Strings ist nicht erlaubt.

## Dictionary Hardening fuer reservierte Intrinsics

Das Runtime System schuetzt einige reservierte Dictionary Intrinsic Namen. Ein Beispiel ist `get_or`. Solche Namen sollten nicht als normale Nutzdaten Schluessel verwendet werden, wenn sie direkt mit Literalen oder per Push gesetzt werden. Das verhindert Kollisionen zwischen Daten und Methoden.

Die sichere Praxis ist einfach. Verwende fuer fachliche Nutzdaten normale Schluessel wie `mode`, `retries`, `host`, `status`, `value`.

## Strings als eigener Datentyp

Strings verhalten sich wie primitive Werte mit vielen Intrinsics. Die vollstaendige Methodenliste findest du in [Standardbibliothek](09_standardbibliothek.md). Die wichtigsten sind `lower`, `upper`, `trim`, `contains`, `substr`, `slice`, `replace` und `insert_at`.

## JSON Formen

Mit `fromjson` und `tojson` kannst du zwischen JSON Text und CFGS Datenstrukturen wechseln.

```cfs
var obj = fromjson("{\"x\":1,\"y\":[2,3]}");
print(obj["y"][0]);

var json = tojson(obj);
print(json);
```

## Kurzer Gesamtueberblick in einem Beispiel

```cfs
var arr = [1, 2, 3];
arr[] = 4;
arr[1~3] = [20, 30];

var cfg = {"host": "localhost", "port": 80};
cfg[] = {"mode": "safe"};
delete cfg.port;

var text = "abcdef";
print(text[1~4]);
```

Wenn du mit diesen Datentypen Bedingungen und Schleifen kombinieren willst, lies als Naechstes [Kontrollfluss und Fehler](03_kontrollfluss_und_fehler.md).
