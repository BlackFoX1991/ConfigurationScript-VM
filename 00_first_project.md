# Your First CFGS Project

This guide starts with a ready-to-run `CFGS_VM.exe` on Windows. You will create a project folder, add a script, run it, and use the VS Code extension.

> **Project creation in CFGS:** `init` creates a `cfgs.json` manifest. It does not generate source files or show a project wizard. You create `main.cfs` yourself in the next step.

## 1. Create and open the project folder

In File Explorer, create a new folder named `hello-cfgs` somewhere outside an existing CFGS project. Open **that folder** in VS Code with **File → Open Folder…**. If you use another editor, open the same folder there.

Open a terminal in the project folder. In VS Code, choose **Terminal → New Terminal**. The terminal's current directory should be `hello-cfgs`. The commands below assume `CFGS_VM.exe` is available as a command; if it is not on your PATH, use the full path to your compiled executable instead.

## 2. Initialize the project

In that terminal, enter:

```text
CFGS_VM.exe init
```

CFGS reports the path of the new `cfgs.json` and the project name. The manifest starts like this:

```json
{
  "name": "hello-cfgs",
  "version": "0.1.0",
  "plugins": []
}
```

The version in this file belongs to **your project**. A lockfile and `.cfgs/plugins` appear only when you add and restore a plugin. If an ancestor folder already contains `cfgs.json`, `init` uses that existing project root; create `hello-cfgs` outside it.

## 3. Create `main.cfs`

In the VS Code Explorer, choose **New File**, name it `main.cfs`, and paste:

```cfs
func main() {
    print("Hello from CFGS");
}

main();
```

Save the file. CFGS executes top-level statements, but it does not automatically call a function named `main`; the final `main();` line runs it.

## 4. Check and run the script

In the terminal already open in `hello-cfgs`, run:

```text
CFGS_VM.exe -check main.cfs
CFGS_VM.exe main.cfs
```

The first command reports `[CHECK] ok` without executing the script. The second prints:

```text
Hello from CFGS
```

For style checks, use `CFGS_VM.exe -lint main.cfs`. To apply the formatter, use `CFGS_VM.exe -format main.cfs --write`.

## 5. Enable the VS Code tools (optional)

If **CFGS Language Tools** is not installed, open VS Code's **Extensions** view, choose **Install from VSIX…**, and select the extension package. The [VS Code guide](13_visual_studio_code_and_lsp.md#install-the-vs-code-extension) explains this in more detail.

Open `main.cfs`. The language mode in the status bar should be **CFGS**. Syntax highlighting, diagnostics, completion, and formatting then work in the editor. Open **View → Command Palette** for `CFGS: Lint Current File`, `CFGS: Format Current File`, and `CFGS: Restart Language Server`.

The extension handles editing. `CFGS_VM.exe` runs the script in the terminal.

## 6. Add a module (optional)

In the VS Code Explorer, create a folder named `Modules`. Inside it, create `Greeter.cfs` with this content:

```cfs
namespace Hello.App {
    class Greeter(person) {
        property prefix { get; set; } = "Hello";
        func message() { return this.prefix + ", " + this.person + "!"; }
    }
}
```

Replace the content of `main.cfs` with:

```cfs
import "Modules/Greeter.cfs";

var greeter = new Hello.App.Greeter("CFGS");
print(greeter.message());
```

Save both files, then run the same check and run commands again. The output is `Hello, CFGS!`. The import path is relative to `main.cfs`. For more on this code, read [Classes and Namespaces](06_classes_enums_namespaces_and_inheritance.md) and [Modules and Imports](07_modules_imports_and_exports.md).

Your project now looks like this:

```text
hello-cfgs/
├── cfgs.json
├── main.cfs
└── Modules/
    └── Greeter.cfs
```

![Illustrated project folder and successful CFGS terminal run](assets/docs/first-project-workflow.svg)

*Illustration based on the verified CLI output after adding the module. Your paths may differ.*

## If something does not work

| Symptom | Check |
| --- | --- |
| `CFGS_VM.exe` is not found | Use the full path to the compiled executable or add its folder to your PATH. |
| `init` reports an unexpected project folder | Check whether a parent directory already contains `cfgs.json`. |
| `import path not found` | Check the path relative to the importing `.cfs` file and open the project folder itself in VS Code. |
| `.cfs` has no highlighting | Check that CFGS Language Tools is installed and that VS Code recognizes the file as CFGS. |
| The terminal prints nothing | Check that `main();` is present, or put a `print(...)` statement at top level. |
| The linter warns about tabs (`CFGS8002`) | Use spaces or deliberately configure both formatter and linter for tabs. See [VS Code settings](13_visual_studio_code_and_lsp.md#optional-vs-code-settings). |

For CLI options, working-directory rules, the REPL, and compiled `.cfb` files, continue with [Getting Started and Running Scripts](01_getting_started_and_running.md). For plugin dependencies, see [Package and Dependency Management](20_package_dependency_management.md).
