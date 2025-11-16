# Standard Builtins/Intrinsics

> File I/O can be disabled globally via `AllowFileIO = false` in the host. When disabled, any file/dir operation throws a runtime error.

---

## Contents

* Built-ins

  * Console & Text
  * Conversion & Types
  * Numbers & Random
  * Arrays & Dictionaries (helpers)
  * JSON
  * Paths & Process
  * Date/Time (constructors)
  * File I/O (constructors)
  * Async Utilities
* Intrinsics

  * String methods
  * Array methods
  * Dictionary methods
  * DateTime methods
  * DirectoryInfo methods (+ string → dirinfo)
  * FileHandle methods
  * ExceptionObject methods
* Notes on `typeof` and Tasks
* Error handling (exceptions)

---

## Built-ins

### Console & Text

* `print(x)` → writes value + newline.
* `put(x)` → writes value without newline.
* `clear()` → clears console.
* `getl()` → reads a line (string).
* `getc()` → reads one character (returns int code).

**Examples**

```cfgs
print("hello");
put("> "); var name = getl();
print("Hi " + name);
```

---

### Conversion & Types

* `str(x)` → string representation (null preserved as null).
* `toi(x)` → int/float normalization: returns Int64 or Double.
* `toi16(x)`, `toi32(x)`, `toi64(x)`
* `chr(x)` → numeric to char.
* `typeof(x)` → friendly type name (see “Notes on typeof”).

**Examples**

```cfgs
print(str(123));       # "123"
print(toi("42"));      # 42
print(chr(65));        # "A"
print(typeof(3.14));   # "Double"
```

---

### Numbers & Random

* `abs(x)` → absolute value.
* `rand(seed, min, max)` → deterministic random int in `[min, max)`.

**Example**

```cfgs
var r = rand(123, 0, 10);
print("r=" + r);
```

---

### Arrays & Dictionaries (helpers)

* `isarray(x)` → true if list.
* `isdict(x)` → true if dictionary.
* `getfields(dict)` → list of keys.

**Example**

```cfgs
var d = {"a":1, "b":2};
print(isdict(d));             # true
print(json(getfields(d)));    # ["a","b"]
```

---

### JSON
---

### `fromjson`
```cfgs
fromjson(text) -> dictionary | null
```
* Takes a single value `text`.
* Converts it to a string internally.
* If the string is `null`, empty, or only whitespace → returns `null`.
* Otherwise, parses the string as JSON and returns a **dictionary** representing the JSON object.
* Expects the JSON to represent an object at the top level (e.g. `{ "key": value }`).
  Invalid JSON will cause a runtime error.

```cfgs
var data = fromjson("{\"name\":\"Alice\",\"age\":30}");

print(data["name"]);   # "Alice"
print(data["age"]);    # 30
```

If the string is empty or whitespace:

```cfgs
fromjson("")        # null
fromjson("   ")     # null
```

You can also combine it with other operations:

```cfgs
var raw  = readfile("config.json");
var conf = fromjson(raw);

if (conf != null) {
    print(conf["mode"]);
}
```

---

### `tojson`

```cfgs
tojson(value) -> string
```

* Takes any value and converts it to a JSON string.
* Always returns a **string**.
* If `value` is `null`, the result is the string `"null"`.

```cfgs
var obj = { "name": "Bob", "score": 42 };
var json = tojson(obj);

print(json);   # e.g. {"name":"Bob","score":42}
```

With simple values:

```cfgs
tojson(5)          # "5"
tojson("hello")    # "\"hello\""
tojson(null)       # "null"
```

Round-trip example:

```cfgs
var original = { "x": 1, "y": 2 };
var json     = tojson(original);
var copy     = fromjson(json);

print(copy["x"]);    # 1
```

---

### Paths & Process

* `set_workspace(path)` → changes current directory (throws if I/O disabled).
* `get_workspace()` → returns current working directory.
* `getDirectory(path)` → returns directory part of a path.
* `cmdArgs()` → returns array of command-line args (excluding executable).
* `DirectoryInfo(path)` → returns a `DirectoryInfo` handle (see intrinsics).

**Examples**

```cfgs
set_workspace("C:/temp");
print(get_workspace());
print(getDirectory("C:/tmp/file.txt"));   # "C:/tmp"

var di = DirectoryInfo(".");
print(di.fullName());
```

---

### Date/Time (constructors)

* `DateTime()` → default DateTime (min value).
* `Now()` → local current DateTime.
* `UtcNow()` → UTC current DateTime.

**Example**

```cfgs
var now = Now();
print(now.toString());               # "YYYY-MM-DD HH:MM:SS"
print(now.toUnixSeconds());
```

---

### File I/O (constructors)

* `fopen(path, mode)` → returns `FileHandle`
  Modes:
  `0=Open/Read`, `1=OpenOrCreate/ReadWrite`, `2=Create/Write`, `3=Append/Write`, `4=OpenOrCreate/Write`

