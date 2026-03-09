# Klassen, Enums, Namespaces und Vererbung

## Klassenueberblick

Eine Klasse hat in CFGS einen Namen, eine Konstruktor Signatur im Kopf und einen Block mit Feldern, Methoden, statischen Mitgliedern, Enums und sogar Nested Classes.

```cfs
class Point(x, y) {
    var x = 0;
    var y = 0;

    func init(x, y) {
        this.x = x;
        this.y = y;
    }

    func sum() {
        return this.x + this.y;
    }
}
```

Die Parameter im Klassenkopf definieren die Konstruktor Oberflaeche. Die eigentliche Initialisierung passiert in der Regel in `init`.

## Instanzfelder und statische Felder

```cfs
class Meter(v) {
    static var Scale = 10;
    var value = 0;

    func init(v) {
        this.value = v;
    }
}
```

- `var` in der Klasse erzeugt Instanzfelder.
- `static var` erzeugt statische Felder auf dem Typ.

## Methoden und statische Methoden

```cfs
class Meter(v) {
    static var Scale = 10;
    var value = 0;

    func init(v) {
        this.value = v;
    }

    func scaled() {
        return this.value * type.Scale;
    }

    static func make(v) {
        return new Meter(v);
    }
}
```

Wichtig ist hier `type`. Dieser Receiver ist in Klassenmethoden verfuegbar und zeigt auf den aktuellen Typ. Er funktioniert in Instanzmethoden und statischen Methoden.

## Objektinstanzen erzeugen

```cfs
var p = new Point(3, 4);
```

Wenn die Klasse ein `init` besitzt, wird es beim `new` Aufruf mit den uebergebenen Argumenten benutzt.

## Objektinitialisierer

Direkt nach `new` kannst du bekannte Mitglieder ueberschreiben.

```cfs
class Config(host, port) {
    var host = "";
    var port = 0;

    func init(host, port) {
        this.host = host;
        this.port = port;
    }
}

var cfg = new Config("localhost", 80) { host: "api.local", port: 443 };
```

Das ist praktisch fuer Datenobjekte und Konfigurationscontainer.

## Receiver `this`, `type`, `super`, `outer`

### `this`

`this` ist in Instanzmethoden verfuegbar.

```cfs
func get() {
    return this.value;
}
```

### `type`

`type` ist in Klassenmethoden verfuegbar. Dazu zaehlen Instanzmethoden und statische Methoden.

```cfs
func scaled() {
    return this.value * type.Scale;
}
```

### `super`

`super` ist in Klassen mit Basisklasse verfuegbar und funktioniert fuer Instanzmethoden und statische Methoden.

```cfs
class Base(seed) {
    var seed = 0;
    func init(seed) { this.seed = seed; }
    func score(a, b = 1) { return this.seed + a + b; }
    static func tag(x = 1) { return x + 1; }
}

class Child(seed) : Base(seed) {
    func score(a, b = 1) {
        return super.score(a, b) + this.seed;
    }

    static func tag(x = 1) {
        return super.tag(x) + 1;
    }
}
```

### `outer`

`outer` ist fuer Nested Classes gedacht und nur in verschachtelten Instanzmethoden verfuegbar.

```cfs
class Outer(seed) {
    var seed = 0;

    func init(seed) {
        this.seed = seed;
    }

    class Inner(add) {
        var add = 0;

        func init(add) {
            this.add = add;
        }

        func total() {
            return outer.seed + this.add;
        }
    }
}
```

## Implizite Mitgliedsauflosung

Innerhalb von Methoden musst du Mitglieder nicht immer mit `this.` oder `type.` qualifizieren. CFGS versucht passende Mitglieder implizit aufzufinden.

Die praktische Aufloesungsreihenfolge ist.

1. Lokaler Name oder Parameter.
2. Passendes Klassenmitglied.
3. Global sichtbarer Name.

Das macht Methoden kuerzer. Es ist aber trotzdem oft lesbarer, wichtige Zugriffe explizit zu schreiben.

## Vererbung

Die Syntax fuer Vererbung ist direkt im Klassenkopf.

```cfs
class FancyCalc(seed) : BaseCalc(seed) {
    func score(a, b = 1) {
        return super.score(a, b) + this.seed;
    }
}
```

Basiskonstruktor Aufrufe koennen Positionsargumente, Named Arguments und Spread verwenden.

