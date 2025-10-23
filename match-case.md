## Match / Case

```cfs
var level = 3;
match(level) {
    case 1: { print("Beginner"); }
    case 2: { print("Intermediate"); }
    case 3: { print("Expert"); }
}
```

```cfs
func toText(n) {
  return n match { 0:"zero", 1:"one", _:"many" };
}
print(toText(0));
print(toText(7));
```

---
---
[← Back to README](./README.md)
