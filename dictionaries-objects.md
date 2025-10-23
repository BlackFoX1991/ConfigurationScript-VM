## Dictionaries (Objects)

Dictionaries map string keys to values.

### Intrinsics (methods)

- `d.len()` → number of keys  
- `d.contains(key)` → boolean existence  
- `d.remove(key)` → remove by key, returns true/false  
- `d.keys()` → array of keys  
- `d.values()` → array of values  
- `d.set(key, value)` → set/replace value (also works with `d["k"] = v`)  
- `d.get_or(key, default)` → get value or the provided default

### Examples

```cfs
var settings = {"volume": 70, "theme": "dark"};

print(settings.contains("volume"));  # true
print(settings.len());          # 2

settings.set("theme", "light");
settings["lang"] = "EN";        # equivalent to set
print(settings.keys());         # e.g. ["volume","theme","lang"]

print(settings.get_or("fpsLimit", 60)); # 60
settings.remove("volume");
```

You can also **append entries** with the push operator using a single-entry dict:

```cfs
settings[] = {"newKey": 123};
```

---
---
[← Back to README](./README.md)
