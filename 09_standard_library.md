# Standard Library

## Loading It

The non-IO standard library is built into the runtime and registered automatically, so scripts do not import it. Its builtins and intrinsics are available globally from the first line.

File-system access is deliberately not part of it. The `file` and `fs` namespaces live in the `CFGS.IO` plugin and need an explicit import. An embedding host must additionally grant `AllowPluginLoading`, and `AllowFileIO` for the operations themselves.

```cfs
import "dist/Debug/net10.0/CFGS.IO.dll";
```

## Builtins and Native Namespaces at a Glance

The standard library exposes ordinary global builtins such as `print`, `len`, `typeof`, and `task`. It also exposes these native CFGS namespaces: `console`, `encoding`, `math`, `regex`, `json`, and `eventlog`. Each of them is additionally registered under a `std.` prefix, so `math.floor` and `std.math.floor` are the same function.

The `file` and `fs` namespaces are not part of this set. They come from the `CFGS.IO` plugin shown above.

Native namespace roots are accessed directly with dot syntax. They are not factory functions.

```cfs
regex.match("abc123", "\\d+");
```

### Types, Conversion, and Inspection

- `typeof(value)` returns a friendly type name such as `Int`, `String`, `Array`, `Dictionary`, `Task<Object>`, or the class name of an instance.
- `len(value)` returns the length of a string, array, or dictionary.
- `destroy(value)` explicitly destroys a class instance or disposable handle.
- `destroy(value, true)` recursively destroys nested values found in instance fields, arrays, and dictionaries.
- `str(value)` and `string(value)` convert values to text.
- `int(value)`, `byte(value)`, `long(value)`, `double(value)`, `float(value)`, `decimal(value)`, `char(value)`, `bool(value)`, `bigint(value)` perform direct conversions.
- `toi(value)`, `toi16(value)`, `toi32(value)`, `toi64(value)` are numeric helper conversions.
- `chr(value)` converts to a char.
- `array(value)` builds an array. Arrays are copied. Single values are wrapped into a one element array.
- `dictionary(value)` builds a dictionary. Dictionaries are copied. Single values produce an empty dictionary.
- `isarray(value)` and `isdict(value)` check collection types.
- `getfields(dict)` returns the keys of a dictionary as an array.
- `encoding.encode(text, encoding = "utf-8")` encodes text into an array of byte values.
- `encoding.decode(bytes, encoding = "utf-8")` decodes an array of byte values into text.
- `encoding.convert(bytes, fromEncoding, toEncoding)` decodes bytes with one encoding and re-encodes them with another.
- `encoding.has(nameOrCodePage)` checks whether an encoding is available.
- `encoding.info(nameOrCodePage)` returns encoding metadata.
- `encoding.names()` returns all available encoding names.
- `encoding.all()` returns all available encodings with name, display name, and code page.

Example.

```cfs
print(typeof(byte(255)));
print(byte(0x41));
print(byte(300));   # runtime error: value must be between 0 and 255
```

```cfs
var bytes = encoding.encode("Hi");
print(encoding.decode(bytes));
```

Encoding names can be standard names such as `"utf-8"` or codepage names such as `"windows-1252"`. Numeric code pages such as `65001` or `1252` are also accepted.

Cleanup example.

```cfs
class Resource(name) {
    var name = "";

    func init(name) {
        this.name = name;
    }

    private func destroy() {
        print("destroy " + this.name);
    }
}

var r = new Resource("cache");
destroy(r);
```

### Output and Console

- `console.print(value)` writes the value followed by a newline.
- `console.put(value)` writes the value without a newline.
- `console.clear()` clears the console.
- `console.getl()` / `console.readLine()` read one line from stdin.
- `console.getc()` / `console.readChar()` read one character from stdin.
- `console.getlAsync()` / `console.readLineAsync()` are asynchronous line readers.
- `console.getcAsync()` / `console.readCharAsync()` are asynchronous character readers.
- `print(value)` writes the value followed by a newline.
- `put(value)` writes the value without a newline.
- `clear()` clears the console.
- `getl()` reads one line from stdin.
- `getc()` reads one character from stdin.
- `format(fmt, ...)` uses .NET `string.Format`.

### Windows Event Log

