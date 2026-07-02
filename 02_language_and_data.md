# Language and Data

## Comments

CFGS supports two comment styles.

- Single line comments start with `#`.
- Block comments start with `#+` and end with `+#`.

```cfs
# Single line

#+
Multi line.
Across several lines.
+#
```

## Top Level Rules

At top level these forms are the declaration-oriented set used by imported modules and namespace bodies.

- Empty statements with `;`
- `import`
- `namespace`
- `export`
- `var` and `const`
- `func` and `async func`
- `class` and `enum`
Direct control flow statements such as `if`, `for`, `foreach`, `while`, `match`, `try`, `throw`, `break`, `continue`, and `delete` are not allowed at declaration-oriented top level. Free executable statements such as `main();` or `x = 1;` are also rejected there.

In imported modules and namespace bodies, `var` and `const` initializers are also restricted to non-executing expressions: literals, variable references, indexing/member access, collection literals, and pure operators. Calls, object construction, `await`, `out`, and function expressions belong inside functions.

Directly executed root scripts still accept the older script-style top level for compatibility. There is no auto-main: a script must call `main()` explicitly (or run imperative top-level code). Defining `main` without calling it does nothing.

## Literals

### Null and Booleans

```cfs
var a = null;
var b = true;
var c = false;
```

### Numbers

CFGS supports several number formats.

- Decimal. `42`
- Negative numbers through the unary operator. `-42`
- Floating point. `3.14`
- Scientific notation. `6.02e23`
- Underscores for readability. `1_000_000`
- Hexadecimal. `0xFF`
- Binary. `0b1010`
- Octal. `0o755`

Depending on size and shape, values are represented internally as `int`, `long`, `double`, `decimal`, or `BigInteger`.

Hexadecimal, binary, and octal literals are read as unsigned magnitudes. A literal that exceeds 64 bits promotes to `BigInteger` instead of wrapping, so `0xFFFFFFFFFFFFFFFF`, `0b1111111111111111111111111111111111111111111111111111111111111111`, and `0o1777777777777777777777` all denote the same positive value, not `-1`.

### Strings

Strings use double quotes.

```cfs
var text = "Hello";
```

Supported escape sequences include these.

- `\n`
- `\t`
- `\r`
- `\\`
- `\"`
- `\u263A`
- `\x41`

### Chars

Char literals use single quotes.

```cfs
var letter = 'A';
var newline = '\n';
```

A char literal must contain exactly one character.

When a char takes part in arithmetic, comparison, or equality, it coerces to its Unicode code point. This is uniform for every char, including digit characters, so `'A'` is 65 and `'5'` is 53.

```cfs
print('A' + 1);    # 66
print('5' + 1);    # 54
print('A' == 65);  # true
```

## Variables and Constants

### `var`

`var` creates a mutable binding.

```cfs
var port = 8080;
port = 9090;
```

### `const`

`const` always requires an initializer and cannot be rebound later.

```cfs
const appName = "Demo";
```

### Shadowing

Local names can shadow outer names. Inside methods the practical resolution order is this.

1. Local variable or parameter.
2. Matching class member.
3. Visible global name.

This matters because methods can often read fields without explicitly writing `this.`.

## Truthiness

The following values count as false in conditions.

- `null`
- `false`
- numeric `0`
- empty strings
- empty arrays
- empty dictionaries

Most other values count as true. Even NaN is treated as true in CFGS.

## Operators

CFGS covers the usual operator families.

- Arithmetic. `+`, `-`, `*`, `/`, `%`, `**`
- Comparison. `==`, `!=`, `<`, `>`, `<=`, `>=`
- Type checking. `value is ClassName`
- Logic. `!`, `&&`, `||`
- Null coalescing. `??`
- Ternary. `condition ? then : else`
- Bitwise. `&`, `|`, `^`, `<<`, `>>`
- Dictionary merge. `<<<`, `>>>`
- Increment and decrement. `++x`, `x++`, `--x`, `x--`
- Compound assignments. `+=`, `-=`, `*=`, `/=`, `%=`

Power is right associative. `2 ** 3 ** 2` is evaluated as `2 ** (3 ** 2)`.

The `+` operator is overloaded by operand type:

- Two numbers add.
- If at least one operand is a string, both operands are converted to text and concatenated.
- Two arrays concatenate into a new array (see Arrays below).
- Any other combination, such as `true + false` or `[1, 2] + {"a": 1}`, is a runtime error.

`+` no longer merges dictionaries. Use the `<<<` and `>>>` operators for that (see Dictionaries).

An arithmetic or bitwise operator with a `null` operand is a runtime error. `null + 5`, `5 - null`, and `null & 1` all fail instead of silently producing `null`.

## Arrays

Array literals look like this.

```cfs
var arr = [1, 2, 3];
```

Elements are read and written through index access.

```cfs
arr[1] = 20;
print(arr[1]);
```