```cfs
class NamedChild() : SpreadBase(a: 3, b: 4) {
}

class SpreadChild() : SpreadBase(*[3, 4, 9]) {
}
```

Die Runtime und der Compiler pruefen dabei die Signatur streng. Unbekannte Named Arguments, zu wenige Argumente oder eine falsche Reihenfolge erzeugen klare Fehler.

## Sichtbarkeit

CFGS unterstuetzt drei Sichtbarkeiten.

- `public`
- `private`
- `protected`

Das gilt fuer.

- Instanzfelder
- Statische Felder
- Methoden
- Statische Methoden
- Konstruktoren ueber `init`
- Enums in Klassen
- Nested Classes

Beispiel.

```cfs
class AccessBase(v) {
    private var secret = 0;
    protected var prot = 0;
    public var open = 0;

    private static var sPriv = 11;
    protected static var sProt = 22;

    protected func init(v) {
        this.secret = v;
        this.prot = v + 1;
        this.open = v + 2;
    }
}
```

Private Konstruktoren eignen sich fuer Factory Muster.

```cfs
class FactoryOnly(v) {
    var x = 0;

    private func init(v) {
        this.x = v;
    }

    static func make(v) {
        return new FactoryOnly(v);
    }
}
```

## Override Regeln

Overrides werden nicht locker behandelt. Der Compiler prueft mehrere Dinge.

- Methodenart muss passen. Instanz gegen Instanz. Statisch gegen statisch.
- Parameterform und Arity muessen kompatibel sein.
- Die Sichtbarkeit darf nicht enger werden.
- Feld gegen Methode oder Methode gegen Feld ist kein gueltiger Override.

Das fuehrt zu einer angenehm vorhersehbaren OOP Schicht.

## Enums

Enums koennen top level oder innerhalb einer Klasse stehen.

```cfs
enum Mode {
    Fast = 1,
    Safe = 2
}
```

Oder in einer Klasse.

```cfs
class Point(x, y) {
    enum Kind {
        A = 1,
        B = 2
    }
}
```

Zugriff funktioniert ueber Index oder qualifizierten Zugriff.

```cfs
Mode["Safe"]
Point.Kind["B"]
```

In vielen Faellen funktioniert auch Dot Zugriff, weil Dot intern auf benannte Member zugreift.

## Namespaces

Namespaces strukturieren groessere Oberflaechen.

```cfs
namespace App.Tools {
    func clamp(x, lo, hi) {
        if (x < lo) { return lo; }
        if (x > hi) { return hi; }
        return x;
    }

    class Box(v) {
        var value = 0;
        func init(v) { this.value = v; }
        func get() { return this.value; }
    }
}
```

Der Zugriff ist qualifiziert.

```cfs
App.Tools.clamp(99, 0, 10)
new App.Tools.Box(7)
```

Wichtig fuer die Praxis.

- Qualifizierte Namespace Namen wie `A.B.C` sind erlaubt.
- Mehrere `namespace App.Tools { ... }` Deklarationen koennen denselben Namespace erweitern.
- `import` ist innerhalb eines Namespace Bodys nicht erlaubt.
- `export` ist innerhalb eines Namespace Bodys nicht erlaubt.
- Ein Namespace Root darf nicht mit schon vorhandenen Top Level Symbolen kollidieren.

## Reserved Namen

Diese Namen sind semantisch reserviert und sollten nicht als normale Bindungen verwendet werden.

- `this`
- `type`
- `super`
- `outer`

Auch interne Runtime Felder wie `__type` und `__outer` gehoeren der VM und nicht dem Anwendungsmodell.

## Zusammengesetztes Beispiel

```cfs
namespace App.Model {
    class Counter(seed) {
        static var Created = 0;
        var value = 0;

        func init(seed) {
            this.value = seed;
            type.Created = type.Created + 1;
        }

        func inc() {
            value = value + 1;
            return value;
        }

        static func make(seed = 0) {
            return new Counter(seed);
        }
    }
}

var c = App.Model.Counter.make(5);
print(c.inc());
print(App.Model.Counter.Created);
```

Mit Klassen kommt fast automatisch das Thema Module dazu. Das naechste Kapitel zeigt dir, wie du Oberflaechen exportierst, importierst und als Namespace wieder hereinholst.