The `eventlog` namespace reads from and writes to the Windows Event Log. It is Windows only. On any other platform every call raises a runtime error. Creating a new event source needs administrator rights the first time, so a `write` to an unknown source from a non-elevated process fails with a clear error.

- `eventlog.write(source, message, level = "info")` writes an entry. `level` is one of `"info"`, `"warning"`, or `"error"`. The source is created automatically if it does not exist yet.
- `eventlog.read(logName, count = 50)` returns up to `count` entries from a log such as `"Application"`, newest first. Each entry is a dictionary with the keys `source`, `message`, `level`, `time`, and `id`.
- `eventlog.exists(source)` returns whether an event source is registered.
- `eventlog.create(source, logName = "Application")` registers an event source.
- `eventlog.remove(source)` removes an event source.

```cfs
eventlog.write("MyApp", "Service started", "info");

var entries = eventlog.read("Application", 5);
foreach (var i, e in entries) {
    console.print(e["level"] + " " + e["source"] + ": " + e["message"]);
}
```

### Files, Paths, and Working Directory

- `fs` and `file` are aliases for the file-system namespace.
- `fs.open(path, mode)` opens a text/binary-capable `FileHandle`.
- `fs.openBinary(path, mode)` opens a `BinaryFileHandle`.
- `fs.exists(path)`, `fs.copy(source, dest, overwrite = false)`, `fs.move(source, dest, overwrite = false)`, and `fs.delete(path)` perform common file operations.
- `fs.readAllBytes(path)`, `fs.readAllBytesAsync(path)`, and `fs.writeAllBytes(path, bytes)` handle byte arrays.
- `fs.readTextAsync(path)`, `fs.writeTextAsync(path, text)`, and `fs.appendTextAsync(path, text)` are asynchronous text helpers.
- `fs.size(path)`, `fs.lastWriteTime(path)`, and `fs.lastWriteTimeUtc(path)` return file metadata.
- `fs.setWorkspace(path)`, `fs.getWorkspace()`, `fs.getDirectory(path)`, and `fs.directoryInfo(path)` wrap working-directory and directory-info helpers.
- The `file` alias exposes the same members, for example `file.size(path)` and `file.exists(path)`.
- `ScriptPath()` returns the entry script path of the current run. In the REPL it returns `<repl>`.
- `tobyte(value)` converts a scalar value to one byte. If `value` is an array of 1..8 bits, it packs them left to right into one byte.
- `tobytes(value)` converts text to UTF-8 bytes, validates byte arrays, converts scalar values to a one-byte array, and converts `null` to an empty array.
- `tobytes(value, encoding)` uses the named text encoding when `value` is text.

`fs.open` supports these modes.

- `0` opens read only.
- `1` opens or creates for reading and writing.
- `2` creates a new file for writing.
- `3` opens for append.
- `4` opens or creates for writing.

`fs.openBinary` uses the same mode values, but the returned handle works on raw bytes instead of UTF-8 text.

Small binary example.

```cfs
fs.writeAllBytes("demo.bin", [0x41, 0x42, 0x43]);
var bytes = fs.readAllBytes("demo.bin");
bytes[1] = byte(bytes[1] | 0x20);
fs.writeAllBytes("demo.bin", bytes);
```

### Environment and Arguments

- `cmdArgs()` returns the script arguments after `-p` or `-params`.

### Date and Time

These return real `DateTime` and `TimeSpan` values, so the intrinsics listed further below apply to the result.

- `now()` returns the current local time, `utcNow()` the current UTC time, and `today()` the current date at midnight.
- `datetime(year, month, day, hour = 0, minute = 0, second = 0, millisecond = 0)` builds a value from components.
- `parseDate(text, format = optional)` parses a string and raises a runtime error on failure. `tryParseDate(text, format = optional)` returns `null` on failure instead.
- `fromUnixSeconds(seconds)` and `fromUnixMilliseconds(ms)` build a UTC value from a Unix timestamp.
- `daysInMonth(year, month)` and `isLeapYear(year)` are calendar helpers.
- `timespan(days, hours, minutes, seconds, milliseconds = 0)` builds a duration. A duration is also what `dateA.diff(dateB)` returns.

### Math and Character Checks

