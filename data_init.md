## Safe Datatype initialisation

CFGS provides several built-in functions to safely construct and convert values into common data types.
All of these built-ins:

* Take **exactly one argument**.
* If the argument is `null`, they return **`null`** (no exception).

They are primarily intended for:

* Safely turning dynamic values (e.g. user input, script data) into typed values.
* Allocating collections without accidentally sharing references.
* Converting large or imprecise numbers into a stable integer representation.

---

## Primitive

### `string(x)`

```cfgs
string(x) -> string | null
```

```cfgs
string(123)      # "123"
string(true)     # "True"
string(null)     # null
```

---

### `int(x)`

```cfgs
int(x) -> int | null
```

```cfgs
int("42")    # 42
int(3.9)     # 3 
int(null)    # null
```

---

### `long(x)`

```cfgs
long(x) -> long | null
```

Same behavior as `int(x)`, but converts to a 64-bit integer.

```cfgs
long(42)       # 42 (as 64-bit integer)
long("9000")   # 9000
```

---

### `double(x)`

```cfgs
double(x) -> double | null
```

```cfgs
double("1.5")  # 1.5
double(3)      # 3.0
```

---

### `float(x)`

```cfgs
float(x) -> float | null
```

Same as `double(x)`, but returns a 32-bit floating-point value.

```cfgs
float("1.5")   # 1.5f
```

---

### `decimal(x)`

```cfgs
decimal(x) -> decimal | null
```
Suitable for money/precision-sensitive values.

```cfgs
decimal("1.234")   # 1.234m
decimal(1.1)       # converted using invariant culture
```

---

### `char(x)`

```cfgs
char(x) -> char | null
```

* For strings: usually expects a string of length 1.
* For numbers: converts numeric value to a Unicode character.

```cfgs
char("A")    # 'A'
char(65)     # 'A'
```

---

### `bool(x)`

```cfgs
bool(x) -> bool | null
```

* Numbers: `0` → `false`, anything else → `true`.
* Strings: `"True"` / `"False"` (case-insensitive). Other strings cause a runtime error.

```cfgs
bool(1)        # true
bool(0)        # false
bool("true")   # true
```

---

### `bigint(x)`

```cfgs
bigint(x) -> bigint | null
```

**Goal:** Safely create a large integer (`BigInteger`) from various inputs.
> Unlike `int`, `long`, etc., `bigint(x)` is intentionally **forgiving**:
> non-parsable strings or unsupported types do **not** throw – they yield `0`.

```cfgs
bigint(42)                     # 42n
bigint("12345678901234567890") # large integer
bigint("abc")                  # 0
bigint(3.9)                    # 3n
```

---

## Collection

These built-ins help you safely create and copy collection types.

### `array(x)`

```cfgs
array(x) -> array | null
```

* A **new copy** of the list is created (shallow copy).
* Otherwise:
  * Returns a **new array containing `x` as a single element**.
 
This is useful both to “wrap” a single value into an array, and to clone an existing array so that later modifications do not affect the original.

```cfgs
array(5)        # [5]
array("hi")     # ["hi"]

var a = [1, 2, 3];
var b = array(a);   # copy of a, not the same list reference
```

---

### `dictionary(x)`

```cfgs
dictionary(x) -> dictionary | null
```

  * A **new copy** of the dictionary is created (shallow copy).
* For any other type:
  * Returns a **new empty dictionary**.

This makes it safe to do `dictionary(value)` in dynamic code without worrying about crashes: if the value isn't a dictionary, you simply get an empty one.

```cfgs
dictionary(null)      # null

var d  = { "a": 1 };
var d2 = dictionary(d)  # copy of d

dictionary(5)         # {}
dictionary("test")    # {}
```

---

[Back](README.md)
