# Functions and Calls

## Function Declarations

Regular functions use `func`.

```cfs
func add(a, b) {
    return a + b;
}
```

Returns are done through `return`. Without an explicit return, the effective result at the end is `null`.

## Async Functions

Async functions use `async func`.

```cfs
async func fetch_value() {
    yield;
    return 42;
}
```

Calling an async function returns a task object. More on that is covered in [Async, Await, and Yield](08_async_await_and_yield.md).

## Function Expressions

Functions are values in CFGS. You can create them anonymously, store them, and pass them around.

```cfs
var twice = func(v) {
    return v * 2;
};

print(twice(5));
```

The same idea also works in async form.

```cfs
var work = async func(v) {
    yield;
    return v + 1;
};
```

## Closures

Function values can capture outer variables.

```cfs
func make_multiplier(factor) {
    return func(v) {
        return v * factor;
    };
}

var mul3 = make_multiplier(3);
print(mul3(7));
```

Captures are live. If the captured variable changes later, the closure observes the later value.

```cfs
var captured = 2;
var add_captured = func(v) { return v + captured; };

print(add_captured(5));
captured = 9;
print(add_captured(5));
```

## Default Parameters

Parameters may have default values.

```cfs
func open_port(host, port = 80) {
    return host + ":" + str(port);
}
```

An important detail is that default values may refer to earlier parameters.

```cfs
func with_dep(a, b = a * 2, c = b + 1) {
    return a + b + c;
}
```

A non default parameter is not allowed after a default parameter.

## Named Arguments

Calls may use named arguments.

```cfs
func sum3(a, b = 10, c = 100) {
    return a + b + c;
}

sum3(1, c: 5)
sum3(1, c: 5, b: 2)
sum3(a: 1, b: 2, c: 3)
```

Important rules for named arguments are these.

- A named argument may only appear once.
- After the first named argument, no positional argument may follow.
- Unknown parameter names cause an error.
- Rest parameters cannot be passed as named arguments.

## Rest Parameters

With `*name` you collect extra positional arguments into an array.

```cfs
func collect(a, b = 2, *rest) {
    return a + b + len(rest);
}
```

Rest parameters follow these rules.

- Only one rest parameter is allowed.
- It must be the last parameter.
- It cannot have a default value.

## Spread Arguments

An array can be expanded again at call site.

```cfs
func add3(a, b, c) {
    return a + b + c;
}

var values = [2, 3, 4];
print(add3(*values));
```

Spread also works together with named arguments as long as the ordering stays valid.

```cfs
collect(*[1], b: 7)
```

After named arguments, nothing positional may follow. A later spread still counts as positional and is therefore invalid in that position.

## Methods Follow the Same Call Rules

Methods can also use default parameters, rest parameters, and named arguments.

```cfs
class CallBox(seed) {
    var seed = 0;

    func init(seed) {
        this.seed = seed;
    }

    func mix(a, b = 5, *rest) {
        return this.seed + a + b + len(rest);
    }
}

var box = new CallBox(10);
print(box.mix(a: 2, b: 6));
```

## Destructuring Parameters

Parameters can be destructured directly. This is one of the stronger CFGS features.

```cfs
func sum_pair([a, b]) {
    return a + b;
}

func read_point({x, y: yy}) {
    return x * 10 + yy;
}
```

Defaults and follow up dependencies are also supported here.

```cfs
func order_from_destructure([sx] = [7], sy = sx) {
    return sy;
}
```

## Return and Scope

`return` is only allowed inside functions. Inside `out` blocks, `return` is explicitly forbidden because `out` is already an expression form of its own.

## Typical Entry Patterns

### Traditional `main`

```cfs
func main() {
    print("start");
}

main();
```

### Async `main`

```cfs
async func main() {
    yield;
    return 1;
}

var _ = await main();
```

### Functions as Pipeline Blocks

```cfs
func apply_twice(f, v) {
    return f(f(v));
}

var inc = func(x) { return x + 1; };
print(apply_twice(inc, 5));
```

If you want to combine functions with pattern matching and destructuring, continue with [Match, Destructuring, and Out](05_match_destructuring_and_out.md).