Negative indices count from the end, consistent with slice bounds: `arr[-1]` is the last
element, `arr[-2]` the second to last. This applies to reading, writing, and `delete` on arrays,
and to reading characters from strings. An index whose resolved position is still outside the
collection raises a runtime error.

```cfs
print(arr[-1]);   # last element
arr[-1] = 99;     # assign to the last element
print(text[-1]);  # last character of a string
```

### Concatenation

`+` joins two arrays into a new array. The operands are not modified.

```cfs
var a = [1, 2];
var b = [3, 4];
print(a + b);   # [1, 2, 3, 4]
print(a);       # [1, 2] (unchanged)
```

### Push Syntax

The language has a dedicated append shorthand.

```cfs
arr[] = 4;
```

That is syntax sugar for pushing a value onto the array.

## Dictionaries

Dictionary literals look like this.

```cfs
var cfg = {"host": "localhost", "port": 80};
```

Data is accessed through brackets. Dot access on a dictionary is reserved for its intrinsic methods (see below), never for data keys.

```cfs
print(cfg["host"]);   # data access
```

### Merging

Two dictionaries can be merged with the `<<<` and `>>>` operators. Both return a new dictionary and do not modify their operands. The arrows point at the side that wins when a key is present in both.

```cfs
var base    = {"host": "localhost", "port": 80};
var overlay = {"port": 8080, "tls": true};

print(base >>> overlay);   # right wins on "port": {"host": "localhost", "port": 8080, "tls": true}
print(base <<< overlay);   # left wins on "port":  {"host": "localhost", "port": 80, "tls": true}
```

`a <<< b` is the same as `b >>> a`. The result keeps the left operand's key order and appends any new keys from the right operand. Both operands must be dictionaries.

For an in place merge that mutates the receiver, use the `.merge(other)` intrinsic instead.

### Dictionary Push

Dictionaries also support the push shorthand.

```cfs
cfg[] = {"mode": "safe"};
```

If you pass exactly one key value pair, the pair is merged into the dictionary. That is the clean and intended form.

## Slicing

Arrays and strings support slices through `~`.

```cfs
var arr = [10, 20, 30, 40];
var part = arr[1~3];
var text = "abcdef";
var cut = text[2~5];
```

The right boundary is exclusive.

Open bounds are also supported.

```cfs
arr[~2]
arr[2~]
arr[~]
```

### Slice Assignment

Arrays also support replacing a slice.

```cfs
arr[1~3] = [111, 222];
```

Strings are immutable. You can slice them, but you cannot overwrite string slices in place.

## Delete

`delete` is a dedicated language feature for arrays and dictionaries.

```cfs
delete arr[1];
delete arr[1~3];
delete arr;

delete cfg["host"];
delete cfg;
```

The meaning is straightforward.

- `delete arr[1];` removes one element.
- `delete arr[1~3];` removes a range.
- `delete arr;` clears the entire array.
- `delete cfg["k"];` removes one key.
- `delete cfg;` clears the entire dictionary.

Delete on strings is not allowed.

## Dictionaries: Data via Brackets, Methods via Dot

A dictionary separates data from methods by syntax, so the two never collide:

- **Bracket access** (`cfg["host"]`) reads and writes data keys. Any non-empty string is a valid key, including names that match an intrinsic method such as `merge` or `get_or`.
- **Dot access** (`cfg.merge(other)`) resolves an intrinsic method only. It never reads or writes a data key.

Because they never overlap, a data key may freely share a name with a method: `cfg["merge"]` is the value stored under `"merge"`, while `cfg.merge(...)` calls the merge intrinsic. There is no longer any reserved-key restriction.

```cfs
var cfg = {"host": "localhost", "merge": 3};
print(cfg["host"]);         # data
print(cfg["merge"]);        # data value 3
print(cfg.merge({"x": 1})); # merge intrinsic

# cfg.host       -> error: dictionaries read data with bracket access, e.g. cfg["host"]
# cfg.host = 8   -> error: assign data with bracket access, e.g. cfg["host"] = 8
```

## Strings as a First Class Type

Strings behave like primitive values with many intrinsics attached to them. The full method list is documented in [Standard Library](09_standard_library.md). The most important ones are `lower`, `upper`, `trim`, `contains`, `substr`, `slice`, `replace`, and `insert_at`.

## JSON Shapes

With `fromjson` and `tojson` you can move between JSON text and CFGS data structures.

```cfs
var obj = fromjson("{\"x\":1,\"y\":[2,3]}");
print(obj["y"][0]);

var json = tojson(obj);
print(json);
```

## Short Combined Example

```cfs
var arr = [1, 2, 3];
arr[] = 4;
arr[1~3] = [20, 30];

var cfg = {"host": "localhost", "port": 80};
cfg[] = {"mode": "safe"};
delete cfg["port"];

var text = "abcdef";
print(text[1~4]);
```

If you want to combine these data structures with conditions and loops, continue with [Control Flow and Errors](03_control_flow_and_errors.md).