**Example**

```cfgs
var fh = fopen("out.txt", 2);    # create/truncate for write
fh.writeln("hello");
fh.close();
```

> All file/dir operations throw if `AllowFileIO=false`.

---

### Async Utilities

All below return a **Task**; you can `await` them or let the VM synchronously unwrap (if your host uses SmartAwait). To run in parallel, store tasks in variables and `await` later.

* `sleep(ms)` → completes after `ms`.
* `nextTick()` → yields once.
* `getlAsync()` → async line read.
* `getcAsync()` → async char read; returns int or -1.
* `readTextAsync(path)` → async file read.
* `writeTextAsync(path, text)` → async write; returns 1.
* `appendTextAsync(path, text)` → async append; returns 1.

**Examples**

```cfgs
# 1) Simple await
await sleep(250);

# 2) Parallel
var t1 = readTextAsync("a.txt");
var t2 = readTextAsync("b.txt");
var a = await t1;
var b = await t2;

# 3) Async console
put("> "); var line = await getlAsync();
print("you typed: " + line);
```

---

# String Builtin

### `format`

The built-in `format` function creates a formatted string from a **template** and one or more **values**.

* The **first argument** is the format string.
* All **following arguments** are the values that will be inserted into the placeholders.
* It always returns the **formatted string**.

#### Basic usage

Placeholders are written as `{0}`, `{1}`, `{2}`, … and refer to the argument positions **after** the format string.

```cfgs
format("Hello {0}", "World");          # "Hello World"
format("{0} + {1} = {2}", 2, 3, 5);    # "2 + 3 = 5"
```

#### Multiple values

```cfgs
var msg = format("Name: {0}, Age: {1}", "Alice", 30);
print(msg);    # "Name: Alice, Age: 30"
```

#### Reusing the same argument

```cfgs
format("{0}, {0}, {0}!", "Echo");   # "Echo, Echo, Echo!"
```

#### With numeric formatting

You can also use format specifiers inside the braces:

```cfgs
format("Price: {0:0.00}", 12.5);             # "Price: 12.50"
format("Padded number: {0:0000}", 42);       # "Padded number: 0042"
format("Percent: {0:0.00}%", 0.1234 * 100);  # "Percent: 12.34%"
```

#### Combining with variables

```cfgs
var name = "Bob";
var score = 97;
var line = format("Player {0} scored {1} points.", name, score);
print(line);   # "Player Bob scored 97 points."
```

In short:
`format(template, arg1, arg2, ...)` → returns a new string with all `{n}` placeholders replaced by the corresponding arguments.


## Intrinsics

### String methods (receiver: `string`)

* `len()` → length (int)
* `contains(substr)` → bool
* `substr(start, length)` → substring
* `slice([start], [endEx])` → Python-like slicing; negative indices accepted; `endEx` exclusive
* `replace_range(start, endEx, repl)`
* `remove_range(start, endEx)`
* `replace(old, new)` → replace all
* `insert_at(index, repl)`

**Examples**

```cfgs
var s = "abcdef";
print(s.len());                  # 6
print(s.substr(2, 3));           # "cde"
print(s.slice(1, -1));           # "bcde"
print(s.replace("cd","xx"));     # "abxxef"
print(s.insert_at(3,"!"));       # "abc!def"
```

---

### Array methods (receiver: `Array` = `List<object>`)

* `len()` → count
* `push(x)` → append, returns new length
* `pop()` → remove last, return it (or null if empty)
* `insert_at(index, x)` → in-place; returns array
* `remove_range(start, endEx)` → in-place; returns array
* `replace_range(start, endEx, valueOrArray)` → in-place splice
* `slice([start], [endEx])` → returns new sub-array

**Examples**

```cfgs
var a = [1,2,3];
a.push(4);            # [1,2,3,4]
print(a.pop());       # 4
print(json(a.slice(1)));  # [2,3]
```

---

### Dictionary methods (receiver: `Dictionary<string, object>`)

* `len()` → number of keys
* `contains(key)` → bool
* `remove(key)` → bool (true if key existed)
* `keys()` → array of keys
* `values()` → array of values
* `set(key, value)` → returns dict (for chaining)
* `get_or(key, default)` → value or default

**Examples**

```cfgs
var d = {"a":1};
d.set("b",2);
print(d.contains("b"));           # true
print(d.get_or("x", 99));         # 99
print(json(d.keys()));            # ["a","b"]
```

---

### DateTime methods (receiver: `DateTime`)

Getters:

* `year()`, `month()`, `day()`, `hour()`, `minute()`, `second()`, `millisecond()`
* `dayOfWeek()`, `dayOfYear()`, `ticks()`, `kind()`
* `dateOnly()`, `timeOfDayTicks()`

Unix & string:

* `toUnixSeconds()`, `toUnixMilliseconds()`
* `toString([format])` (`"yyyy-MM-dd HH:mm:ss"` default)

Conversions:

