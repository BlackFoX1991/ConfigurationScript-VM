# TheVault in CFGS

This folder contains a CFGS implementation of the `TheVault` idea:

- encrypted vault metadata and index
- encrypted virtual file objects
- RSA key containers
- virtual filesystem paths
- ACL based rights
- signed journal entries
- command line interface
- command based REPL
- modular CFGS source split across namespaces, classes, interfaces, properties and async functions

The script is intentionally a native CFGS vault format. It is not a binary compatible reader for the original C# `TheVault` files.

## Run

```powershell
dotnet C:\Users\Trainee5\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll .\main.cfs -p help
```

## CLI Examples

```powershell
dotnet C:\Users\Trainee5\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll .\main.cfs -p init --path C:\Temp\CfgsVault --pass secret
dotnet C:\Users\Trainee5\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll .\main.cfs -p mkdir --path C:\Temp\CfgsVault --pass secret --vpath /root/docs
dotnet C:\Users\Trainee5\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll .\main.cfs -p write --path C:\Temp\CfgsVault --pass secret --vpath /root/docs/a.txt --text "hello vault"
dotnet C:\Users\Trainee5\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll .\main.cfs -p cat --path C:\Temp\CfgsVault --pass secret --vpath /root/docs/a.txt
dotnet C:\Users\Trainee5\source\repos\BlackFoX1991\ConfigurationScript-VM\dist\Debug\net10.0\CFGS_VM.dll .\main.cfs -p repl --path C:\Temp\CfgsVault --pass secret
```

## REPL Commands

- `pwd`
- `whoami`
- `cd <vpath>`
- `ls [vpath]`
- `tree [vpath]`
- `mkdir <vpath>`
- `cat <vpath>`
- `write <vpath> <text>`
- `import <source-file-or-dir> [vpath]`
- `export <vpath> <output-file>`
- `grant <vpath> <public.pem> [rights]`
- `users`
- `info`
- `clear`
- `exit`

## Project Layout

- `main.cfs` loads plugins and starts the app.
- `src/core.cfs` contains shared contracts, enums, logging and text helpers.
- `src/vfs.cfs` contains virtual path handling and node factories.
- `src/security.cfs` contains PBKDF2, AES-GCM and RSA key-container helpers.
- `src/storage.cfs` contains filesystem and encrypted index/object persistence.
- `src/services.cfs` contains the vault domain service.
- `src/cli.cfs` contains command parsing, help, CLI dispatch and REPL.
- `scripts/selftest.cfs` runs a compact end-to-end smoke test.
