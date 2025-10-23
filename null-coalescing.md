## Null Coalescing

Return the left operand if it is **not** `null`; otherwise return the right operand.

**Syntax:** `left ?? right`

**Examples**

```cfs
var userName = inputName ?? "Guest";      # fall back to "Guest" if inputName is null
var title = config["title"] ?? "Untitled";

# Chaining
var port = env["PORT"] ?? settings["port"] ?? 8080;

# With function calls
func findUser(id) { /* ... */ }    # returns user or null
var user = findUser(42) ?? defaultUser();
```

> `??` short-circuits: if the left side is non-null, the right side is not evaluated.
---
[← Back to README](./README.md)
