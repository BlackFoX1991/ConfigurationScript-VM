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
---
[← Back to README](./README.md)
