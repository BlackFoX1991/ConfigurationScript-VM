# Classes, Enums, Namespaces, and Inheritance

## Class Overview

A class in CFGS has a name, a constructor signature in the header, and a body that can contain fields, methods, static members, enums, and even nested classes.

```cfs
class Point(x, y) {
    var x = 0;
    var y = 0;

    func init(x, y) {
        this.x = x;
        this.y = y;
    }

    func sum() {
        return this.x + this.y;
    }
}
```

The parameters in the class header define the constructor surface. The actual initialization usually happens in `init`.

## Instance Fields and Static Fields

```cfs
class Meter(v) {
    static var Scale = 10;
    var value = 0;

    func init(v) {
        this.value = v;
    }
}
```

- `var` inside a class creates instance fields.
- `static var` creates static fields on the type.

## Methods and Static Methods

```cfs
class Meter(v) {
    static var Scale = 10;
    var value = 0;

    func init(v) {
        this.value = v;
    }

    func scaled() {
        return this.value * type.Scale;
    }

    static func make(v) {
        return new Meter(v);
    }
}
```

The important receiver here is `type`. It is available in class methods and points at the current type. It works in instance methods and static methods.

## Creating Instances

```cfs
var p = new Point(3, 4);
```

If the class defines `init`, the `new` call uses it with the provided arguments.

## Object Initializers

Directly after `new`, you can override known members.

```cfs
class Config(host, port) {
    var host = "";
    var port = 0;

    func init(host, port) {
        this.host = host;
        this.port = port;
    }
}

var cfg = new Config("localhost", 80) { host: "api.local", port: 443 };
```

This is especially practical for data objects and configuration holders.

## Explicit Destruction

CFGS instances live on the managed .NET heap, so normal memory is still reclaimed by the CLR garbage collector. If you need deterministic cleanup, define an instance method named `destroy` and call the standard-library builtin `destroy(obj)`.

```cfs
class TempFile(path) {
    var path = "";
    var closed = false;

    func init(path) {
        this.path = path;
    }

    private func destroy() {
        this.closed = true;
        print("cleanup " + this.path);
    }
}

var file = new TempFile("demo.tmp");
destroy(file);
```

Important details.

- `destroy(obj)` may call a `private` destructor method.
- `destroy(obj, true)` also walks nested fields, arrays, and dictionaries recursively.
- A destroyed instance can no longer be used through member access.
- `destroy` must stay synchronous. `await` inside `destroy` is rejected.
- If a base class also needs cleanup, call `super.destroy()` explicitly from the derived destructor.

## Receivers `this`, `type`, `super`, `outer`

### `this`

`this` is available in instance methods.

```cfs
func get() {
    return this.value;
}
```

### `type`

`type` is available in class methods. That includes instance methods and static methods.

```cfs
func scaled() {
    return this.value * type.Scale;
}
```

### `super`

`super` is available in classes with a base class and works in instance methods and static methods.

```cfs
class Base(seed) {
    var seed = 0;
    func init(seed) { this.seed = seed; }
    func score(a, b = 1) { return this.seed + a + b; }
    static func tag(x = 1) { return x + 1; }
}

class Child(seed) : Base(seed) {
    func score(a, b = 1) {
        return super.score(a, b) + this.seed;
    }

    static func tag(x = 1) {
        return super.tag(x) + 1;
    }
}
```

### `outer`

`outer` is designed for nested classes and is only available inside nested instance methods.

```cfs
class Outer(seed) {
    var seed = 0;

    func init(seed) {
        this.seed = seed;
    }

    class Inner(add) {
        var add = 0;

        func init(add) {
            this.add = add;
        }

        func total() {
            return outer.seed + this.add;
        }
    }
}
```

## Implicit Member Resolution

Inside methods, you do not always have to write `this.` or `type.` explicitly. CFGS tries to resolve matching members implicitly.

The practical resolution order is this.

1. Local name or parameter.
2. Matching class member.
3. Visible global name.

This makes methods shorter. Still, explicit access is often clearer for important reads and writes.

## Inheritance

Inheritance syntax lives directly in the class header.

```cfs
class FancyCalc(seed) : BaseCalc(seed) {
    func score(a, b = 1) {
        return super.score(a, b) + this.seed;
    }
}
```

Base constructor calls may use positional arguments, named arguments, and spread.

```cfs
class NamedChild() : SpreadBase(a: 3, b: 4) {
}

class SpreadChild() : SpreadBase(*[3, 4, 9]) {
}
```

The runtime and compiler validate these calls strictly. Unknown named arguments, too few arguments, or the wrong ordering produce clear errors.

## Interfaces

CFGS also supports interface declarations with method signatures only.

```cfs
interface IAnimal {
    func speak();
}

interface IWalker {
    func steps();
}

interface IPet : IAnimal {
    func pet_name();
}
```

Important rules.

- Interfaces may inherit from one or more interfaces with `:`.
- Interface bodies may contain only `func ...;` or `async func ...;` signatures.
- Interface members are instance methods only. No fields, no static members, no default bodies.
- Signature compatibility checks include arity, optional/default parameter shape, rest-parameter usage, and `async`.

Classes implement interfaces in the class header. The base class, if present, comes first, followed by interfaces.

```cfs
class Dog(name) : Animal(name), IPet, IWalker {
    func speak() { return "woof"; }
    func pet_name() { return this.name; }
    func steps() { return 4; }
}
```

An inherited public instance method may satisfy an interface contract.

