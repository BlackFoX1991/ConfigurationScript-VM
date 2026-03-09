# Async, Await, and Yield

## Async Functions

An async function is declared with `async func`.

```cfs
async func delayed_inc(x) {
    yield;
    return x + 1;
}
```

Calling it returns a task object. In CFGS, `typeof` usually renders that as something like `Task<Object>`.

```cfs
var task = delayed_inc(9);
print(typeof(task));
print(await task);
```

## `await`

`await` unwraps awaitable values. That includes these categories.

- CFGS internal tasks
- .NET `Task`
- .NET `Task<T>`
- .NET `ValueTask`
- .NET `ValueTask<T>`
- collections whose elements are themselves awaitable

### Await on Regular Values

Regular values may also pass through `await`. In that case the value simply comes back unchanged.

```cfs
async func demo() {
    return await 5;
}
```

### Await on Lists

```cfs
async func demo(timer) {
    var a = timer.delay(25, 2);
    var b = timer.delay(5, 4);
    var result = await [a, b, 8];
    return result;
}
```

The list is resolved element by element. Non awaitable values remain unchanged.

### Await on Dictionaries

```cfs
async func demo(timer) {
    return await {"x": timer.delay(5, 10), "y": 3};
}
```

Again, awaitable values are resolved recursively.

## `yield`

`yield` is only allowed inside `async func` and does not accept a value.

```cfs
async func step() {
    yield;
    return 1;
}
```

In practice, `yield` is a cooperative suspension point. The function starts running, but intentionally gives control back to the scheduler.

## Hot Start Behavior of Async Functions

CFGS starts async functions immediately. This matters. A call already executes code before you later `await` it.

```cfs
var trace = "";

async func hot() {
    trace = trace + "S";
    yield;
    trace = trace + "R";
    return 11;
}

var t = hot();
trace = trace + "C";
print(trace);
print(await t);
```

Before the await, `trace` is already `SC`. The start is hot, not fully lazy.

## Where `await` Is Allowed

Inside synchronous functions, `await` is not allowed.

```cfs
func bad() {
    return await 1;
}
```

At top level, a bare `await` statement is also invalid. An embedded await inside an assignment or another identifier led statement is valid.

Clean.

```cfs
var _ = await main();
```

Not clean.

```cfs
await main();
```

## Async Errors

Failed await operations appear as CFGS exceptions. Common types are `AwaitError` and `AwaitCanceled`.

```cfs
try {
    var _ = await something();
} catch(e) {
    print(e.type());
    print(e.message());
}
```

## Task Namespace from the Standard Library

The standard library provides a small async helper namespace through `task()`.

```cfs
var t = task();
var value = await t.delay(50, 123);
```

More details are documented in [Standard Library](09_standard_library.md).

## Async I O and Plugins

Many builtins from the standard library and the plugins return tasks directly. This includes these examples.

- `sleep`
- `nextTick`
- `getlAsync`
- `getcAsync`
- `readTextAsync`
- `writeTextAsync`
- `appendTextAsync`
- `http_get`
- `http_post`
- `http_download`
- `sql_connect`
- all SQL `_async` intrinsics

## Typical `main` Pattern

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";

async func main() {
    var t = task();
    return await t.delay(10, "done");
}

var result = await main();
print(result);
```

## When to Use `yield` Instead of `await`

Use `await` when you actually need to wait for an awaitable value. Use `yield` when you want to insert a scheduling point inside an async function even without an external task.

## Where to Continue

The async language layer is tightly connected to the runtime. That makes [Standard Library](09_standard_library.md) a good next step, especially the parts covering `task`, `sleep`, and the async file builtins.
