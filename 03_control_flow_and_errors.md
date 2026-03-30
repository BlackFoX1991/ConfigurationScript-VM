# Control Flow and Errors

## Core Idea

CFGS keeps declaration and execution flow fairly separate. Anything that looks like actual program flow belongs inside functions or methods.

## `if` and `else`

```cfs
if (x > 10) {
    print("large");
} else {
    print("small");
}
```

You write `else if` as a chained `else` containing another `if`.

```cfs
if (score > 90) {
    print("A");
} else if (score > 75) {
    print("B");
} else {
    print("C");
}
```

## `while`

```cfs
var i = 0;
while (i < 3) {
    print(i);
    i = i + 1;
}
```

## `do while`

This form always executes the body at least once.

```cfs
var count = 0;
do {
    count = count + 1;
} while (count < 2);
```

## `for`

The `for` loop in CFGS is slightly unusual if you come from C like languages. The initializer and increment sections are both parsed as statements. Because of that, the increment section also ends with a semicolon inside the parentheses.

```cfs
for (var i = 0; i < 5; i = i + 1;) {
    print(i);
}
```

It looks unusual at first, but it is correct and important. Without that final semicolon before `)` the loop is not syntactically complete.

These forms are also valid.

```cfs
for (; i < 10; i++;) {
    print(i);
}

for (var i = 0; ; i++;) {
    if (i == 3) {
        break;
    }
}
```

## `foreach`

### Simple iteration

```cfs
foreach (var v in [10, 20, 30]) {
    print(v);
}
```

### Array index and value

```cfs
foreach (var idx, val in [10, 20, 30]) {
    print(idx);
    print(val);
}
```

### Dictionary key and value

```cfs
foreach (var key, value in {"a": 1, "b": 2}) {
    print(key);
    print(value);
}
```

### Destructuring inside `foreach`

```cfs
foreach (var [x, y] in [[1, 2], [3, 4]]) {
    print(x + y);
}

var a = 0;
var b = 0;
foreach ([a, b] in [[5, 6], [7, 8]]) {
    print(a * b);
}
```

## `break` and `continue`

Both statements work as expected and are only valid inside loops.

```cfs
foreach (var n in [1, 2, 3, 4, 5]) {
    if (n % 2 == 0) {
        continue;
    }
    if (n > 4) {
        break;
    }
    print(n);
}
```

## `try`, `catch`, `finally`

CFGS supports the full trio.

```cfs
try {
    risky();
} catch(e) {
    print(e.message());
} finally {
    print("cleanup");
}
```

A `try` block must have at least `catch` or `finally`. An empty `try` without handlers is invalid.

### The Catch Object

Inside `catch`, the error object is a runtime exception object with intrinsics such as `message()`, `type()`, `file()`, `line()`, `col()`, and `stack()`. The complete list is documented in [Standard Library](09_standard_library.md).

## `using`

`using` is the structured cleanup form for values that should be explicitly destroyed at the end of a scope. It compiles to a `try`/`finally` pattern internally and calls the runtime destruction path even when the body exits via `return` or `throw`.

```cfs
using (var file = open_resource()) {
    print(file.name);
}
```

You can also use a `const` binding.

```cfs
using (const conn = connect()) {
    print(conn.state());
}
```

If you do not need a local variable, the anonymous form is valid too.

```cfs
using (open_resource()) {
    work();
}
```

Single statement bodies are supported in the same way as `if` or `while`.

```cfs
using (var r = open_resource())
    print(r.name);
```

Inside a normal `{ ... }` block you can also use the short declaration form. It keeps the resource alive until the end of the current block.

```cfs
func work() {
    using var file = open_resource();
    print(file.name);
    print("still inside the same using scope");
}
```

Multiple short declarations dispose in reverse order because they nest over the remainder of the block.

```cfs
{
    using var a = first();
    using var b = second();
    run();
}
```

If you need `using` directly under `if`, `while`, or similar without braces, use the parenthesized form or add `{ ... }`.

## `defer`

`defer` schedules cleanup code for the end of the current explicit `{ ... }` block. Internally it behaves like a nested `try`/`finally`, so it still runs when the block exits via `return` or `throw`.

```cfs
func work() {
    defer close_handle();
    run_step();
    run_step();
}
```

You can also defer a whole block.

```cfs
{
    defer {
        print("cleanup");
        flush();
    }

    run();
}
```

Multiple `defer` statements run in reverse order.

```cfs
{
    defer print("outer");
    defer print("inner");
    print("body");
}
```

Like `finally`, deferred cleanup also runs when control leaves the block via `break`, `continue`, `return`, or `throw`.

`defer` is only valid inside an explicit `{ ... }` block. If you need it under `if`, `while`, or similar, add braces first.

## `throw`

You can throw almost any value. In practice strings or structured dictionaries are often the easiest form.

```cfs
throw "something went wrong";
```

Or more structured.

```cfs
throw {"kind": "ConfigError", "message": "host is missing"};
```

## Errors in Async Code

When an `await` hits a failed task, CFGS throws an exception object with types such as `AwaitError` or `AwaitCanceled`. This is especially relevant for plugin calls.

```cfs
try {
    var _ = await some_task();
} catch(e) {
    print(e.type());
    print(e.message());
}
```

## What Does Not Work at Top Level

The following forms are not allowed at top level.

- `if (...) { ... }`
- `while (...) { ... }`
- `for (...) { ... }`
- `foreach (...) { ... }`
- `match (...) { ... }`
- `try { ... }`
- `using (...) { ... }`
- `throw ...;`
- `delete ...;`
- `break;`
- `continue;`

The standard solution is always the same. Put the logic into `main()` and call it at the end.

## Combined Example

```cfs
func main() {
    var total = 0;

    try {
        for (var i = 1; i <= 5; i = i + 1;) {
            if (i == 4) {
                continue;
            }
            total = total + i;
        }
    } catch(e) {
        print("Error: " + e.message());
    } finally {
        print("Total = " + str(total));
    }
}

main();
```

The next two language areas you usually need after control flow are function calls and pattern matching. These chapters cover them.

- [Functions and Calls](04_functions_and_calls.md)
- [Match, Destructuring, and Out](05_match_destructuring_and_out.md)
