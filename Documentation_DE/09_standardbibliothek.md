# Standardbibliothek

## Einbindung

Die Standardbibliothek wird wie ein Plugin per DLL Import geladen.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
```

Danach stehen Builtins und Intrinsics global zur Verfuegung.

## Builtins im Ueberblick

### Typen, Umwandlung und Inspektion

- `typeof(value)` liefert einen freundlichen Typnamen wie `Int`, `String`, `Array`, `Dictionary`, `Task<Object>` oder den Klassennamen einer Instanz.
- `len(value)` liefert die Laenge von String, Array oder Dictionary.
- `str(value)` und `string(value)` wandeln in Text um.
- `int(value)`, `long(value)`, `double(value)`, `float(value)`, `decimal(value)`, `char(value)`, `bool(value)`, `bigint(value)` sind direkte Konvertierungen.
- `toi(value)`, `toi16(value)`, `toi32(value)`, `toi64(value)` sind numerische Helfer.
- `chr(value)` wandelt in ein Char.
- `array(value)` baut ein Array. Arrays werden kopiert. Einzelwerte werden als Einzelelement Array verpackt.
- `dictionary(value)` baut ein Dictionary. Dictionaries werden kopiert. Einzelwerte ergeben ein leeres Dictionary.
- `isarray(value)` und `isdict(value)` pruefen Collectiontypen.
- `getfields(dict)` liefert die Schluessel eines Dictionary als Array.

### Ausgabe und Konsole

- `print(value)` schreibt den Wert mit Zeilenumbruch.
- `put(value)` schreibt den Wert ohne Zeilenumbruch.
- `clear()` leert die Konsole.
- `getl()` liest eine Zeile von stdin.
- `getc()` liest ein Zeichen von stdin.
- `format(fmt, ...)` verwendet .NET `string.Format`.

### Dateien, Pfade und Arbeitsverzeichnis

- `set_workspace(path)` setzt das aktuelle Arbeitsverzeichnis.
- `get_workspace()` liest das aktuelle Arbeitsverzeichnis.
- `getDirectory(path)` liefert den Ordnerteil eines Pfads.
- `fopen(path, mode)` oeffnet eine Datei und liefert einen `FileHandle`.
- `fexist(path)` prueft, ob eine Datei existiert.
- `readTextAsync(path)`, `writeTextAsync(path, text)`, `appendTextAsync(path, text)` sind asynchrone Dateihelfer.

`fopen` kennt diese Modi.

- `0` oeffnet nur lesend.
- `1` oeffnet oder erstellt fuer lesen und schreiben.
- `2` erstellt neu und schreibt.
- `3` oeffnet fuer Append.
- `4` oeffnet oder erstellt fuer Schreiben.

### Umgebung und Argumente

- `cmdArgs()` liefert die Skriptargumente nach `-p` oder `-params`.

### Datum und Zeit

- `DateTime()` liefert ein leeres DateTime Objekt.
- `Now()` und `Now(format)` liefern die lokale Zeit als String.
- `UtcNow()` und `UtcNow(format)` liefern die UTC Zeit als String.

### Mathematik und Zeichenpruefung

- `abs(x)` liefert den Betrag.
- `rand(seed, min, max)` liefert eine pseudozufaellige Zahl.
- `isdigit(x)`, `isletter(x)`, `isalnum(x)`, `isspace(x)` pruefen Zeichenklassen.

### JSON

- `fromjson(text)` wandelt JSON Text in CFGS Werte um.
- `tojson(value)` serialisiert einen CFGS Wert nach JSON Text.

### Async Helfer

- `task()` liefert das Task Namespace Objekt.
- `task(value)` liefert direkt eine abgeschlossene Task mit diesem Wert.
- `sleep(ms)` liefert eine awaitbare Verzogerung.
- `nextTick()` liefert einen awaitbaren Scheduling Punkt.
- `getlAsync()` und `getcAsync()` sind asynchrone Konsolenleser.

## Task Namespace Intrinsics

Das Objekt aus `task()` unterstuetzt diese Intrinsics.

- `get(value)` erzeugt eine abgeschlossene Task mit `value`.
- `completed()` erzeugt eine abgeschlossene Task mit `null`.
- `delay(ms, value = null)` wartet und liefert danach `value`.

Beispiel.

```cfs
var t = task();
var answer = await t.delay(50, 42);
print(answer);
```

## String Intrinsics

Strings unterstuetzen diese Methoden.

- `lower()`
- `upper()`
- `trim()`
- `rtrim()`
- `ltrim()`
- `len()`
- `contains(needle)`
- `substr(start, length)`
- `slice(start = null, end = null)`
- `replace_range(start, end, repl)`
- `remove_range(start, end)`
- `replace(old, new)`
- `insert_at(index, text)`
- `toDateTime(format = optional)`
- `toUnixSeconds()`
- `toUnixMilliseconds()`

Beispiel.

```cfs
var text = "  Hallo Welt  ";
print(text.trim().upper());
print(text.slice(2, 7));
```

## Array Intrinsics

Arrays unterstuetzen diese Methoden.

- `len()`
- `push(value)`
- `pop()`
- `insert_at(index, value)`
- `remove_range(start, end)`
- `replace_range(start, end, value_or_list)`
- `slice(start = null, end = null)`

Beispiel.

```cfs
var arr = [1, 2, 3];
arr.push(4);
arr.replace_range(1, 3, [20, 30]);
print(arr.slice(1, 3));
```

## Dictionary Intrinsics

Dictionaries unterstuetzen diese Methoden.

- `len()`
- `contains(key)`
- `remove(key)`
- `keys()`
- `values()`
- `set(key, value)`
- `get_or(key, fallback)`

Beispiel.

```cfs
var cfg = {"host": "localhost"};
print(cfg.contains("host"));
print(cfg.get_or("port", 80));
```

## DateTime Intrinsics

DateTime Werte unterstuetzen diese Eigenschaften und Methoden.

- `year()`
- `month()`
- `day()`
- `hour()`
- `minute()`
- `second()`
- `millisecond()`
- `dayOfWeek()`
- `dayOfYear()`
- `ticks()`
- `kind()`
- `dateOnly()`
- `timeOfDayTicks()`
- `toUnixSeconds()`
- `toUnixMilliseconds()`
- `toString(format = optional)`
- `toLocalTime()`
- `toUniversalTime()`
- `withKind(kind)`
- `addYears(n)`
- `addMonths(n)`
- `addDays(n)`
- `addHours(n)`
- `addMinutes(n)`
- `addSeconds(n)`
- `addMilliseconds(n)`
- `addTicks(n)`
- `compareTo(other)`
- `diffMs(other)`
- `diffTicks(other)`

Strings koennen ausserdem ueber `toDateTime`, `toUnixSeconds` und `toUnixMilliseconds` in DateTime Werte umgewandelt werden.

## DirectoryInfo Builtin und Intrinsics

`DirectoryInfo(path)` erzeugt ein DirectoryInfo Objekt. Ein String unterstuetzt ausserdem `dirinfo()`.

Verfuegbare DirectoryInfo Intrinsics sind.

- `exists()`
- `fullName()`
- `name()`
- `parent()`
- `root()`
- `attributes()`
- `creationTime()`
- `lastAccessTime()`
- `lastWriteTime()`
- `setCreationTime(value)`
- `setLastAccessTime(value)`
- `setLastWriteTime(value)`
- `setAttributes(value)`
- `create()`
- `delete(recursive = optional)`
- `refresh()`
- `moveTo(dest)`
- `createSubdirectory(name)`
- `getFiles(pattern = optional, recursive = optional)`
- `getDirectories(pattern = optional, recursive = optional)`
- `enumerateFileSystem(pattern = optional, recursive = optional)`
- `existsOrCreate()`
- `toString()`

Beispiel.

```cfs
var dir = "logs".dirinfo();
dir.existsOrCreate();
print(dir.fullName());
```

## FileHandle Intrinsics

Ein `FileHandle` aus `fopen` unterstuetzt diese Methoden.

- `write(text)`
- `writeln(text)`
- `flush()`
- `read(count)`
- `readline()`
- `seek(offset, origin)`
- `tell()`
- `eof()`
- `close()`
- `writeAsync(text)`
- `writelnAsync(text)`
- `flushAsync()`
- `readAsync(count)`
- `readlineAsync()`

Bei `seek` bedeutet `origin`.

- `0` Anfang
- `1` aktuelle Position
- `2` Ende

Beispiel.

```cfs
var f = fopen("demo.txt", 2);
f.writeln("Hallo");
f.flush();
f.close();
```

## Exception Intrinsics

Exception Objekte aus `catch` oder aus Async Fehlern unterstuetzen.

- `message()`
- `type()`
- `file()`
- `line()`
- `col()`
- `stack()`
- `toString()`

Beispiel.

```cfs
try {
    throw "boom";
} catch(e) {
    print(e.type());
    print(e.message());
}
```

## Sicherheitsrelevante Schalter

Datei I O kann global deaktiviert werden. Die Standardbibliothek prueft dafuer intern `AllowFileIO`. Wenn Dateioperationen abgeschaltet sind, schlagen betroffene Builtins und Intrinsics mit einer Runtime Exception fehl.

## Praktische Startrezepte

### JSON lesen und normalisieren

```cfs
var cfg = fromjson("{\"host\":\"localhost\"}");
cfg[] = {"port": 8080};
print(tojson(cfg));
```

### Einfacher Async Timer

```cfs
var t = task();
print(await t.delay(100, "fertig"));
```

### Datei schreiben

```cfs
var file = fopen("notes.txt", 2);
file.writeln("erste Zeile");
file.close();
```

## Weiterfuehrung

Die Standardbibliothek deckt den Kern ab. Fuer HTTP und SQL kommen die offiziellen Zusatzplugins dazu. Genau die zeigt die naechste Seite.
