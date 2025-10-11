<center><img src="CFGS_NE/assets/logo_cfgs.png" width="256" alt="Configuration Language Logo"></center>

# ConfigurationScript (CFGS) – Bytecode VM Edition

CFGS has been **completely redesigned**.  
The old prototype (a direct AST interpreter) has been replaced by a **stack-based bytecode Virtual Machine** with its own compiler and runtime.  
The **syntax was refined** so the **lexer, parser, compiler, and VM** work together cleanly.  
This project also served to **deepen my compiler engineering knowledge**.

> **Note**
> - The old project is archived for historical reasons but is no longer developed.
> - CFGS is **work in progress** and may still contain bugs.

---

## 🚀 Getting Started

You can run code in two ways:

1. **Interactive REPL**  
   Starts automatically if you run without arguments.  
   The REPL detects open blocks and only executes when all blocks are closed.

2. ## CFS Command-Line Guide

> Quick reference for compiling and running CFS scripts from the terminal.

---

### 🚀 Usage

```bash
cfcs [-d | -c | -b] <script-path>
```

- `<script-path>` can be a `.cfs` source file or a compiled `.cfb` bytecode file.

---

### 🧭 Options

| Flag | Description |
|-----:|-------------|
| `-d` | Enable **debug mode**. |
| `-c` | Compile a `.cfs` script to a `.cfb` bytecode file. The output is written next to the input script, using the same filename with a `.cfb` extension. |
| `-b` | Run a compiled `.cfb` file. |

> **Tip:** You don't need `-b` when passing a `.cfb` file—CFS detects the extension automatically and runs it as bytecode.

---

### 📌 Examples

Compile a script:
```bash
cfcs -c ./scripts/example.cfs
# → creates ./scripts/example.cfb
```

Run a compiled bytecode file:
```bash
cfcs ./scripts/example.cfb
# or explicitly:
cfcs -b ./scripts/example.cfb
```

Debug while running a source script:
```bash
cfcs -d ./scripts/example.cfs
```

---

### ❓ Notes

- If you provide a `.cfb` file, CFS treats it as **bytecode** automatically.  
- If you provide a `.cfs` file with `-c`, the compiler writes the `.cfb` next to the source.  
- You can combine `-d` with `-b` to debug a compiled program.

---

## Table of Contents

