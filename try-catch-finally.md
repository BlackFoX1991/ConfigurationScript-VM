## Try-Catch-Finally

> **Note:** A `try` block **must include at least one** of `catch` **or** `finally`.
> A bare `try { ... }` without `catch` and without `finally` is **invalid**.
> 
---

### Overview

* **`try { ... }`**: Code that may fail.
* **`catch(name?) { ... }`**: Handles a thrown value. The identifier is **optional**.
* **`finally { ... }`**: Cleanup block that **always** executes after `try`/`catch`.
* **`throw expr;`**: Throws any value (e.g., a string).

---

### Syntax

```cfgs
try {
    # risky code
}
catch(err) {
    # err is the thrown payload (e.g., "error text")
}
finally {
    # cleanup that always runs
}
```

**Variants (all valid):**

```cfgs
# Catch without an identifier
try { ... }
catch() { ... }
finally { ... }

# try + finally only (no catch): error is not handled but cleanup still runs
try { ... }
finally { ... }

# try + catch only (no finally)
try { ... }
catch(e) { ... }
```

> **Invalid:**
>
> ```cfgs
> try { ... }   # no catch and no finally → not allowed
> ```

---

### Execution Rules

1. **No error in `try`:**
   Run `finally`, then continue after the construct.
2. **Error in `try`:**

   * With `catch`: run `catch`, **then `finally`**, then continue.
   * Without `catch`: run **`finally`**, then propagate the error outward.
3. **`return` in `try` or `catch`:**
   `finally` runs **before** returning.
4. **`break` / `continue` inside `try`/`catch` in loops:**
   `finally` runs **before** the jump.
5. **Error thrown in `catch`:**
   `finally` runs, then the new error propagates.
6. **Error thrown in `finally`:**
   The error from `finally` **overrides** previous flow.

---

### Examples

#### 1) Basic case with payload

```cfgs
func demo_basic() {
    var log = "";
    try {
        log = log + "T";
        throw "Boom";
    }
    catch(err) {
        log = log + "C:" + err;   # -> "C:Boom"
    }
    finally {
        log = log + "F";
    }
    print(log); # "TC:BoomF"
}
```

#### 2) `catch()` without identifier

```cfgs
func demo_catch_no_ident() {
    var log = "";
    try { throw "X"; }
    catch() { log = log + "C"; }
    finally { log = log + "F"; }
    print(log); # "CF"
}
```

#### 3) `return` in `try` — `finally` still runs

```cfgs
func demo_return_in_try() {
    var log = "";
    try {
        log = log + "T";
        return 42;     # finally runs before returning
    }
    catch(e) { log = log + "C"; }
    finally { log = log + "F"; }

    # Not reached, but "F" was appended.
}
```

#### 4) `break` / `continue` with `finally`

```cfgs
func demo_loop_finally() {
    var i = 0;
    var log = "";
    while (i < 3) {
        try {
            if (i == 1) { i = i + 1; continue; }  # finally runs first
            if (i == 2) { break; }                # finally runs first
            log = log + "W" + i;
        }
        catch(e) { log = log + "C"; }
        finally { log = log + "F"; }
        i = i + 1;
    }
    print(log);  # e.g., "W0FW1FW2F"
}
```

#### 5) Propagation without `catch`

```cfgs
func demo_propagate() {
    try {
        throw "outer";
    }
    finally {
        print("cleanup"); # always runs
    }
    # "outer" propagates if nothing catches it outside.
}
```

#### 6) Rethrow

```cfgs
func demo_rethrow() {
    try {
        throw "X";
    }
    catch(e) {
        # optional handling...
        throw e;  # rethrow the payload
    }
    finally {
        print("always");
    }
}
```

# `catch(ex)` – using the **ExceptionObject** (CFGS)

When you write `catch(ex)`, the variable `ex` is an **ExceptionObject**.

### Members of ExceptionObject

* `ex.message()` – error message
* `ex.type()` – error type/category
* `ex.file()` – source file
* `ex.line()` / `ex.col()` – line / column
* `ex.stack()` – stack trace
* `ex.toString()` – formatted summary

### Examples

```cfgs
try { throw "Boom"; }
catch(ex) {
    print(ex.type() + ": " + ex.message());
    print(ex.file() + ":" + ex.line() + ":" + ex.col());
    print(ex.stack());
}
```
**Notes**

* Use parentheses: `ex.message()` (not `ex.message`).
* `ex` is scoped to the `catch` block.
* `catch()` without a variable does not bind an ExceptionObject.

---

### Best Practices

* Put **cleanup in `finally`**: release resources, reset transient state, flush logs, etc.
* **Catch selectively**: handle only when you can recover; otherwise let it propagate (still use `finally` for cleanup).
* **Rethrow intentionally**: if you only log in `catch`, `throw e;` so the caller can decide.
* For loops, wrap the **body** in `try/finally` when you need per-iteration cleanup guarantees.

---

### Quick Reference

| Situation                           | Order / Result                                        |
| ----------------------------------- | ----------------------------------------------------- |
| Normal flow                         | `try` → `finally` → continue                          |
| Error in `try`, with `catch`        | `try` → `catch` → `finally` → continue                |
| Error in `try`, without `catch`     | `try` → `finally` → **error propagates**              |
| `return` in `try`/`catch`           | `try`/`catch` → `finally` → **return**                |
| `break`/`continue` in `try`/`catch` | `try`/`catch` → `finally` → **jump**                  |
| Error thrown in `catch`             | `catch` throws → `finally` → **new error propagates** |
| Error thrown in `finally`           | `finally` error **overrides** prior flow              |


---
---
[← Back to README](./README.md)
