# Standard Library

## Loading It

The standard library is loaded the same way as a plugin through a DLL import.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
```

After that, its builtins and intrinsics are available globally.

## Builtins at a Glance

### Types, Conversion, and Inspection

- `typeof(value)` returns a friendly type name such as `Int`, `String`, `Array`, `Dictionary`, `Task<Object>`, or the class name of an instance.
- `len(value)` returns the length of a string, array, or dictionary.
- `str(value)` and `string(value)` convert values to text.
- `int(value)`, `long(value)`, `double(value)`, `float(value)`, `decimal(value)`, `char(value)`, `bool(value)`, `bigint(value)` perform direct conversions.
- `toi(value)`, `toi16(value)`, `toi32(value)`, `toi64(value)` are numeric helper conversions.
- `chr(value)` converts to a char.
- `array(value)` builds an array. Arrays are copied. Single values are wrapped into a one element array.
- `dictionary(value)` builds a dictionary. Dictionaries are copied. Single values produce an empty dictionary.
- `isarray(value)` and `isdict(value)` check collection types.
- `getfields(dict)` returns the keys of a dictionary as an array.

### Output and Console

- `print(value)` writes the value followed by a newline.
- `put(value)` writes the value without a newline.
- `clear()` clears the console.
- `getl()` reads one line from stdin.
- `getc()` reads one character from stdin.
- `format(fmt, ...)` uses .NET `string.Format`.

### Files, Paths, and Working Directory

- `set_workspace(path)` sets the current working directory.
- `get_workspace()` returns the current working directory.
- `getDirectory(path)` returns the directory part of a path.
- `fopen(path, mode)` opens a file and returns a `FileHandle`.
- `fexist(path)` checks whether a file exists.
- `readTextAsync(path)`, `writeTextAsync(path, text)`, `appendTextAsync(path, text)` are asynchronous file helpers.

`fopen` supports these modes.

- `0` opens read only.
- `1` opens or creates for reading and writing.
- `2` creates a new file for writing.
- `3` opens for append.
- `4` opens or creates for writing.

### Environment and Arguments

- `cmdArgs()` returns the script arguments after `-p` or `-params`.

### Date and Time

- `DateTime()` returns an empty DateTime object.
- `Now()` and `Now(format)` return local time as a string.
- `UtcNow()` and `UtcNow(format)` return UTC time as a string.

### Math and Character Checks

- `abs(x)` returns the absolute value.
- `rand(seed, min, max)` returns a pseudo random number.
- `isdigit(x)`, `isletter(x)`, `isalnum(x)`, `isspace(x)` check character classes.

### JSON

- `fromjson(text)` converts JSON text into CFGS values.
- `tojson(value)` serializes a CFGS value into JSON text.

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

Example.

```cfs
var t = task();
var answer = await t.delay(50, 42);
print(answer);
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
- `toDateTime(format = optional)`
- `toUnixSeconds()`
- `toUnixMilliseconds()`

Example.

```cfs
var text = "  Hello World  ";
print(text.trim().upper());
print(text.slice(2, 7));
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

Example.

```cfs
var arr = [1, 2, 3];
arr.push(4);
arr.replace_range(1, 3, [20, 30]);
print(arr.slice(1, 3));
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

Strings can also be converted into DateTime values through `toDateTime`, `toUnixSeconds`, and `toUnixMilliseconds`.

## DirectoryInfo Builtin and Intrinsics

`DirectoryInfo(path)` creates a DirectoryInfo object. A string also supports `dirinfo()`.

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

A `FileHandle` returned by `fopen` supports these methods.

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

For `seek`, `origin` means the following.

- `0` beginning
- `1` current position
- `2` end

Example.

```cfs
var f = fopen("demo.txt", 2);
f.writeln("Hello");
f.flush();
f.close();
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

File I O can be disabled globally. The standard library checks `AllowFileIO` internally for that. When file operations are disabled, affected builtins and intrinsics fail with a runtime exception.

## Practical Starter Recipes

### Reading and Normalizing JSON

```cfs
var cfg = fromjson("{\"host\":\"localhost\"}");
cfg[] = {"port": 8080};
print(tojson(cfg));
```

### Simple Async Timer

```cfs
var t = task();
print(await t.delay(100, "done"));
```

### Writing a File

```cfs
var file = fopen("notes.txt", 2);
file.writeln("first line");
file.close();
```

## Next Step

The standard library covers the core runtime surface. For HTTP and SQL you add the official extra plugins. The next page walks through those directly.
