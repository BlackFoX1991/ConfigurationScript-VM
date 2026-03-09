# Match, Destructuring, and Out

## Match as a Statement

The `match` statement is designed for structured branching by pattern.

```cfs
func describe(v) {
    var result = "none";

    match (v) {
        case [var a, var b] if (a < b): {
            result = "asc:" + str(a + b);
        }
        case [var a, var b]: {
            result = "pair:" + str(a * b);
        }
        default: {
            result = "other";
        }
    }

    return result;
}
```

A `match` statement uses `case` for regular arms and `default` for the fallback.

## Match as an Expression

As an expression, `match` becomes especially compact.

```cfs
func classify(v) {
    return v match {
        [var a, var b] if (a == b): "same",
        {"tag": "num", "value": var n} if (n > 10): "big",
        {"tag": "num", "value": var n}: "small",
        _ if (v == null): "nil",
        _: "other"
    };
}
```

In expression form there is no `default`. Instead, `_` acts as the wildcard arm. An unguarded `_` arm is the true fallback.

## Supported Match Patterns

The language supports these pattern forms.

- Literals such as numbers, strings, chars, `true`, `false`, and `null`
- `_` as a wildcard
- `var name` as a binding
- Array patterns such as `[var a, var b]`
- Dictionary patterns such as `{"kind": "pair", "left": var x, "right": var y}`
- Arbitrary nesting of these forms

## Guards

A pattern can be refined through `if (...)`.

```cfs
case [var a, var b] if (a < b): { ... }
```

A guard is only evaluated if the pattern itself already matched.

## Destructuring in Declarations

### Arrays

```cfs
var arr = [10, [20, 30], 40];
var [a, [b, c], _] = arr;
```

### Dictionaries

```cfs
var obj = {"x": 1, "y": 2, "pair": [7, 8], "inner": {"z": 9}};
const {x, y: yy, pair: [p0, p1], inner: {z}} = obj;
```

The meaning is direct.

- `x` binds the key with the same name.
- `y: yy` reads the key `y` and stores it under the local name `yy`.
- Nested patterns work for arrays and dictionaries.
- `_` ignores a value.

## Destructuring in Assignments

Existing variables can be filled again through a pattern later on.

```cfs
var q = 0;
var r = 0;
[q, r] = [3, 4];

var dx = 0;
var dy = 0;
({x: dx, y: dy} = {"x": 1, "y": 2});
```

The parenthesized form for dictionary assignment is important because it lets the parser recognize the syntax cleanly.

## Destructuring in Parameters

```cfs
func sum_pair([m, n]) {
    return m + n;
}

func read_obj({x, y: y2}) {
    return x * 10 + y2;
}
```

Defaults and dependency chains are also supported.

```cfs
func order_from_destructure([sx] = [7], sy = sx) {
    return sy;
}
```

## Destructuring in `foreach`

```cfs
foreach (var [x, y] in [[1, 2], [3, 4]]) {
    print(x + y);
}

foreach (var {x, y: yy} in [{"x": 1, "y": 7}, {"x": 2, "y": 8}]) {
    print(x + yy);
}
```

## `out` as an Expression Block

`out` is an expression form with its own block. The result is the last evaluated expression inside that block.

```cfs
var result = out {
    var a = 4;
    a;
};
```

If there is no final expression, `out` returns `null`.

```cfs
var result = out {
    var a = 1;
};
```

This is useful for small local computation islands when you do not want to create a full function frame.

## Important `out` Rules

- `return` is not allowed inside an `out` block.
- The block has its own scope.
- The resulting value comes from the final expression, not from `return`.

## Good Use Cases

`match` is strong when you work with structured data. `destructuring` is strong when you unpack values. `out` is strong for compact local computations. Together they make CFGS very comfortable for configuration and transformation scripts.

## Combined Example

```cfs
func normalize(msg) {
    var header = out {
        var raw = msg["headers"];
        raw["Content-Type"] ?? "text/plain";
    };

    return msg match {
        {"kind": "pair", "left": var x, "right": var y} if (x == y): {
            "same:" + str(x + y);
        },
        {"kind": "pair", "left": var x, "right": var y}: {
            "diff:" + str(x - y);
        },
        _: {
            "fallback:" + header;
        }
    };
}
```

The next chapter opens the OOP side of the language. That includes classes, visibility, inheritance, namespaces, and the special receivers `this`, `type`, `super`, and `outer`.
