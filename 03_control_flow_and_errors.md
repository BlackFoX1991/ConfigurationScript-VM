# Control Flow and Errors

## Core Idea

Control flow runs anywhere code runs: inside functions and methods, and directly at the top level of the entry script. Only imported modules and namespace bodies keep declaration and execution separate, so importing a file never runs program flow.

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

The `for` loop uses the familiar three-part shape.

```cfs
for (var i = 0; i < 5; i = i + 1) {
    print(i);
}
```

The initializer still ends with the first semicolon, and the condition still ends with the second semicolon. The increment section does not need a trailing semicolon before `)`.

These forms are also valid.

```cfs
for (; i < 10; i++) {
    print(i);
}

for (var i = 0; ; i++) {
    if (i == 3) {
        break;
    }
}
```

The initializer may declare several variables. Each of them gets a fresh binding per iteration, like a single loop variable.

```cfs
for (var i = 0, j = 10; i < j; i++) {
    j = j - 1;
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

With a single binding, dictionary iteration yields a `[key, value]` pair array.

```cfs
foreach (var entry in {"a": 1, "b": 2}) {
    print(entry[0]);
    print(entry[1]);
}
```

### String iteration

A string iterates character by character, and the two binding form gives index and
character.

```cfs
foreach (var c in "abc") {
    print(c);
}

foreach (var idx, c in "abc") {
    print(str(idx) + ":" + str(c));
}
```

### Loop variables and closures

Every loop gives its loop variable a fresh binding per iteration, so a function created
inside the body keeps that iteration's value. This is the same for `for` and `foreach`.

```cfs
var fns = [];
for (var i = 0; i < 3; i = i + 1) {
    fns[] = func() { return i; };
}
print(fns[0]() + "," + fns[1]() + "," + fns[2]());   # 0,1,2
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

Both statements work as expected and are only valid inside loops. A loop does not reach into a function declared inside it: `break` and `continue` in a closure, a local function, or a method of a local class only refer to loops within that function body.

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

What `catch` receives depends on how the error was raised.

- A runtime error, such as a division by zero, an undefined variable, or a failed await, arrives as an exception object with intrinsics such as `message()`, `type()`, `file()`, `line()`, `col()`, and `stack()`. The complete list is documented in [Standard Library](09_standard_library.md).
- A value raised with `throw` arrives unchanged. `throw "boom"` gives the string `"boom"`, and `throw {"kind": "X"}` gives that dictionary, so its data is read with `e["kind"]`.
- `throw null` is the one exception: it arrives as an exception object.

```cfs
try {
    var x = 1 / 0;
} catch(e) {
    print(e.type());      # RuntimeError
    print(e.message());   # division by zero
}

try {
    throw {"kind": "ConfigError", "message": "host is missing"};
} catch(e) {
    print(e["kind"]);     # ConfigError
}
```

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

## Control Flow at Top Level

The top level of a directly executed script is fully imperative: `if`, `while`, `for`, `foreach`, `do`, `match`, `try`/`catch`/`finally`, `using`, `throw`, `delete`, and expression statements all run directly, exactly as inside a function body.

```cfs
var mode = "fast";
if (mode == "fast") {
    print("skipping warmup");
} else {
    warmup();
}
```

Context-bound forms keep their usual rules everywhere, including at top level:

- `return` and `yield` need an enclosing function.
- `break` and `continue` need an enclosing loop in the same function body (a top level loop counts for top level code).
- `defer` needs an enclosing `{ ... }` block.

Imported modules and namespace bodies are declaration-oriented and reject imperative statements; see [Modules, Imports and Exports](07_modules_imports_and_exports.md). If logic must run when a module is used, export a function and call it from the entry script.

## Combined Example

```cfs
func main() {
    var total = 0;

    try {
        for (var i = 1; i <= 5; i = i + 1) {
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
- [Match and Destructuring](05_match_and_destructuring.md)
