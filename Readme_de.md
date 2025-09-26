# ConfigurationScript (VM) Features

CFGS wurde **komplett neu gestaltet**.
Während die vorherige Version im Wesentlichen ein **Prototyp war, der auf einem direkten AST-Interpreter basierte**, läuft die neue Version auf einer **stack-basierten Bytecode-VM** mit eigenem VM-Code.
Die **Syntax wurde bewusst angepasst**, um eine reibungslose Zusammenarbeit zwischen **Lexer, Parser, Compiler und VM** zu gewährleisten.
Dieses Projekt diente auch dazu, **meine Kenntnisse in der Compiler-Entwicklung weiter auszubauen und zu festigen**.

⚠️ **Wichtige Hinweise:**

* Das alte Projekt wird **nicht mehr aktiv weiterentwickelt**, das Repository bleibt jedoch bestehen, um die **Entwicklung von CFGS nachvollziehen zu können**, auch wenn die Basis nun komplett verändert wurde.
* **CFGS befindet sich noch in der Entwicklung** und kann Fehler enthalten.

## 💻 Ausführen von CFGS-Code

Der Code kann auf zwei Arten ausgeführt werden:

1. **Eingebaute REPL:**

   * Wenn keine Kommandozeilen-Argumente angegeben werden, startet CFGS eine **interaktive REPL**.
   * Die REPL ist so konzipiert, dass **offene Blöcke erkannt werden** und der Code erst **ausgeführt wird, wenn alle Blöcke korrekt geschlossen sind**, um unvollständige Ausführung zu verhindern.

2. **Kommandozeilen-Modus:**

   * Ein Skript kann direkt über die Kommandozeile ausgeführt werden:

     * `cfcs [-d] <skript-pfad>`

       * `-d` (optional) aktiviert den **Debug-Modus**.
       * `<skript-pfad>` ist der Pfad zu deinem Skript.
     * Die **Dateiendung des Skripts spielt derzeit keine Rolle**.

Diese Vorgehensweise ermöglicht flexibles Testen und Ausprobieren, entweder interaktiv oder durch Ausführen kompletter Skripte.

---

## Inhaltsverzeichnis

1. [Variablen](#variablen)
2. [Arrays](#arrays)
3. [Dictionaries (Objekte)](#dictionaries-objekte)
4. [Funktionen](#funktionen)
5. [For-Schleifen](#for-schleifen)
6. [While-Schleifen](#while-schleifen)
7. [If / Else](#if--else)
8. [Match-Case](#match-case)
9. [Break und Continue](#break-und-continue)
10. [Closures / Funktionen als Werte](#closures--funktionen-als-werte)
11. [Klassen / Objekte](#klassen--objekte)
12. [Enums](#Enums)

---

## Variablen

💡 Variablen speichern Werte (Zahlen, Strings, etc.)

```cfs
var score = 100;
print(score); # 100

var playerName = "Alex";
print(playerName); # Alex
```

---

## Arrays

📚 Arrays speichern geordnete Listen. Verschachtelte Arrays sind möglich. Items können hinzugefügt oder gelöscht werden.

```cfs
var numbers = [5, 10, 15, [20, 25], 30];
for(var i = 0; i < len(numbers); i++;) {
    print(numbers[i]);
}

var nestedArray = ["A", "B", ["C", "D"]];
print(nestedArray);
print(nestedArray[2][1]); # D

# Neues Element hinzufügen
numbers[] = 99;
print(numbers);

# Element löschen
delete numbers[1];
print(numbers);
```

---

## For-Schleifen

```cfs
var arr = [10, 20, 30, 40, 50];
for(var x = 0; x < len(arr); x++;) {
    print("Element an Index " + x + ": " + arr[x]);
}
```

---

## Dictionaries (Objekte)

🗂 Schlüssel-Wert-Paare, Zugriff über Punktnotation oder `[]`. Neue Schlüssel können hinzugefügt oder gelöscht werden.

```cfs
var settings = {"volume": 70, "theme": "dark"};
print(settings.volume);
print(settings["theme"]);

settings.volume = 90;
settings["theme"] = "light";
print(settings);

# Neues Schlüssel-Wert-Paar hinzufügen
settings[] = {"language": "DE"};
print(settings);

# Schlüssel löschen
delete settings["volume"];
print(settings);
```

---

## Funktionen

🔧 Wiederverwendbare Logik ( Closures ) mit Parametern und Rückgabewerten.

```cfs
func addNumbers(a, b) {
    return a + b;
}
print(addNumbers(15, 25)); // 40

func greet(name) {
    return "Hello, " + name + "!";
}
print(greet("Sam")); # Hello, Sam!
```

---

## While-Schleifen

🔁 Wiederholt Code solange die Bedingung wahr ist.

```cfs
var counter = 1;
while(counter <= 5) {
    print(counter);
    counter++;
}
```

---

## If / Else

⚡ Bedingte Ausführung von Codeblöcken.

```cfs
var health = 80;
if(health >= 90) {
    print("Sehr gesund!");
} else if(health >= 50) {
    print("Alles in Ordnung.");
} else {
    print("Wenig Gesundheit!");
}
```

---

## Match-Case

🔹 Alternative zu mehreren `if/else if`-Statements.

```cfs
var level = 3;
match(level) {
    case 1: { print("Anfänger"); }
    case 2: { print("Fortgeschritten"); }
    case 3: { print("Experte"); }
}
```

---

## Break und Continue

⏹ `break` beendet Schleifen, `continue` überspringt die aktuelle Iteration.

```cfs
var n = 10;
while(true) {
    n--;
    if(n % 2 == 0) { continue; }
    print(n);
    if(n <= 1) { break; }
}
```

---

## Closures / Funktionen als Werte

🔒 Funktionen können Variablen zugewiesen oder zurückgegeben werden.

```cfs
var multiply = func(a, b) {
    return a * b;
};
print(multiply(5, 6)); # 30

func makeAdder(x) {
    return func(y) {
        return x + y;
    };
}
var addFive = makeAdder(5);
print(addFive(10)); # 15
```

---

## Klassen / Objekte

🏛 Objekte mit Eigenschaften und Methoden. `this` referenziert die Instanz.

```cfs
class Player(name, score) {
    var level;
    func setLevel(lvl) {
        this.level = lvl;
    }
    func getInfo() {
        return name + ":" + score + ":" + level;
    }
}
var hero = new Player("Lara", 250);
hero.setLevel(3);
print(hero.getInfo());
```

---

## Enums
```cfs

enum colors
{
   red = 5,
   green,
   blue
}

print(colors.green); # Output 6

```

[Beispiele](CFGS_NE/Samples)

[Back](README.md)
