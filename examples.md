## Examples

### Array & String methods together

```cfs
var a = [ "cfgs", "vm", "rocks" ];
print(a.len());             # 3
a.push("!");
print(a.slice(0, 3));       # ["cfgs","vm","rocks"]

var s = "abcde";
print(s.slice(1, 4));       # "bcd"
print(s.insert_at(3, "_")); # "abc_de"
```

### Dictionary workflow

```cfs
var d = {"a": 1};
d.set("b", 2);
d[] = {"c": 3};             # append entry
print(d.get_or("x", 0));    # 0
print(d.keys());            # ["a","b","c"] (order by creation)
```

---

**Imports**

Use `import` to include external scripts or individual classes.

```cfs
import "path/to/file.ext";
import ClassName from "path/to/file.ext";
```

> Import statements must appear **at the top** of the script.

---

[Samples](CFGS_NE/Samples/) · [Standard Builtins/ Intrinsics](fileio.md)  · [HTTP Functions](httpc.md)  · [Creating Plugins](plugins.md)
---
[← Back to README](./README.md)
