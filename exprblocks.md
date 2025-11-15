## Blocks as Expression

Sometimes you want to run several statements, then use the final result **as a value** inside an expression.
For this, CFGS provides the `out { ... }` expression.

An `out` block:

* Executes a sequence of statements.
* Evaluates to the value of the **last statement** (if it produces a value).
* Returns `null` if the last statement does not produce a value.
* Can be used **anywhere** a normal expression is allowed.

---

### Syntax

```cfgs
out {
    // one or more statements...
    // last statement decides the result
}
```

Examples:

```cfgs
print(out { var x = 5; x; });
// prints: 5
```

Here:

* `var x = 5;` is a normal statement.
* `x;` is the **last statement** and is an expression statement.
* The value of `x` (5) becomes the value of the whole `out { ... }` block.
* `print` receives `5`.

---

### Using `out` as a value

Because `out { ... }` is an expression, you can use it anywhere expressions are allowed:

* In variable initializers
* As function arguments
* In larger arithmetic or logical expressions
* Inside other expressions

Example: computing a complex value and storing it in a variable:

```cfgs
var complex = out {
    var endwert = bigint(3);

    for (var j = 1; j < 200; j++;)
    {
        endwert *= j;
    }

    endwert;
};

print(complex);
```

Here:

* The `out { ... }` block runs the loop and updates `endwert`.
* The last statement `endwert;` returns the current value of `endwert`.
* `complex` receives that final value.
* `print(complex);` prints the result.

---

### Result rules

Inside an `out` block, **only the last statement matters** for the resulting value:

* If the last statement is an **expression statement** (e.g. `x;`, `x + 1;`, `someFunc();` that returns a value),
  → the block evaluates to that value.
* If the last statement is a statement that does **not** produce a value (e.g. `var`, `while`, `for`, `if` without a final expression, etc.),
  → the block evaluates to `null`.

So conceptually:

```cfgs
var value = out {
    // many statements...

    finalExpression;   # result of this expression is returned
};
```

If you don’t explicitly put a final expression, think of the block as returning `null`.

---

### Typical use cases

* **Inline multi-step computation** without introducing extra helper functions.
* **Local setup** (temporary variables) that should not leak outside the block, but whose final result you still need.
* **Complex argument building** for a function call:

```cfgs
print(out {
    var a = 10;
    var b = 20;
    a * b;
});
```

---

In short:
`out { ... }` lets you treat a block of statements like a single expression, where the value of the **last statement** becomes the value of the whole block.


[Back](README.md)
