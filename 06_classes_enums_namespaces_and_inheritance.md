# Classes, Enums, Namespaces, and Inheritance

## Class Overview

A class in CFGS has a name, a constructor signature in the header, and a body that can contain fields, properties, methods, static members, enums, and even nested classes.

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

## Properties

CFGS supports instance and static properties with explicit accessor metadata. A property may declare `get`, `set`, and `init` accessors.

```cfs
class Person(seed) {
    property name { get; private set; } = "";
    property normalized {
        get;
        set(value) { field = value.trim(); }
    } = "";

    func init(seed) {
        this.name = seed;
        this.normalized = seed;
    }
}
```

Important rules.

- `property x { get; set; }` creates an auto-property with hidden backing storage.
- `property x { get { ... } set(value) { ... } }` creates a fully manual property.
- Mixed forms are allowed. Example: auto `get` with manual `set`, or manual `get` with auto `set`.
- `init` is write-only and is intended for object initialization.
- A property initializer such as `= expr` is allowed when at least one accessor is auto-implemented.
- `static property` works the same way, but the property lives on the type instead of the instance.

### Auto-Properties and the `field` Backing Symbol

If a property has hidden backing storage, accessor bodies may use the special identifier `field`.

```cfs
class Settings() {
    property endpoint {
        get;
        set(value) { field = value.trim(); }
    } = "https://localhost";
}
```

Important details.

- `field` is only available inside property accessors that actually have hidden backing storage.
- Hidden backing storage exists when at least one accessor is auto-implemented.
- In purely manual properties, `field` is rejected.
- Inside such accessors, `field` is reserved. Do not use it as an accessor parameter or local variable name.

### Accessor Visibility

Accessor visibility may be narrower than the property visibility, but never wider.

```cfs
class Secret(seed) {
    public property value {
        get;
        private set;
    } = "";

    func init(seed) {
        this.value = seed;
    }
}
```

This means outside callers can read `value`, while only code with access to the private setter may assign to it.

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

Static class members use dot access, for example `Meter.make(10)`. Bracket access such as `Meter["make"]` is dictionary or index access and is not a class member lookup. Constructors are invoked with `new Meter(...)`; they are not exposed as a `"new"` static member.

## Creating Instances

```cfs
var p = new Point(3, 4);
```

If the class defines `init`, the `new` call uses it with the provided arguments.

## Object Initializers

Directly after `new`, you can set known members through an object initializer.

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

Object initializers also work with properties. CFGS first tries a matching `init` accessor, and if none exists it falls back to `set`.

```cfs
class Settings() {
    property host { get; set; } = "localhost";
    property port { get; init; } = 80;
}

var settings = new Settings() { host: "api.local", port: 443 };
```

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

CFGS also supports interface declarations with method signatures and property signatures.

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

interface INamed {
    property name { get; }
}
```

Important rules.

- Interfaces may inherit from one or more interfaces with `:`.
- Interface bodies may contain `func ...;`, `async func ...;`, and `property ... { ... }` signatures.
- Interface members are instance methods and instance properties only. No fields, no static members, no default bodies.
- Interface properties declare accessor shape only. Example: `property x { get; }` or `property x { get; set; }`.
- Signature compatibility checks include arity, optional/default parameter shape, rest-parameter usage, and `async`.
- Property compatibility checks include the required accessor set and public accessor visibility where the interface requires it.
- CFGS does not use a separate `implements` keyword. Interface names go directly into the class header after `:`.

Classes implement interfaces in the class header. The base class, if present, comes first, followed by interfaces.

```cfs
class Dog(name) : Animal(name), IPet, IWalker {
    func speak() { return "woof"; }
    func pet_name() { return this.name; }
    func steps() { return 4; }
}
```

An inherited public instance method or property may satisfy an interface contract.

```cfs
class Creature(name) {
    func pet_name() { return this.name; }
}

class Dog(name) : Creature(name), IPet {
    func speak() { return "woof"; }
}
```

### Detailed Example: Multiple Interfaces and Inherited Implementations

This example shows several important points together.

- `IPet` inherits from `IAnimal`.
- `IWorkingPet` inherits from both `IPet` and `IWalker`.
- `Dog` satisfies `pet_name()` through its base class.
- `RobotDog` inherits the `IWorkingPet` contract through `Dog` and adds one more async contract.

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

interface IWorkingPet : IPet, IWalker {
}

interface IAsyncNamed {
    async func fetch_name();
}

class Creature(name) {
    var name = "";

    func init(name) {
        this.name = name;
    }

    func pet_name() {
        return this.name;
    }
}

class Dog(name) : Creature(name), IWorkingPet {
    func speak() {
        return "woof";
    }

    func steps() {
        return 4;
    }
}

class RobotDog(name) : Dog(name), IAsyncNamed {
    async func fetch_name() {
        yield;
        return this.pet_name();
    }
}
```

In that example, `Dog` must provide `speak()` and `steps()`, while `pet_name()` is already inherited from `Creature`. `RobotDog` automatically remains an `IWorkingPet` because it derives from `Dog`, and it additionally satisfies `IAsyncNamed`.

### Detailed Example: Optional Parameters, Rest Parameters, and `async`

Interface checks are not limited to the method name. The compiler also validates the callable shape.

```cfs
interface ILogger {
    func write(level, message = "", ...tags);
}

interface IDataSource {
    async func fetch(id, includeMeta = false);
}

class ConsoleLogger() : ILogger {
    func write(level, message = "", ...tags) {
        print(level + ": " + message);
        print(tags);
    }
}

class UserApi() : IDataSource {
    async func fetch(id, includeMeta = false) {
        yield;
        if (includeMeta) {
            return { "id": id, "meta": true };
        }

        return { "id": id };
    }
}
```

