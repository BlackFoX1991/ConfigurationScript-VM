# Built-in Functions


## Quick Reference

| Name       | Params                       | Returns                | Notes |
|------------|------------------------------|------------------------|-------|
| `typeof`   | `(value)`                    | `String` type name     | e.g. `"Int"`, `"String"`, `"Array"`, `"Dictionary"`, `"Function"`, `"Closure"`, class name |
| `getfields`| `(dict)`                     | `Array` of keys        | Returns empty dictionary if not a dictionary or null |
| `isarray`  | `(value)`                    | `Bool`                 |       |
| `isdict`   | `(value)`                    | `Bool`                 |       |
| `len`      | `(array/string/dict)`        | `Int`                  | `-1` for unsupported types |
| `isdigit`  | `(char)`                     | `Bool`                 | Non-null required |
| `isletter` | `(char)`                     | `Bool`                 | Non-null required |
| `isspace`  | `(char)`                     | `Bool`                 | Non-null required |
| `isalnum`  | `(char)`                     | `Bool`                 | Non-null required |
| `str`      | `(value)`                    | `String`               | Uses `ToString()` |
| `toi`      | `(value)`                    | `Int`/number           | Generic number conversion |
| `toi16`    | `(value)`                    | `Int16`                |      |
| `toi32`    | `(value)`                    | `Int32`                |      |
| `toi64`    | `(value)`                    | `Int64`                |      |
| `abs`      | `(number)`                   | `number`               | Works with numeric types |
| `rand`     | `(seed, min, max)`           | `Int`                  | Random non-negative integer |
| `print`    | `(value)`                    | `Int` (`1`)            | Prints with newline |
| `put`      | `(value)`                    | `Int` (`1`)            | Prints without newline |
| `clear`    | `()`                         | `Int` (`1`)            | Clears console |
| `getl`     | `()`                         | `String`               | Reads a line (no newline) |
| `getc`     | `()`                         | `Int`                  | Reads next char code |

---

## Examples

### typeof
```cfs
print(typeof(1));           # Int
print(typeof(1.0));         # Double (or Float/Decimal depending on literal rules)
print(typeof("hi"));        # String
var a = []; print(typeof(a));    # Array
var d = { "k": 1 }; print(typeof(d));  # Dictionary
```

### getfields
```cfs
var d = { "a": 1, "b": 2 };
print(getfields(d));  # ["a","b"] (order = insertion order)
print(getfields(123));# Empty dictionary
```

### isarray / isdict
```cfs
var a = [1,2,3];
var d = { "x": 1 };
print(isarray(a));    # true
print(isarray(d));    # false
print(isdict(d));     # true
print(isdict(a));     # false
```

### len
```cfs
print(len("abcd"));           # 4
print(len([1,2,3]));          # 3
print(len({ "k":1, "v":2 })); # 2
print(len(123));              # -1
```

### isdigit / isletter / isspace / isalnum
```cfs
print(isdigit('5'));   # true
print(isletter('A'));  # true
print(isspace(' '));   # true
print(isalnum('7'));   # true
print(isalnum('Z'));   # true
print(isalnum('-'));   # false
```

### str
```cfs
print(str(123));            # "123"
print(str([1,2]));          # "[1,2]" (implementation-dependent formatting)
```

### toi / toi16 / toi32 / toi64
```cfs
print(toi("42"));    # 42
print(toi16(42));    # 42
print(toi32("7"));   # 7
print(toi64(9007199254740991)); # 9007199254740991 (if in range)
```

### abs
```cfs
print(abs(-5));    # 5
```

### rand
```cfs
print(rand(5,300,500));     # e.g., 325
```

### print / put
```cfs
put("Hello ");
put("World");
print("!");
```

### clear
```cfs
clear();
```

### getl / getc
```cfs
print("Enter your name:");
var name = getl();
print("Hi " + name);

print("Press a key:");
var code = getc();
print(code);
```