- `math.abs(x)`, `math.floor(x)`, `math.ceil(x)`, `math.round(x, digits = optional)`, `math.min(a, b)`, and `math.max(a, b)` provide basic numeric helpers.
- `math.sqrt(x)`, `math.pow(base, exp)`, `math.log(x, base = optional)`, `math.log10(x)`, `math.sin(x)`, `math.cos(x)`, and `math.tan(x)` provide scientific helpers.
- `math.sign(x)`, `math.trunc(x)`, `math.rand(seed, min, max)`, `math.pi()`, and `math.e()` provide sign, truncation, random, and constant helpers. The constants are also available uppercase as `math.PI` and `math.E`.
- `isdigit(x)`, `isletter(x)`, `isalnum(x)`, `isspace(x)` check character classes.

### Regular Expressions

- `regex.match(input, pattern)` returns the first match as a dictionary with `matched`, `index`, and `groups`, or `null`.
- `regex.matches(input, pattern)` returns all matches as a list of dictionaries.
- `regex.replace(input, pattern, replacement)` replaces all matches and returns the result string.
- `regex.test(input, pattern)` returns `true` if the pattern matches anywhere in the input.
- `regex.split(input, pattern)` splits the input by the pattern and returns an array.

Example.

```cfs
print(regex.test("hello123", "\\d+"));
var m = regex.match("abc 42 def", "(\\d+)");
print(m["matched"]);
print(regex.replace("foo bar baz", "\\s+", "-"));
```

### JSON

- `json.parse(text)` converts JSON text into CFGS values.
- `json.stringify(value)` serializes a CFGS value into JSON text.
- `json.from(text)` and `json.to(value)` are short aliases for parse and stringify inside the JSON namespace.

### Async Helpers

- `task()` returns the task namespace object.
- `task(value)` returns an already completed task with that value.
- `sleep(ms)` returns an awaitable delay.
- `nextTick()` returns an awaitable scheduling point.
- `getlAsync()` and `getcAsync()` are asynchronous console readers.

## Task Namespace Intrinsics

The object returned by `task()` supports these intrinsics.

- `get(value)` creates a completed task with `value`.
- `completed()` creates a completed task with `null`.
- `delay(ms, value = null)` waits and then returns `value`.

Important detail. `delay` takes a value, not a callback. That means the `value` expression is evaluated before the wait starts.

Example.

```cfs
var t = task();
var answer = await t.delay(50, 42);
print(answer);
```

For delayed side effects, put the side effect after the `await`.

```cfs
var t = task();
var _ = await t.delay(50);
print("done");
```

## String Intrinsics

Strings support these methods.

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
- `split(separator)` splits the string by a separator and returns an array. An empty separator splits into individual characters.
- `startsWith(prefix)` returns `true` if the string starts with the given prefix.
- `endsWith(suffix)` returns `true` if the string ends with the given suffix.
- `indexOf(needle)` returns the zero based index of the first occurrence, or -1.
- `lastIndexOf(needle)` returns the zero based index of the last occurrence, or -1.
- `repeat(count)` repeats the string the given number of times.
- `padStart(totalLength, padChar)` pads the beginning of the string.
- `padEnd(totalLength, padChar)` pads the end of the string.
- `toDateTime(format = optional)`
- `toUnixSeconds()`
- `toUnixMilliseconds()`

Example.

```cfs
var text = "  Hello World  ";
print(text.trim().upper());
print(text.slice(2, 7));
```

## String Builder

Building a string with repeated `s = s + piece` rebuilds the whole string on every step, which is
quadratic for a growing result. `strbuilder()` returns a mutable accumulator that appends in place
(amortized linear), then materializes once.

- `strbuilder()` / `strbuilder(initial)` — create a builder, optionally seeded with an initial value.
- `append(value)` — append the string form of `value` (same rendering as `str()`); returns the builder,
  so calls chain.
- `str()` / `toString()` — materialize the accumulated text into a normal string.
- `len()` — current length (the `len()` builtin also works on a builder).
- `clear()` — reset to empty; returns the builder.

```cfs
var b = strbuilder();
var i = 0;
while (i < 1000) {
    b.append("row-").append(str(i)).append("\n");
    i = i + 1;
}
var report = b.str();
print(len(report));
```

## Array Intrinsics

