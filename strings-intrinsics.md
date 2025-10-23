## Strings (Intrinsics)

- `s.substr(start, length)` → substring by count
- `s.contains(value)` → returns true or false depending on existing substring 
- `s.slice(start?, end?)` → half-open range (like array slice)  
- `s.replace_range(start, end, text)` → returns new string  
- `s.remove_range(start, end)` → returns new string with range removed  
- `s.insert_at(index, text)` → returns new string with text inserted  
- `s.len()` → string length

Example:

```cfs
var s = "hello world";
print(s.substr(0, 5));              # "hello"
print(s.slice(6));                  # "world"
print(s.insert_at(5, ","));         # "hello, world"
print(s.replace_range(6, 11, "CFG"))# "hello CFG"
print(s.len());                     # 11
```

---
---
[← Back to README](./README.md)
