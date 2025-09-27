# ConfigurationScript(VM) Features

CFGS has been **completely redesigned**.  
While the previous version was a **prototype relying on a direct AST interpreter**, the new version runs on a **stack-based bytecode virtual machine** with its own VM code.  
The **syntax has been intentionally revised** to ensure smooth collaboration between the **lexer, parser, compiler, and VM**.  
This project also served to **expand and solidify my knowledge in compiler development**.  

⚠️ **Important notes:**  
- The old project will **no longer be actively developed**, but the repository remains available to **track the evolution of CFGS**, even though the foundation has now changed completely.  
- **CFGS is still under development** and may contain bugs.


## 💻 First Steps

You can execute code in two ways:

1. **Built-in REPL:**  
   - If no command-line arguments are provided, CFGS will launch an **interactive REPL**.  
   - The REPL is designed to **detect open blocks** and will only execute the code **once all blocks are properly closed**, preventing incomplete code execution.

2. **Command-line mode:**  
   You can run a script directly from the command line:
   - `cfcs [-d] <script-path>`  
     - `-d` (optional) enables **debug mode**.  
     - `<script-path>` is the path to your script.  
   - The script **file extension does not matter** at this stage.

This setup allows flexible testing and experimentation, either interactively or by running complete scripts.

---

## Table of Contents
1. [Variables](#variables)
2. [Arrays](#arrays)
3. [Dictionaries (Objects)](#dictionaries-objects)
4. [Functions](#functions)
5. [For Loops](#for-loops)
6. [While Loops](#while-loops)
7. [If / Else](#if--else)
8. [Match-Case](#match-case)
9. [Break and Continue](#break-and-continue)
10. [Closures / Functions as Values](#closures--functions-as-values)
11. [Classes / Objects](#classes--objects)
12. [Enums](#Enums)

---

## Variables
💡 Variables store values (numbers, strings, etc.)

```cfs
var score = 100;
print(score); # 100

var playerName = "Alex";
print(playerName); # Alex
```

---

## Arrays
📚 Arrays store ordered lists. Nested arrays are possible. You can push and delete items.

```cfs
var numbers = [5, 10, 15, [20, 25], 30];
for(var i = 0; i < len(numbers); i++;) {
    print(numbers[i]);
}

var nestedArray = ["A", "B", ["C", "D"]];
print(nestedArray);
print(nestedArray[2][1]); # D

# Push new item to array
numbers[] = 99;
print(numbers);

# Delete an item from array
delete numbers[1];
print(numbers);
```

---

## For Loops

```cfs
var arr = [10, 20, 30, 40, 50];
for(var x = 0; x < len(arr); x++;) {
    print("Element at index " + x + ": " + arr[x]);
}
```

---

## Dictionaries (Objects)
🗂 Key-value pairs, accessed via dot notation or `[]`. You can push new keys or delete them.

```cfs
var settings = {"volume": 70, "theme": "dark"};
print(settings.volume);
print(settings["theme"]);

settings.volume = 90;
settings["theme"] = "light";
print(settings);

# Push new key-value pair
settings[] = {"language": "EN"};
print(settings);

# Delete a key
delete settings["volume"];
print(settings);
```

---

## Functions
🔧 Reusable logic (Closures) with parameters and return values.

```cfs
func addNumbers(a, b) {
    return a + b;
}
print(addNumbers(15, 25)); # 40

func greet(name) {
    return "Hello, " + name + "!";
}
print(greet("Sam")); # Hello, Sam!
```

---

## While Loops
🔁 Repeats code as long as the condition is true.

```cfs
var counter = 1;
while(counter <= 5) {
    print(counter);
    counter++;
}
```

---

## If / Else
⚡ Conditional code execution.

```cfs
var health = 80;
if(health >= 90) {
    print("Very healthy!");
} else if(health >= 50) {
    print("Okay.");
} else {
    print("Low health!");
}
```

---

## Match-Case
🔹 Alternative to multiple `if/else if` statements.

```cfs
var level = 3;
match(level) {
    case 1: { print("Beginner"); }
    case 2: { print("Intermediate"); }
    case 3: { print("Expert"); }
}
```

---

## Break and Continue
⏹ `break` exits loops, `continue` skips the current iteration.

```cfs
var n = 10;
while(true) {
    n--;
    if(n % 2 == 0) { continue; }
    print(n);
    if(n <= 1) { break; }
}
```

---

## Closures / Functions as Values
🔒 Functions can be assigned to variables or returned.

```cfs
var multiply = func(a, b) {
    return a * b;
};
print(multiply(5, 6)); // 30

func makeAdder(x) {
    return func(y) {
        return x + y;
    };
}
var addFive = makeAdder(5);
print(addFive(10)); # 15
```

---

## Classes / Objects
🏛 Objects with properties and methods. `this` refers to the instance.

```cfs
class Player(name, score) {
    var level;
    func setLevel(lvl) {
        this.level = lvl;
    }
    func getInfo() {
        return name + ":" + score + ":" + level;
    }
}
var hero = new Player("Lara", 250);
hero.setLevel(3);
print(hero.getInfo());
```

---

## Enums
```cfs

enum colors
{
   red = 5,
   green,
   blue
}

print(colors.green); # Output 6

```

[Examples](CFGS_NE/Samples/)


[Hier geht es zur deutschen Version](Readme_de.md)

