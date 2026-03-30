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

Example.

```cfs
print(typeof(byte(255)));
print(byte(0x41));
print(byte(300));   # runtime error: value must be between 0 and 255
```

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
- `fbopen(path, mode)` opens a file and returns a `BinaryFileHandle`.
- `fexist(path)` checks whether a file exists.
- `readAllBytes(path)` reads the full file into an array of byte values.
- `writeAllBytes(path, bytes)` writes an array of byte values and returns the written byte count.
- `readTextAsync(path)`, `writeTextAsync(path, text)`, `appendTextAsync(path, text)` are asynchronous file helpers.

`fopen` supports these modes.

- `0` opens read only.
- `1` opens or creates for reading and writing.
- `2` creates a new file for writing.
- `3` opens for append.
- `4` opens or creates for writing.

`fbopen` uses the same mode values, but the returned handle works on raw bytes instead of UTF-8 text.

Small binary example.

```cfs
writeAllBytes("demo.bin", [0x41, 0x42, 0x43]);
var bytes = readAllBytes("demo.bin");
bytes[1] = byte(bytes[1] | 0x20);
writeAllBytes("demo.bin", bytes);
```

### Environment and Arguments

- `cmdArgs()` returns the script arguments after `-p` or `-params`.

### Date and Time

- `DateTime()` returns an empty DateTime object.
- `Now()` and `Now(format)` return local time as a string.
- `UtcNow()` and `UtcNow(format)` return UTC time as a string.

### Math and Character Checks

- `abs(x)` returns the absolute value.
- `rand(seed, min, max)` returns a pseudo random number.
- `floor(x)` rounds down to the nearest integer.
- `ceil(x)` rounds up to the nearest integer.
- `round(x)` rounds to the nearest integer. `round(x, digits)` rounds to a given number of decimal places.
- `min(a, b)` returns the smaller value.
- `max(a, b)` returns the larger value.
- `sqrt(x)` returns the square root.
- `pow(base, exp)` returns `base` raised to the power `exp`.
- `log(x)` returns the natural logarithm. `log(x, base)` returns the logarithm with the given base.
- `log10(x)` returns the base 10 logarithm.
- `sin(x)`, `cos(x)`, `tan(x)` are trigonometric functions accepting radians.
- `sign(x)` returns -1, 0, or 1.
- `trunc(x)` truncates toward zero.
- `PI` and `E` are constants.
- `isdigit(x)`, `isletter(x)`, `isalnum(x)`, `isspace(x)` check character classes.

### Regular Expressions

- `regex_match(input, pattern)` returns the first match as a dictionary with `matched`, `index`, and `groups`, or `null`.
- `regex_matches(input, pattern)` returns all matches as a list of dictionaries.
- `regex_replace(input, pattern, replacement)` replaces all matches and returns the result string.
- `regex_test(input, pattern)` returns `true` if the pattern matches anywhere in the input.
- `regex_split(input, pattern)` splits the input by the pattern and returns an array.

Example.

```cfs
print(regex_test("hello123", "\\d+"));
var m = regex_match("abc 42 def", "(\\d+)");
print(m["matched"]);
print(regex_replace("foo bar baz", "\\s+", "-"));
```

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

This is not delayed.

```cfs
var t = task();
var _ = await t.delay(50, out {
    print("done");
});
```

In that version the `print("done")` runs immediately and `delay` later returns `null`.

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
var doubled = nums.map(func(x) => x * 2);
var evens = nums.filter(func(x) => x % 2 == 0);
var sum = nums.reduce(func(acc, x) => acc + x, 0);
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

## BinaryFileHandle Intrinsics

A `BinaryFileHandle` returned by `fbopen` supports these methods.

- `writeByte(value)`
- `writeBytes(bytes)`
- `flush()`
- `readByte()`
- `readBytes(count)`
- `seek(offset, origin)`
- `tell()`
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
var f = fbopen("demo.bin", 2);
f.writeBytes([0x41, 0x42, 0x43, 0x44]);
f.flush();
f.close();

var inp = fbopen("demo.bin", 0);
var bytes = inp.readBytes(4);
bytes[0] = bytes[0] | 0x20;
inp.close();

writeAllBytes("demo.bin", bytes);
```

Example: patch a single byte in place.

```cfs
var f = fbopen("demo.bin", 1);
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
    var f = fbopen(path, 0);
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