1. [Variables](#variables)  
2. [Arrays](#arrays)  
3. [Dictionaries (Objects)](#dictionaries-objects)  
4. [Slicing](#slicing)  
5. [Strings (Intrinsics)](#strings-intrinsics)  
6. [Functions](#functions)  
7. [While Loops](#while-loops)  
8. [For Loops](#for-loops)  
9. [If / Else](#if--else)  
10. [Match / Case](#match--case)  
11. [Break & Continue](#break--continue)  
12. [Closures / Functions as Values](#closures--functions-as-values)  
13. [Classes (Instances, Statics, Inheritance)](#classes-instances-statics-inheritance)  
14. [Enums](#enums)  
15. [File I/O](#file-io)  
16. [Built-ins (selected)](#built-ins-selected)  
17. [Examples](#examples)

---

## Variables

```cfs
var score = 100;
print(score); # 100

var playerName = "Alex";
print(playerName); # Alex
```

---

## Arrays

Arrays are ordered, dynamic lists. You can use both **operators** and **intrinsics**.

### Intrinsics (methods)

- `arr.len()` → number of items  
- `arr.push(value)` → appends; returns new length  
- `arr.pop()` → removes & returns last element or `null` if empty  
- `arr.insert_at(index, value)` → inserts at index  
- `arr.remove_range(start, end)` → remove half-open range `[start, end)`  
- `arr.replace_range(start, end, valueOrArray)` → replace range with a value or list  
- `arr.slice(start?, end?)` → returns a new subarray (half-open)

### Examples

```cfs
var nums = [10, 20, 30];
print(nums.len());      # 3
nums.push(40);          # [10,20,30,40]
print(nums.pop());      # 40, nums -> [10,20,30]
nums.insert_at(1, 99);  # [10,99,20,30]
print(nums.slice(1, 3));# [99,20]
nums.remove_range(0, 2);# [20,30]
nums.replace_range(1, 2, [7,8,9]); # [20,7,8,9]
```

You can also push with the **append operator**:

```cfs
var a = [];
a[] = 1; a[] = 2; a[] = 3;  # same as push
```

---

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

## Functions

```cfs
func add(a, b) { return a + b; }
print(add(15, 25)); # 40

func greet(name) { return "Hello, " + name + "!"; }
print(greet("Sam")); # Hello, Sam!
```

---

## While Loops

```cfs
var counter = 1;
while (counter <= 5) {
    print(counter);
    counter++;
}
```

---

## For Loops

```cfs
var arr = [10, 20, 30];
for (var i = 0; i < arr.len(); i++;) {
    print("Index " + i + ": " + arr[i]);
}
```

---

## If / Else

```cfs
var health = 80;
if (health >= 90) {
    print("Very healthy!");
} else if (health >= 50) {
    print("Okay.");
} else {
    print("Low health!");
}
```

---

## Match / Case

```cfs
var level = 3;
match(level) {
    case 1: { print("Beginner"); }
    case 2: { print("Intermediate"); }
    case 3: { print("Expert"); }
}
```

---

## Break & Continue

```cfs
var n = 10;
while (true) {
    n--;
    if (n % 2 == 0) { continue; }
    print(n);
    if (n <= 1) { break; }
}
```

---

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

## Classes (Instances, Statics, Inheritance)

Classes are dictionaries with attached behavior. Instances get `this`.  
There is also a **static container** (`type`) per class.

### Basic class

```cfs
class Player(name, score) {
    var level = 0;

    func setLevel(lvl) { this.level = lvl; }

    func getInfo() {
        return name + ":" + score + ":" + level;
    }
}

var hero = new Player("Lara", 250);
hero.setLevel(3);
print(hero.getInfo());  # Lara:250:3
```

### Statics

- Declare with `static var` / `static func`.
- From a **static method**, `type` refers to the class static object.
- From an **instance method**, `type` also refers to that static object (e.g., shared counters).

```cfs
class BuildInfo {
    static var version = "1.2.3";
    static func tag() { return "Build(" + type.version + ")"; }
}

print(BuildInfo.version);  # "1.2.3"
print(BuildInfo.tag());    # "Build(1.2.3)"
```

### Inheritance

Supports **single inheritance** with base-constructor forwarding, `super`, and shadowing/overriding.

```cfs
class Animal(name) {
    var Name = "";
    func init(name) { this.Name = name; }

    static var ver = "1.0";
    static func who() { return "Animal(" + type.ver + ")"; }

    func speak() { return "???"; }
    func tag()   { return "[A:" + this.Name + "]"; }
}

class Cat(name) : Animal(name) {
    var damage = 30;

    func speak() { return "meow"; }         # shadows Animal.speak()
    func superSpeak() { return super.speak(); }
    func both() { return this.speak() + "/" + super.speak(); }

    func label() { return this.tag(); }
    func superTag() { return super.tag(); }

    static var ver = "2.0";
    static func who() { return "Cat(" + type.ver + ")"; }
    static func whoBase() { return super.who(); }
}

var a = new Animal("Milo");
var c = new Cat("Nya");

print(c.speak());       # "meow"
print(c.superSpeak());  # "???"
print(c.both());        # "meow/???"
print(c.label());       # "[A:Nya]"
print(c.superTag());    # "[A:Nya]"

print(Animal.who());    # "Animal(1.0)"
print(Cat.who());       # "Cat(2.0)"
print(Cat.whoBase());   # "Animal(1.0)"
```

> **Notes**
> - “Override” is **shadowing**: if you reuse the same name in the child, it wins via `this`.  
>   You can still reach the base via `super`.
> - Inside instance methods, `this` is the instance; `type` is the static container; `super` calls the base implementation (instance or static).

# Nested classes

Nested (inner) classes are declared **inside** another class.  
They support *lexical binding* of the outer instance via the keyword `outer`.

## Key points

- Declare an inner class with `class Inner(...) { ... }` **inside** an outer class body.
- Creating an inner instance **through an outer instance** (e.g., `car.Engine(30)`) binds `outer` inside the inner object.
- Accessing the inner **through the static container** (e.g., `Auto.Engine`) exposes the type but **does not** bind `outer`.  
  Calling inner methods that reference `outer` in that case will error.

```cfs
# Outer class with an inner class that uses `outer`
class Auto(pwr) {
    var basePower = 0;
    func init(pwr) { this.basePower = pwr; }

    class Engine(boost) {
        var boost = 0;
        func init(boost) { this.boost = boost; }

        func power() {
            # `outer` refers to the enclosing Auto instance,
            # but only if Engine was created via an Auto instance.
            return outer.basePower + this.boost;
        }
    }
}

var car = Auto(100);              # create an Auto instance
var e1  = car.Engine(25);         # inner via instance -> `outer` is bound
print(e1.power());                # 125

# Accessing the nested type via the static container:
var EType = Auto.Engine;          # type reference only, no `outer` bound
var e2    = EType(null, 25);      # constructing without an outer instance ( first Parameter is the instance , in our case null since we dont use any )
# print(e2.power());              # would error: 'outer' not available
```

## Nested classes in derived types

Inner classes in subclasses can freely reference the subclass’s fields via `outer`.  
They also compose with `super`/`type` as usual (from the enclosing instance methods).

```cfs
class BMW(pwr) : Auto(pwr) {
    var brand = "BMW";
    func init(pwr) { super.init(pwr); }

    class Badge(txt) {
        var txt = "";
        func init(txt) { this.txt = txt; }

        func show() {
            # Uses state from the enclosing BMW instance
            return outer.brand + "-" + this.txt;   # e.g., "BMW-SPORT"
        }
    }
}

var bmw   = BMW(220);
var badge = bmw.Badge("SPORT");
print(badge.show());              # "BMW-SPORT"
```

### Notes

- `outer` is only available **inside inner-class methods** and only when the inner was created via an **outer instance**.
- You can still access the nested **type** through `Outer.Inner` on the static container, but any use of `outer` inside that inner will fail unless it was instantiated through an outer instance.

---

## Enums

```cfs
enum Colors {
   red = 5,
   green,
   blue
}

print(Colors.green);  # 6
```

---

## File I/O

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

## Built-ins (selected)

Common built-ins include `print`, `str`, numeric conversions, `len`, random utilities, etc.

```cfs
print("score=" + str(123));
print(abs(-5));          # 5
print(len([1,2,3]));     # 3   (works for arrays/strings/dicts)
```

> You can prefer the **method style** as well:  
> `arr.len()`, `strVal.len()`, `dict.len()`.

---

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

[Samples](CFGS_NE/Samples/) · [Built-in Functions](builtin.md)