These implementations compile because the signatures match the contracts exactly enough.

- `write` keeps the same required argument count, optional/default parameter shape, and rest parameter.
- `fetch` stays `async` and keeps the same optional parameter shape.

### Common Invalid Interface Declarations

The following forms are rejected.

```cfs
# invalid: interface bodies cannot contain fields
interface IBadFields {
    var value = 0;
}

# invalid: interface bodies cannot contain method bodies
interface IBadBody {
    func run() {
        return 1;
    }
}

# invalid: interface property accessors cannot have bodies
interface IBadProperty {
    property name {
        get {
            return "x";
        }
    }
}

# invalid: interface members are instance methods and instance properties only
interface IBadStatic {
    static func make();
}
```

These class headers are also invalid.

```cfs
interface IClosable {
    func close();
}

class HiddenCloser() : IClosable {
    private func close() {
    }
}

class Base() {
}

interface IWrong : Base {
    func ping();
}

class WrongOrder() : IClosable, Base() {
    func close() {
    }
}
```

Why they fail.

- `HiddenCloser` fails because interface methods must be implemented as public instance methods.
- Interface properties likewise require public instance accessors where the contract requires them.
- `IWrong` fails because interfaces may only inherit from interfaces, never from classes.
- `WrongOrder` fails because a base class must appear before interfaces in the class header.

## Interfaces in Namespaces

Interfaces can also live inside namespaces and are accessed through qualified names.

```cfs
namespace App.Contracts {
    interface ITagged {
        func tag();
    }
}

class User(idValue) : App.Contracts.ITagged {
    var idValue = 0;

    func init(idValue) {
        this.idValue = idValue;
    }

    func tag() {
        return "user:" + str(this.idValue);
    }
}
```

The same qualified form can be used for interface inheritance and for runtime checks.

```cfs
namespace App.Contracts {
    interface IEntity {
        func id();
    }

    interface ITagged : IEntity {
        func tag();
    }
}

class User(idValue) : App.Contracts.ITagged {
    var idValue = 0;

    func init(idValue) {
        this.idValue = idValue;
    }

    func id() {
        return this.idValue;
    }

    func tag() {
        return "user:" + str(this.id());
    }
}

var user = new User(42);
print(user is App.Contracts.ITagged); // true
print(user is App.Contracts.IEntity); // true
```

## Visibility

CFGS supports three visibility levels.

- `public`
- `private`
- `protected`

This applies to these member categories.

- instance fields
- static fields
- instance properties
- static properties
- methods
- static methods
- constructors through `init`
- enums inside classes
- nested classes

For properties, the property declaration has its own visibility, and each accessor may optionally declare a narrower visibility.

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

Overriding is implicit by redeclaring a member with the same name in a derived class. There is no separate `override` keyword in CFGS.

- Member kind must match. Instance against instance. Static against static.
- Parameter shape and arity must be compatible.
- Visibility must not become narrower.
- Property accessor shape must match across overrides.
- Property accessor visibility must not become narrower.
- Field against method, method against property, or field against property is not a valid override.

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

If you want shorter lookup syntax in a file, you can add header-only `use` directives.

```cfs
import "CFGS.StandardLibrary.dll";
import "_imports/use_feature_valid.cfs";

use App.Core.Tools;
use App.Core;

print(ping());

var box = new Box(7);
print(box.get());
print(Math.plus(2, 3));
print(box is Box);
```

`use App.Core.Tools;` brings the direct members of that namespace into unqualified lookup.
`use App.Core;` also makes direct child namespaces of `App.Core` available, so `Math.plus(...)` works without writing `App.Core.Math.plus(...)`.

File-scoped namespace syntax is also supported.

```cfs
namespace App.Tools;

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
```

This is equivalent to putting the same declarations into `namespace App.Tools { ... }`.

Important practical notes.

- Qualified namespace names such as `A.B.C` are allowed.
- Multiple `namespace App.Tools { ... }` declarations can extend the same namespace.
- File-scoped syntax `namespace App.Tools;` is allowed and applies to the remaining declarations in that file.
- A file-scoped namespace must be the first declaration after the import header.
- `use A.B.C;` is allowed in the file header after `import` directives and before the first declaration.
- Multiple `use` directives can be combined.
- `use` is a lookup shortcut, not a second import system.
- Ambiguous names from multiple `use` directives fail on first actual reference.
- `use` currently shortens expression-side lookup such as calls, `new`, `is`, and child-namespace access.
- Namespace bodies may contain functions, classes, interfaces, enums, variables, and constants.
- In file-scoped namespaces, the same declaration-only rule applies to the rest of the file.
- `import` is not allowed inside a namespace body.
- `use` is not allowed inside a namespace body.
- `import` stays in the file header and is not part of namespace syntax.
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

The check also works transitively across interface inheritance and class inheritance.

```cfs
interface IAnimal {
    func speak();
}

interface IPet : IAnimal {
    func pet_name();
}

class Creature(name) {
    var name = "";

    func init(name) {
        this.name = name;
    }

    func pet_name() {
        return this.name;
    }
}

class Dog(name) : Creature(name), IPet {
    func speak() {
        return "woof";
    }
}

class ShowDog(name) : Dog(name) {
}

var d = new ShowDog("Milo");
print(d is ShowDog); // true
print(d is Dog);     // true
print(d is Creature); // true
print(d is IPet);    // true
print(d is IAnimal); // true
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

Inside property accessors with hidden backing storage, `field` is also reserved for the property's backing value.

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
