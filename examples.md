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

Klar — hier ist eine sauber integrierte, allgemeinere README-Beschreibung für `import`, jetzt inkl. deiner **`import ClassName from ...`**-Variante. Ich halte es konsistent mit dem, was du schon definiert hast (Basis-Pfad = aktuell ausgeführtes Skript).

---

## Import

The `import` statement loads external code into the current script and helps structure projects into reusable, maintainable modules.

It supports importing:

* **CFGS script files** (`.cfs`)
* **.NET libraries** (`.dll`)

### Supported forms

#### 1) Import a full script or library

Use this form to load a module or library by path:

```cfgs
import "./myfile.cfs";
```

```cfgs
import "./libs/MyLibrary.dll";
```

#### 2) Import specific classes from a script

When importing CFGS scripts, you can import individual classes directly:

```cfgs
import ClassName from "path/to/file.cfs";
```

You can also use relative paths:

```cfgs
import MyService from "./services/MyService.cfs";
```

---

### Path handling

The `import` statement supports **absolute and relative paths**.

Relative paths (`./`, `../`) are resolved against the **base path of the script that is currently being executed**.
This base path acts as the effective working path for import resolution at that moment.

---

### Import resolution

1. The import system first tries to resolve the file at the **specified location**.
2. If it is not found, it falls back to the **CFGS path**.

`Note, since 06.12.2025 : If an imported .cfs or .dll file is not found at the specified path, the import statement will fall back to searching the CFGS path.`

---

### Examples

Import a script in the same directory:

```cfgs
import "./myfile.cfs";
```

Import from a subfolder:

```cfgs
import "./modules/myfile.cfs";
```

Import from a parent folder:

```cfgs
import "../shared/common.cfs";
```

Import a DLL via a relative path:

```cfgs
import "./libs/MyLibrary.dll";
```

Import a specific class from a script:

```cfgs
import User from "./models/User.cfs";
```

---


[Samples](CFGS_NE/Samples/) · [Standard Builtins/ Intrinsics](fileio.md)  · [HTTP Functions](httpc.md)  · [Creating Plugins](plugins.md)
---
[← Back to README](./README.md)
