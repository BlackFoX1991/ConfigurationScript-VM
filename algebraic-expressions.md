## Algebraic Expressions

CFGS supports a rich set of arithmetic, shift, bitwise, comparison, and logical operators with clear precedence. Use parentheses to make intent explicit and to override the default order.

**Operator categories (from higher to lower precedence):**

1. **Grouping**: `(...)`  
2. **Power**: `**`  
3. **Unary**: `+x`, `-x`  
4. **Multiplicative**: `*`, `/`  
5. **Additive**: `+`, `-`  
6. **Shifts**: `<<`, `>>`  
7. **Bitwise**: `&`, `^`, `|`  
8. **Comparisons**: `==`, `!=`, `<`, `<=`, `>`, `>=`  
9. **Logical (short-circuit)**: `&&`, `||`

> **Note:** `**` is the power operator. `^` is **bitwise XOR** (not power).

**Examples**

```cfs
# Arithmetic
print(1 + 2 * 3);          # 7
print((1 + 2) * 3);        # 9
print((2 ** 3) * 2);       # 16

var a = 10;
var b = 3;
print(a / b);              # 3  (integer division if both sides are integers)

# Shifts & bitwise
print(1 << 3);             # 8
print(13 & 7);             # 5   (1101 & 0111 = 0101)
print(10 | 4);             # 14  (1010 | 0100 = 1110)
print(10 ^ 4);             # 14  (1010 ^ 0100 = 1110)

# Comparisons
print(5 > 3);              # true
print(5 == 5);             # true
print(5 != 6);             # true

# Logical (short-circuit)
var isAdmin = false;
func heavyCheck() { print("called"); return true; }
print(isAdmin && heavyCheck());   # false; heavyCheck() is NOT called
print(isAdmin || true);           # true

# Precedence demo (use parentheses to be explicit)
print(1 + 2 << 2);         # 12  because (1 + 2) << 2 = 3 << 2 = 12
print(1 + (2 << 2));       # 9   parentheses change the result
```

> **Tip:** When mixing shifts, bitwise, and arithmetic, add parentheses—future-you will thank present-you.
---
[← Back to README](./README.md)
