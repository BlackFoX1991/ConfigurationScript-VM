## Closures / Functions as Values

```cfs
var mul = func(a, b) { return a * b; };
print(mul(5, 6)); # 30

func makeAdder(x) {
    return func(y) { return x + y; };
}
var addFive = makeAdder(5);
print(addFive(10)); # 15
```

---
---
[← Back to README](./README.md)