* `toLocalTime()`, `toUniversalTime()`, `withKind(kind)` where kind: `"Utc"|"Local"|"Unspecified"|0|1|2`

Arithmetic:

* `addYears(n)`, `addMonths(n)`, `addDays(n)`, `addHours(n)`, `addMinutes(n)`, `addSeconds(n)`, `addMilliseconds(n)`, `addTicks(n)`

Compare & diff:

* `compareTo(other)` → -1/0/1
* `diffMs(other)`, `diffTicks(other)`

String helpers (receiver: `string`):

* `"2024-01-01 12:00:00".toDateTime([fmt])`
* `"1700000000".toUnixSeconds()` → DateTime
* `"1700000000000".toUnixMilliseconds()` → DateTime

**Examples**

```cfgs
var now = Now();
print(now.toString("yyyy-MM-dd"));

var dt = "2024-12-24 18:30:00".toDateTime("yyyy-MM-dd HH:mm:ss");
print(now.compareTo(dt));
print(now.diffMs(dt));
```

---

### DirectoryInfo methods (receiver: `DirectoryInfo`)

(+ string intrinsic: `somePath.dirinfo()`)

Info:

* `exists()`, `fullName()`, `name()`, `parent()`, `root()`, `attributes()`

Timestamps:

* `creationTime()`, `lastAccessTime()`, `lastWriteTime()`

Setters (require I/O enabled):

* `setCreationTime(dt)`, `setLastAccessTime(dt)`, `setLastWriteTime(dt)`
* `setAttributes(flagsLong)`

Ops (require I/O enabled):

* `create()`, `delete([recursiveBool])`, `refresh()`, `moveTo(dest)`
* `createSubdirectory(name)`
* `getFiles([pattern], [recursiveBool])` → array of file paths
* `getDirectories([pattern], [recursiveBool])` → array of directory paths
* `enumerateFileSystem([pattern], [recursiveBool])` → array of paths
* `existsOrCreate()` → bool
* `toString()` → full path

String → DirectoryInfo:

* `path.dirinfo()` → DirectoryInfo

**Examples**

```cfgs
var di = ".".dirinfo();
print(di.exists());
print(json(di.getFiles("*.txt", false)));
var sub = di.createSubdirectory("data");
print(sub.fullName());
```

---

### FileHandle methods (receiver: `FileHandle`)

Sync:

* `write(text)`, `writeln(text)`, `flush()`
* `read(count)` → string
* `readline()` → string
* `seek(offset, origin)` → new position; origin: `0=Begin,1=Current,2=End`
* `tell()` → current position (long)
* `eof()` → bool
* `close()` → 1

Async variants (return Tasks):

* `writeAsync(text)`, `writelnAsync(text)`, `flushAsync()`
* `readAsync(count)`, `readlineAsync()`

**Examples**

```cfgs
var fh = fopen("log.txt", 3);     # append
fh.writeln("start");
fh.flush();
fh.close();

# Async
var fa = fopen("async.txt", 2);
await fa.writelnAsync("hello async");
await fa.flushAsync();
fa.close();
```

---

### ExceptionObject methods (receiver: `ExceptionObject`)

* `message()`, `type()`, `file()`, `line()`, `col()`, `stack()`, `toString()`

**Example**

```cfgs
try {
    set_workspace("/root/forbidden");
} catch (e) {
    print("Error " + e.type() + ": " + e.message());
}
```

---

## Notes on `typeof` and Tasks

`typeof(x)` returns a **friendly type**:

* Primitives: `"Boolean"`, `"Int"`, `"Long"`, `"Double"`, `"Float"`, `"Decimal"`, `"String"`, `"Char"`
* Containers: `"Array"`, `"Dictionary"`
* Handles: class name (e.g., `FileHandle`, `DirectoryInfo`)
* Exceptions: `"Exception"`
* Tasks: `"Task"` or `"Task<T>"`, and similarly `"ValueTask"` / `"ValueTask<T>"`

**Example**

```cfgs
var t = sleep(100);
print(typeof(t));          # e.g., "Task"
var r = await t;           # r is null
```

---

## Error Handling

* Built-ins and intrinsics throw **runtime exceptions** on invalid input or when file I/O is disabled.
* These surface as `ExceptionObject` and can be handled with `try { ... } catch (e) { ... }`.

**Example**

```cfgs
try {
    var fh = fopen("missing.txt", 0);
    print(fh.readline());
} catch (e) {
    print(e.toString());
}
```

---

### Quick End-to-End Example

```cfgs
# Write & read a file, then print stats about the folder

set_workspace("C:/temp");

var fh = fopen("demo.txt", 2);   # create/truncate
fh.writeln("Hello CFGS");
fh.close();

var text = await readTextAsync("demo.txt");
print("content=" + text);

var di = ".".dirinfo();
print("Files here: " + str(len(di.getFiles("*", false))));
print("Now: " + Now().toString());
```

[Back](README.md)
