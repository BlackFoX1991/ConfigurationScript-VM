## Ternary Operator

Use the ternary operator to select between two values inline.

**Syntax:** `condition ? valueIfTrue : valueIfFalse`

**Examples**

```cfs
var health = 72;
var status = (health >= 90) ? "Great" : "Needs attention";
print(status);  # "Needs attention"

# Nesting (use sparingly; consider readability):
var grade = (score >= 90) ? "A" : (score >= 75) ? "B" : "C";

# Ternary as an expression within larger statements:
print( (isAdmin) ? "Welcome, admin." : "Welcome." );
```

> The condition is evaluated once; only the chosen branch is evaluated.

---
---
[← Back to README](./README.md)
