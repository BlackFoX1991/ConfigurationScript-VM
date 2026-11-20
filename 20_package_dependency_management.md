# Package and Dependency Management

CFGS manages local plugin DLLs through a project manifest. The examples assume that `CFGS_VM.exe` is already available as a command. Open a terminal in your project folder before running the commands. Use the full path to the executable if it is not on your PATH. Plugin packages are loaded from local files; there is no package download service.

## Files

- `cfgs.json` is the project manifest.
- `cfgs.lock.json` is the reproducible restore snapshot.
- `.cfgs/plugins` is the local restore directory for plugin artifacts.

## Manifest

A minimal manifest looks like this.

```json
{
  "name": "demo",
  "version": "0.1.0",
  "plugins": [
    {
      "name": "my-plugin",
      "version": "1.0.0",
      "path": "plugins/MyPlugin.dll",
      "includeDependencies": true
    }
  ]
}
```

`path` may be absolute or relative to the folder containing `cfgs.json`. `includeDependencies` also copies files the plugin needs from beside its DLL.

## Init

Create a manifest in the current directory. `init` does not generate `main.cfs`, a project template, or a VS Code workspace; create source files separately. For a complete first project, follow [Your First CFGS Project](00_first_project.md).

```text
CFGS_VM.exe init
```

Use `--project <dir>` to target a specific directory. Run this from a folder outside an existing CFGS project: if a parent contains `cfgs.json`, `init` uses that project instead.

```text
CFGS_VM.exe init --project my-project
```

## Install

Install adds or updates a local plugin dependency in `cfgs.json`.

```text
CFGS_VM.exe install plugins/MyPlugin.dll --name my-plugin --version 1.0.0
```

By default, install also runs restore. Use `--no-restore` to update the manifest without restoring yet. CFGS rejects a missing or invalid DLL and leaves the existing manifest in place if installation fails.

```text
CFGS_VM.exe install plugins/MyPlugin.dll --no-restore
```

Use `--no-dependencies` when only the declared DLL should be copied.

## Restore

Restore reads `cfgs.json`, copies plugin files into `.cfgs/plugins`, and writes `cfgs.lock.json`. If two plugins provide different files with the same name and path, restore reports the conflict and leaves the installed files in place.

```text
CFGS_VM.exe restore
```

CFGS checks restored plugin files before loading them. If you change a plugin DLL or its dependencies, run `CFGS_VM.exe restore` again instead of editing `.cfgs/plugins` by hand. Run restore after upgrading an older project lockfile as well.

## Listing

Use `deps list` to inspect manifest dependencies and lock status.

```text
CFGS_VM.exe deps list
```

## Importing Restored Plugins

After restore, scripts can import a plugin by DLL file name.

```cfs
import "MyPlugin.dll";
```

Import resolution searches the script directory, package restore directories such as `.cfgs/plugins`, the working directory, and the CLI directory.

## Lockfile Policy

Commit both `cfgs.json` and `cfgs.lock.json` for applications. This gives CI and other machines the same declared plugin versions and hashes, and it enables load-time integrity checks for restored plugin artifacts.

Do not commit `.cfgs/plugins` unless you intentionally vendor binary artifacts into the repository.
