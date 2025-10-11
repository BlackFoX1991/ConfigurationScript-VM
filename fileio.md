# File I/O

Open files with the builtin `fopen(path, mode)` and then call **file intrinsics**:

- `fh.write(text)`  
- `fh.writeln(text)`  
- `fh.flush()`  
- `fh.read(nBytes)`  
- `fh.readline()`  
- `fh.seek(offset, origin)` → origin: `0=Begin, 1=Current, 2=End`  
- `fh.tell()`  
- `fh.eof()`  
- `fh.close()`

Example:

```cfs
var f = fopen("out.txt", "w");
f.writeln("Hello");
f.flush();
f.close();

var r = fopen("out.txt", "r");
print(r.readline()); # Hello
r.close();
```

---

[Back](README.md)