Arrays support these methods.

- `len()`
- `push(value)`
- `pop()`
- `insert_at(index, value)`
- `remove_range(start, end)`
- `replace_range(start, end, value_or_list)`
- `slice(start = null, end = null)`
- `sort()` sorts the array in place and returns it.
- `reverse()` reverses the array in place and returns it.
- `indexOf(value)` returns the zero based index of the first occurrence, or -1.
- `includes(value)` returns `true` if the value is found.
- `join(separator)` joins the elements into a string.
- `flat(depth = 1)` flattens nested arrays up to the given depth.
- `map(fn)` returns a new array with each element transformed by `fn(element, index)`.
- `filter(fn)` returns a new array with only the elements where `fn(element, index)` returns truthy.
- `reduce(fn, initial)` reduces the array using `fn(accumulator, element, index)`.
- `find(fn)` returns the first element where `fn(element, index)` returns truthy, or `null`.
- `findIndex(fn)` returns the index of the first element where `fn(element, index)` returns truthy, or -1.
- `every(fn)` returns `true` if `fn(element, index)` returns truthy for every element.
- `some(fn)` returns `true` if `fn(element, index)` returns truthy for at least one element.

Example.

```cfs
var arr = [1, 2, 3];
arr.push(4);
arr.replace_range(1, 3, [20, 30]);
print(arr.slice(1, 3));
```

### Higher Order Example

```cfs
var nums = [3, 1, 4, 1, 5];
var doubled = nums.map(func(x) { return x * 2; });
var evens = nums.filter(func(x) { return x % 2 == 0; });
var sum = nums.reduce(func(acc, x) { return acc + x; }, 0);
print(doubled);
print(evens);
print(sum);
print(nums.sort());
print(nums.includes(4));
print([1, [2, [3]]].flat(2));
```

## Dictionary Intrinsics

Dictionaries support these methods.

- `len()`
- `contains(key)`
- `remove(key)`
- `keys()`
- `values()`
- `set(key, value)`
- `get_or(key, fallback)`
- `entries()` returns a list of `[key, value]` pairs.
- `merge(other)` merges another dictionary into this one and returns it. Existing keys are overwritten.
- `clone()` returns a shallow copy of the dictionary.

Example.

```cfs
var cfg = {"host": "localhost"};
print(cfg.contains("host"));
print(cfg.get_or("port", 80));
```

## DateTime Intrinsics

DateTime values support these properties and methods.

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
- `isLeapYear()` and `daysInMonth()` report calendar facts about this value.
- `startOfDay()`, `endOfDay()`, `startOfMonth()`, and `endOfMonth()` snap to a boundary.
- `iso()` formats as ISO 8601 (`yyyy-MM-ddTHH:mm:ss`). `monthName()` and `weekdayName()` give the invariant full names. `format(pattern)` is an alias of `toString(pattern)`.
- `isBefore(other)`, `isAfter(other)`, `equals(other)`, and `isBetween(a, b)` compare values.
- `diff(other)` returns the gap to another date as a `TimeSpan`. `add(span)` returns this value plus a `TimeSpan`. `subtract(x)` returns a `TimeSpan` when `x` is a date, or a `DateTime` when `x` is a `TimeSpan`.

Strings can also be converted into DateTime values through `toDateTime`, `toUnixSeconds`, and `toUnixMilliseconds`.

## TimeSpan Intrinsics

A TimeSpan is a duration, returned by `dateA.diff(dateB)` or built with `timespan(...)`.

- `totalDays()`, `totalHours()`, `totalMinutes()`, `totalSeconds()`, and `totalMilliseconds()` give the whole span as a fractional amount of that unit.
- `days()`, `hours()`, `minutes()`, `seconds()`, `milliseconds()`, and `ticks()` give the individual components.
- `negate()`, `abs()`, and `isZero()` transform or test the value.
- `add(other)`, `subtract(other)`, and `compareTo(other)` combine or order two durations.
- `toString(format = optional)` formats the value.

## DirectoryInfo Helpers and Intrinsics

`fs.directoryInfo(path)` creates a DirectoryInfo object. A string also supports `dirinfo()`.

