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

At top level these forms are generally allowed and useful.

- Empty statements with `;`
- `import`
- `namespace`
- `export`
- `var` and `const`
- `func` and `async func`
- `class` and `enum`
- Statements that begin with an identifier, such as `main();` or `x = 1;`

Direct control flow statements such as `if`, `for`, `foreach`, `while`, `match`, `try`, `throw`, `break`, `continue`, and `delete` are not allowed at top level.

One important special case is `await`. A bare top level statement like `await work();` is not allowed. An embedded await inside an expression is allowed. That is why this pattern appears often.

```cfs
var _ = await main();
```

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
- Logic. `!`, `&&`, `||`
- Null coalescing. `??`
- Ternary. `condition ? then : else`
- Bitwise. `&`, `|`, `^`, `<<`, `>>`
- Increment and decrement. `++x`, `x++`, `--x`, `x--`
- Compound assignments. `+=`, `-=`, `*=`, `/=`, `%=`

Power is right associative. `2 ** 3 ** 2` is evaluated as `2 ** (3 ** 2)`.

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

Access works through brackets and often also through dot access.

```cfs
print(cfg["host"]);
print(cfg.host);
```

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
delete cfg.host;
delete cfg;
```

The meaning is straightforward.

- `delete arr[1];` removes one element.
- `delete arr[1~3];` removes a range.
- `delete arr;` clears the entire array.
- `delete cfg["k"];` or `delete cfg.k;` removes one key.
- `delete cfg;` clears the entire dictionary.

Delete on strings is not allowed.

## Dictionary Hardening for Reserved Intrinsic Names

The runtime protects some reserved dictionary intrinsic names. One example is `get_or`. Those names should not be used as normal application data keys when they are written through literals or push style merges. This avoids collisions between data and methods.

The practical rule is simple. Use regular business keys such as `mode`, `retries`, `host`, `status`, or `value`.

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
delete cfg.port;

var text = "abcdef";
print(text[1~4]);
```

If you want to combine these data structures with conditions and loops, continue with [Control Flow and Errors](03_control_flow_and_errors.md).