```cfs
class Creature(name) {
    func pet_name() { return this.name; }
}

class Dog(name) : Creature(name), IPet {
    func speak() { return "woof"; }
}
```

## Interfaces in Namespaces

Interfaces can also live inside namespaces and are accessed through qualified names.

```cfs
namespace App.Contracts {
    interface ITagged {
        func tag();
    }
}

class User(id) : App.Contracts.ITagged {
    func tag() { return "user:" + str(id); }
}
```

## Visibility

CFGS supports three visibility levels.

- `public`
- `private`
- `protected`

This applies to these member categories.

- instance fields
- static fields
- methods
- static methods
- constructors through `init`
- enums inside classes
- nested classes

Example.

```cfs
class AccessBase(v) {
    private var secret = 0;
    protected var prot = 0;
    public var open = 0;

    private static var sPriv = 11;
    protected static var sProt = 22;

    protected func init(v) {
        this.secret = v;
        this.prot = v + 1;
        this.open = v + 2;
    }
}
```

Private constructors are useful for factory patterns.

```cfs
class FactoryOnly(v) {
    var x = 0;

    private func init(v) {
        this.x = v;
    }

    static func make(v) {
        return new FactoryOnly(v);
    }
}
```

## Const Fields

Fields can be declared as `const` to prevent reassignment after initialization. This works for both instance fields and static fields.

### Instance Const Fields

Instance const fields are set during construction and cannot be changed afterward.

```cfs
class User(id, name) {
    const id;
    var name;

    func init(id, name) {
        this.id = id;
        this.name = name;
    }
}

var u = new User(1, "Max");
u.name = "Moritz";  // ok
// u.id = 2;        // Runtime error: cannot assign to const field 'id'
```

Const fields can also have an initializer directly in the class body. The `var` keyword is optional when `const` is used.

```cfs
class Config() {
    const version = "1.0";
    const var maxRetries = 3;
}
```

### Static Const Fields

Static const fields are set when the class is created and cannot be changed afterward.

```cfs
class Math() {
    static const PI = 3.14159265;
    static const E = 2.71828182;
}

print(Math.PI);
// Math.PI = 0;  // Runtime error: cannot assign to const static field 'PI'
```

### Visibility and Const

`const` combines with visibility modifiers in any order.

```cfs
class Token(value) {
    public const value;
    private const secret = "internal";

    func init(value) {
        this.value = value;
    }
}
```

## Override Rules

Overrides are validated strictly. The compiler checks several things.

- Member kind must match. Instance against instance. Static against static.
- Parameter shape and arity must be compatible.
- Visibility must not become narrower.
- Field against method, or method against field, is not a valid override.

This leads to a predictable and stable OOP layer.

## Enums

Enums can exist at top level or inside a class.

```cfs
enum Mode {
    Fast = 1,
    Safe = 2
}
```

Or inside a class.

```cfs
class Point(x, y) {
    enum Kind {
        A = 1,
        B = 2
    }
}
```

Access works through indexing or qualified access.

```cfs
Mode["Safe"]
Point.Kind["B"]
```

In many cases dot access also works because dot access is internally resolved as named member lookup.

## Namespaces

Namespaces structure larger surfaces.

```cfs
namespace App.Tools {
    func clamp(x, lo, hi) {
        if (x < lo) { return lo; }
        if (x > hi) { return hi; }
        return x;
    }

    class Box(v) {
        var value = 0;
        func init(v) { this.value = v; }
        func get() { return this.value; }
    }
}
```

Access is qualified.

```cfs
App.Tools.clamp(99, 0, 10)
new App.Tools.Box(7)
```

Important practical notes.

- Qualified namespace names such as `A.B.C` are allowed.
- Multiple `namespace App.Tools { ... }` declarations can extend the same namespace.
- Namespace bodies may contain functions, classes, interfaces, enums, variables, and constants.
- `import` is not allowed inside a namespace body.
- `export` is not allowed inside a namespace body.
- A namespace root must not collide with an already declared top level symbol.

## Type Checking with `is`

The `is` operator checks whether an object matches a class or interface type. For classes it walks the base-class chain. For interfaces it also walks implemented interfaces and inherited interfaces transitively.

```cfs
class Thing() {}
class Car() : Thing() {}

interface IVehicle {
    func drive();
}

class Truck() : Car(), IVehicle {
    func drive() { }
}

var c = new Car();
print(c is Car);      // true
print(c is Thing);    // true (Car inherits from Thing)

var t = new Truck();
print(t is IVehicle); // true
```

If the left side is not a class instance or the right side is not a class or interface type, `is` returns `false` without throwing an error.

```cfs
print("hello" is Car);  // false
print(42 is Car);        // false
```

## Reserved Names

These names are semantically reserved and should not be used as normal bindings.

- `this`
- `type`
- `super`
- `outer`

Internal runtime slots such as `__type`, `__base`, `__interfaces`, `__is_interface`, and `__outer` also belong to the VM, not to your application model.

## Combined Example

```cfs
namespace App.Model {
    class Counter(seed) {
        static var Created = 0;
        var value = 0;

        func init(seed) {
            this.value = seed;
            type.Created = type.Created + 1;
        }

        func inc() {
            value = value + 1;
            return value;
        }

        static func make(seed = 0) {
            return new Counter(seed);
        }
    }
}

var c = App.Model.Counter.make(5);
print(c.inc());
print(App.Model.Counter.Created);
```

Once you start using classes, modules usually follow right after. The next chapter shows how to export surfaces, import them again, and use namespace style imports.
