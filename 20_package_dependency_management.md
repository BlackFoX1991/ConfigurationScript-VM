# Package and Dependency Management

CFGS supports a project-local dependency workflow for plugin DLLs.

The first implementation is intentionally local. It does not download from a remote registry. It gives projects an official manifest, lockfile, restore folder, and CLI workflow that a registry can build on later.

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
      "name": "io",
      "version": "5.3.4",
      "path": "dist/Debug/net10.0/CFGS.IO.dll",
      "includeDependencies": true
    }
  ]
}
```

`path` may be absolute or relative to the manifest directory. `includeDependencies` copies adjacent plugin artifacts, including DLLs, JSON runtime metadata, PDBs, and a `runtimes` folder when present.

## Init

Create a manifest in the current directory.

```powershell
dotnet run --project CFGS_NE -- init
```

Use `--project <dir>` to target a specific project directory.

```powershell
dotnet run --project CFGS_NE -- init --project .\my-project
```

## Install

Install adds or updates a local plugin dependency in `cfgs.json`.

```powershell
dotnet run --project CFGS_NE -- install .\plugins\MyPlugin.dll --name my-plugin --version 1.0.0
```

By default, install also runs restore. Use `--no-restore` to only edit the manifest.

```powershell
dotnet run --project CFGS_NE -- install .\plugins\MyPlugin.dll --no-restore
```

Use `--no-dependencies` when only the declared DLL should be copied.

## Restore

Restore reads `cfgs.json`, copies plugin artifacts into `.cfgs/plugins`, and writes `cfgs.lock.json`.

```powershell
dotnet run --project CFGS_NE -- restore
```

The lockfile records plugin name, version, source, installed path, SHA-256 hash, and dependency-copy mode.
When a DLL listed in `cfgs.lock.json` is loaded from `.cfgs/plugins`, CFGS verifies the installed artifact hash before loading it. This applies to plugin imports and to `import native` alike. A mismatch fails fast and should be fixed by running restore again from the trusted source.

DLL references stored in a `.cfb` artifact are reduced to bare file names, both when the artifact is written and when it is read back. A compiled artifact can therefore name which DLL it needs, but it cannot direct the loader at a path of its own choosing.

## Listing

Use `deps list` to inspect manifest dependencies and lock status.

```powershell
dotnet run --project CFGS_NE -- deps list
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
