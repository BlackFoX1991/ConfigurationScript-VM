# Introduction

Here’s a brief introduction to **Configuration-Language**.

## Origins

**Configuration-Language (CFGS)** grew out of my earlier interpreters/compilers: **LemonVM**, **Codium**, and **Symvl**. All three were more like proofs of concept that helped me solidify my understanding of compiler development. After I wrote the first CFGS concept as an interpreter—mostly for fun—the idea evolved to merge **Codium** (a half-finished VM) and **Symvl** (an interpreter written in C++) into a newly designed project. CFGS is still far from its final vision, but thanks to my prior experience, development is progressing rapidly.

## What is Configuration-Language?

The language was created with the same spirit as Codium. Much like Lua or Python, it aims to let you write scripts *on the fly* to process data with minimal effort. Thanks to its dynamic nature and C-like syntax, there’s little adjustment required for most developers. This language isn’t meant to compete with Python or Lua—it’s simply an alternative.

CFGS is built on **.NET** and can be embedded into .NET-based projects. That was one of my main goals as a .NET developer. As of **October 15, 2025**, I’m proud to provide a stable foundation and to help others better understand how a compiler works.

## Features

CFGS offers a solid base of capabilities:

- Algebraic expressions
- Ternary Operator
- Null Coelscing Operator
- Array
- Dictionary
- File I/O ( stdlib )
- HTTP Capabilties ( httplib )
- Variables
- Functions
- Closures
- Classes, including inheritance and primary constructors
- Control structures such as loops and conditionals
- Enums

---

# Quick Start

First, download the latest release from this page and extract **CFGS** wherever you prefer. You can either open CFGS directly to launch the so-called **REPL** (*Read–Eval–Print Loop*), or you can run a script file as shown later. Let’s start with the REPL.

Start CFGS:  
![step 1](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c061d94dbff1431553e344e58c8906d1f0d55cdd/CFGS_NE/Tutorial/REPL_0.PNG)

Enter `print` followed by a string literal containing the text `"Hello World!"`. Always include parentheses and terminate each standalone statement with a semicolon.  
![step 2](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c061d94dbff1431553e344e58c8906d1f0d55cdd/CFGS_NE/Tutorial/REPL_1.PNG)

Confirm by pressing **Enter**:  
![step 3](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c061d94dbff1431553e344e58c8906d1f0d55cdd/CFGS_NE/Tutorial/REPL_2.PNG)

As you can see, the code executed successfully. Here’s another example that shows an error—this happens if you forget the semicolon:  
![error](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c48644e3840cd510c1c6feb8ebf0d658335794df/CFGS_NE/Tutorial/REPL_ERROR.PNG)

You can also make execution more transparent by enabling **debug mode**, which lets you peek into the VM.  
(When enabled, the debug mode also writes a log file into the script’s directory with additional details.)  
![debug mode](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c48644e3840cd510c1c6feb8ebf0d658335794df/CFGS_NE/Tutorial/REPL_3.PNG)  
![execution debug](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c48644e3840cd510c1c6feb8ebf0d658335794df/CFGS_NE/Tutorial/REPL_4.PNG)

We’ve enabled debug mode and re-ran the same code as before. Now you can see an instruction list that shows, in detail, which VM commands are executed. The log file will look like this:

```
[DEBUG] 0 ->  IP=0, STACK=[], SCOPES=1, CALLSTACK=0
[DEBUG] JMP 1 (Line 0, Col 0)
[DEBUG] 1 ->  IP=1, STACK=[], SCOPES=1, CALLSTACK=0
[DEBUG] LOAD_VAR print (Line 1, Col 6)
[DEBUG] 1 ->  IP=2, STACK=[<builtin print/1..1>], SCOPES=1, CALLSTACK=0
[DEBUG] PUSH_STR Hello World! (Line 1, Col 22)
[DEBUG] 1 ->  IP=3, STACK=[<builtin print/1..1>, Hello World!], SCOPES=1, CALLSTACK=0
[DEBUG] CALL_INDIRECT 1 (Line 1, Col 6)
[DEBUG] 1 ->  IP=4, STACK=[1], SCOPES=1, CALLSTACK=0
[DEBUG] POP (Line 1, Col 6)
[DEBUG] 0 ->  IP=5, STACK=[], SCOPES=1, CALLSTACK=0
[DEBUG] HALT (Line 0, Col 0)
```

You’ll also find small snapshots of the stack state. If unexpected errors occur, this log helps you trace what happened and why a crash occurred.

---

Now let’s create and run a script from a file. An IDE is still planned; I’m also working on a VS Code integration. For now, we’ll just use **Notepad++** and set the syntax highlighting to **C**, **C++**, or **C#**—that’s close enough. Of course, you can use any editor you like.

Create a file named `tutor.cfs` (feel free to choose a different name) and add some code, for example:  
![Code Sample](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c3c7dce8a0a510b77ebbd9e80eaf6150d4a3bfcd/CFGS_NE/Tutorial/Code_Sample.PNG)

Save your code. You now have a few options:

- Add the path to `cfgs.exe` to your system variables so you don’t need to type the full path every time.
- Open a terminal and provide the path to `cfgs.exe` (drag-and-drop works nicely).
- Associate the `.cfs` file extension with `cfgs.exe`.

For this example, open **Windows Terminal** and drag `cfgs.exe` into the console to insert its full path automatically. Then add a space and drag in `tutor.cfs`. Before proceeding, there are two additional parameters worth knowing:

- Compile with `-c`
- Debug with `-d` or `--debug`

These parameters can appear anywhere in the command line, and they apply to all files you pass to CFGS in that command. A few important notes:

- The compiled binary format for `cfs` is `cfb`. If you provide `-c` for a `.cfb` file, the flag is ignored automatically.
- There’s also a legacy `-b` parameter that was originally intended to mark files as `cfb` when they didn’t have the `.cfb` extension. This flag is now obsolete because CFGS decides that automatically to avoid mistakes.
- When you compile a `.cfs` file, the complete code—including everything from `import` directives—is packaged into a single `.cfb` file. A `.cfb` file is packed bytecode for the CFGS VM.
- Compiled files aren’t natively executable; they require CFGS and any libraries you use. For example, if your script uses functions from `stdlib` or `httplib`, make sure those libraries are present in CFGS’s `plugins` folder.
- Internally, CFGS keeps its own execution path while switching into the respective script directories when running your scripts. This ensures libraries load correctly and that relative imports work if they sit next to your script.
- You can also use absolute or relative paths in your scripts if you prefer.

Here’s an example command line:

```
cfgs.exe "C:\Scripts\tutor.cfs" -d -c
```

This runs `tutor.cfs` in debug mode **and** compiles it. The resulting `.cfb` file is written to the script directory. Parameters like `-c` or `-d` have no fixed positions and can be provided in any order. They’re also optional—use them only if needed.

After running the file, the output should look like this:  
![Code Output](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c3c7dce8a0a510b77ebbd9e80eaf6150d4a3bfcd/CFGS_NE/Tutorial/Output_Sample.PNG)

If you need more help, you can find detailed information on programming with CFGS **[here](https://github.com/BlackFoX1991/ConfigurationScript-VM/blob/c3c7dce8a0a510b77ebbd9e80eaf6150d4a3bfcd/README.md)**.

Have fun! If you have questions, I’m happy to help.