Available DirectoryInfo intrinsics are.

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
- `deleteDir(recursive = optional)` alias for scripts, because `delete` is also a language keyword
- `refresh()`
- `moveTo(dest)`
- `createSubdirectory(name)`
- `getFiles(pattern = optional, recursive = optional)`
- `getDirectories(pattern = optional, recursive = optional)`
- `enumerateFileSystem(pattern = optional, recursive = optional)`
- `existsOrCreate()`
- `toString()`

Example.

```cfs
var dir = "logs".dirinfo();
dir.existsOrCreate();
print(dir.fullName());
```

## FileHandle Intrinsics

A `FileHandle` returned by `fs.open` supports these methods.

- `write(text)`
- `writeln(text)`
- `flush()`
- `read(count)`
- `readline()`
- `writeByte(value)`
- `writeBytes(bytes)`
- `readByte()`
- `readBytes(count)`
- `seek(offset, origin)`
- `tell()`
- `position()`
- `setPosition(position)`
- `length()`
- `eof()`
- `close()`
- `writeAsync(text)`
- `writelnAsync(text)`
- `flushAsync()`
- `readAsync(count)`
- `readlineAsync()`
- `writeByteAsync(value)`
- `writeBytesAsync(bytes)`
- `readByteAsync()`
- `readBytesAsync(count)`

For `seek`, `origin` means the following.

- `0` beginning
- `1` current position
- `2` end

Example.

```cfs
var f = fs.open("demo.txt", 2);
f.writeln("Hello");
f.flush();
f.close();
```

## BinaryFileHandle Intrinsics

A `BinaryFileHandle` returned by `fs.openBinary` supports these methods.

- `writeByte(value)`
- `writeBytes(bytes)`
- `flush()`
- `readByte()`
- `readBytes(count)`
- `seek(offset, origin)`
- `tell()`
- `position()`
- `setPosition(position)`
- `length()`
- `eof()`
- `close()`
- `writeByteAsync(value)`
- `writeBytesAsync(bytes)`
- `flushAsync()`
- `readByteAsync()`
- `readBytesAsync(count)`

Byte arrays use normal CFGS arrays whose elements must stay in the range `0..255`.

`readByte()` returns the next byte as an integer `0..255`. At end of file it returns `-1`.

Example: whole file patch.

```cfs
var f = fs.openBinary("demo.bin", 2);
f.writeBytes([0x41, 0x42, 0x43, 0x44]);
f.flush();
f.close();

var inp = fs.openBinary("demo.bin", 0);
var bytes = inp.readBytes(4);
bytes[0] = bytes[0] | 0x20;
inp.close();

fs.writeAllBytes("demo.bin", bytes);
```

Example: patch a single byte in place.

```cfs
var f = fs.openBinary("demo.bin", 1);
f.seek(1, 0);
var b = f.readByte();
f.seek(-1, 1);
f.writeByte(byte(b ^ 0x20));
f.flush();
f.close();
```

Example: async read of a fixed header.

```cfs
async func read_magic(path) {
    var f = fs.openBinary(path, 0);
    var header = await f.readBytesAsync(4);
    f.close();
    return header;
}
```

## Exception Intrinsics

Exception objects from `catch` or from async failures support these methods.

- `message()`
- `type()`
- `file()`
- `line()`
- `col()`
- `stack()`
- `toString()`

Example.

```cfs
try {
    throw "boom";
} catch(e) {
    print(e.type());
    print(e.message());
}
```

## Security Relevant Switches

File I O can be disabled per VM. The standard library checks `AllowFileIO` internally for that. When file operations are disabled, affected builtins and intrinsics fail with a runtime exception. The permission belongs to the VM running the script, so two scripts hosted in the same process can be granted different file access, and a hot started async call inherits the permission of the VM that started it.

## Practical Starter Recipes

### Reading and Normalizing JSON

```cfs
var cfg = json.parse("{\"host\":\"localhost\"}");
cfg[] = {"port": 8080};
print(json.stringify(cfg));
```

### Simple Async Timer

```cfs
var t = task();
print(await t.delay(100, "done"));
```

### Writing a File

```cfs
var file = fs.open("notes.txt", 2);
file.writeln("first line");
file.close();
```

## Next Step

The standard library covers the core runtime surface. For HTTP and SQL you add the official extra plugins. The next page walks through those directly.
