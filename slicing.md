## Slicing

Slicing works for **arrays**, **dictionaries** (by current key order), and **strings**.  
Syntax: `target[start~end]` (end is exclusive). Negative indices are supported.

### Array Slicing

```cfs
var a = [1,2,3,4,5];
print(a[0~2]);  # [1,2]
print(a[1~4]);  # [2,3,4]
print(a[~3]);   # from start, 3 items -> [1,2,3]
print(a[2~]);   # from index 2 to end -> [3,4,5]
```

### Dictionary Slicing

```cfs
var d = {"a":1, "b":2, "c":3, "d":4};
print(d[0~2].len());   # 2
print(d["b"]);         # 2
```

### String Slicing (immutable)

```cfs
var s = "abcdef";
print(s[0~2]); # "ab"
print(s[2~5]); # "cde"
print(s[~3]);  # "abc"
print(s[3~]);  # "def"
```

**Deleting a slice**

For arrays and dictionaries, `delete target[i~j];` removes that slice.  
For strings, build a new one:

```cfs
var s = "abcdef";
s = s[~1] + s[3~];  # drop index 1..2 -> "adef"
```

---
---
[← Back to README](./README.md)
